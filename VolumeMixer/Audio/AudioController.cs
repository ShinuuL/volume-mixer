using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using VolumeMixer.Infrastructure;
using VolumeMixer.Models;

namespace VolumeMixer.Audio;

public sealed partial class AudioController : IAudioController
{
    private readonly SynchronizationContext? _syncContext;
    private readonly ComCallbacks _callbacks;
    private readonly ComDispatcher _dispatcher;
    private readonly SessionNotificationSink _sessionNotificationSink;
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _endpoint;
    private IAudioSessionManager2? _sessionManager;
    private IAudioSessionEnumerator? _sessionEnumerator;
    private readonly Dictionary<int, List<SessionEntry>> _sessions = new();
    private static readonly ConcurrentDictionary<string, byte[]> IconCacheByExe = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    // Polling de reconciliação: os callbacks COM de sessão do Windows nem sempre
    // disparam quando novas sessões surgem (OnSessionCreated é notoriamente flaky).
    // Como o GetSessions() é o único método chamado periodicamente pela UI (a cada
    // ~2s), ele também reconciliará o cache com o enumerator em intervalos fixos —
    // caso contrário a lista congelaria no estado inicial quando um app novo tocasse
    // áudio sem o sink notificar.
    private int _pollTick;
    private const int ReconcileEveryTicks = 5;     // ~10s (5 polls de 2s)
    private const int RecreateEveryTicks = 30;     // ~60s (recria o enumerator)

    /// <summary>Tracks one audio session: control RCW (for unregister), sink, volume RCW, and current state.</summary>
    private sealed class SessionEntry(
        IAudioSessionControl Control,
        SessionEventSink Sink,
        ISimpleAudioVolume Volume,
        int State)
    {
        internal IAudioSessionControl Control { get; } = Control;
        internal SessionEventSink Sink { get; } = Sink;
        internal ISimpleAudioVolume Volume { get; } = Volume;
        internal int State { get; } = State;
    }

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public AudioController()
    {
        _syncContext = SynchronizationContext.Current;
        _callbacks = new ComCallbacks(this);
        _sessionNotificationSink = new SessionNotificationSink(this);
        _dispatcher = new ComDispatcher();
        AppLog.Instance.Info("AudioController criado");

        // Marshal all COM object creation to the dedicated MTA thread.
        _dispatcher.Invoke(() =>
        {
            _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _deviceEnumerator.RegisterEndpointNotificationCallback(_callbacks.DeviceNotifications);
            ActivateDefaultDevice();
        });
    }

    /// <summary>Create device + endpoint + session manager + enumerator on current (MTA) thread.</summary>
    private void ActivateDefaultDevice()
    {
        _deviceEnumerator!.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out _device);

        var iidEndpoint = typeof(IAudioEndpointVolume).GUID;
        _device!.Activate(ref iidEndpoint, ComCtx.ClsCtxAll, IntPtr.Zero, out var endpointObj);
        _endpoint = (IAudioEndpointVolume)endpointObj;
        _endpoint.RegisterControlChangeNotify(_callbacks.EndpointCallback);

        var iidManager = typeof(IAudioSessionManager2).GUID;
        try
        {
            _device!.Activate(ref iidManager, ComCtx.ClsCtxAll, IntPtr.Zero, out var managerObj);
            _sessionManager = (IAudioSessionManager2)managerObj;
            _sessionManager.RegisterSessionNotification(_sessionNotificationSink);
            _sessionManager.GetSessionEnumerator(out _sessionEnumerator);
            AppLog.Instance.Info("dispositivo de áudio ativado");
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("Falha ao criar session manager/enumerator", ex);
            // Unregister if notification was registered but enumerator failed.
            try { _sessionManager?.UnregisterSessionNotification(_sessionNotificationSink); } catch { }
            Release(ref _sessionEnumerator);
            Release(ref _sessionManager);
        }
        RefreshSessionCache();
    }

    // ──────────────── Public synchronous API (marshalled to MTA) ────────────────

    public MasterInfo GetMaster()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(() =>
        {
            _endpoint!.GetMasterVolumeLevelScalar(out var level);
            _endpoint.GetMute(out var mute);
            return new MasterInfo(Math.Round(level * 100d), mute);
        });
    }

    public void SetMasterVolume(double percent)
    {
        ThrowIfDisposed();
        var level = (float)(Math.Clamp(percent, 0, 100) / 100d);
        _dispatcher.Invoke(() =>
        {
            try
            {
                var ctx = ComCtx.Empty;
                _endpoint!.SetMasterVolumeLevelScalar(level, ref ctx);
            }
            catch (Exception ex)
            {
                AppLog.Instance.Error("SetMasterVolume falhou", ex);
            }
        });
    }

    public void SetMasterMute(bool mute)
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(() =>
        {
            try
            {
                _endpoint!.SetMute(mute, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLog.Instance.Error("SetMasterMute falhou", ex);
            }
        });
    }

    public IReadOnlyList<AppVolume> GetSessions()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(() =>
        {
            ReconcileIfDue();
            var result = new List<AppVolume>();
            foreach (var (pid, entries) in _sessions)
            {
                if (entries.Count == 0) continue;
                // Sessões inativas permanecem no cache (sink registrado) para
                // reaparecerem em tempo real; apenas a listagem filtra por Active.
                if (entries[0].State != AudioSessionState.Active) continue;
                try
                {
                    AppLog.Instance.Info($"GetSessions: lendo PID={pid}");
                    entries[0].Volume.GetMasterVolume(out var level);
                    entries[0].Volume.GetMute(out var mute);
                    var name = ResolveProcessName(pid);
                    result.Add(new AppVolume(pid, name, ResolveIconPng(pid),
                        Math.Round(level * 100d), mute));
                }
                catch (Exception ex) { AppLog.Instance.Error($"GetSessions: exceção PID={pid}", ex); }
            }
            AppLog.Instance.Info($"GetSessions: {result.Count} apps retornados");
            return result.OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public void SetSessionVolume(int processId, double percent)
    {
        ThrowIfDisposed();
        var level = (float)(Math.Clamp(percent, 0, 100) / 100d);
        _dispatcher.Invoke(() =>
        {
            if (!_sessions.TryGetValue(processId, out var entries)) return;
            foreach (var entry in entries)
            {
                var ctx = ComCtx.Empty;
                try { entry.Volume.SetMasterVolume(level, ref ctx); } catch { }
            }
        });
    }

    public void SetSessionMute(int processId, bool mute)
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(() =>
        {
            if (!_sessions.TryGetValue(processId, out var entries)) return;
            foreach (var entry in entries)
            {
                try { entry.Volume.SetMute(mute, IntPtr.Zero); } catch { }
            }
        });
    }

    // ──────────────── Session cache (runs on MTA thread) ────────────────

    /// <summary>Runs on the MTA thread (via GetSessions). Periodically re-enumerates
    /// the session enumerator so the list keeps reflecting audio apps even when the
    /// COM callbacks fail to fire. Falls back to a fresh enumerator every so often to
    /// avoid a stale snapshot that hides newly created sessions.</summary>
    private void ReconcileIfDue()
    {
        var tick = Interlocked.Increment(ref _pollTick);
        if (tick % RecreateEveryTicks == 0)
            RecreateEnumerator();
        else if (tick % ReconcileEveryTicks == 0)
            RefreshSessionCache();
    }

    /// <summary>Release the current session enumerator and obtain a fresh one, then
    /// refresh the cache. Ensures the enumerator reflects sessions created since
    /// startup (an existing enumerator may hold a stale snapshot).</summary>
    private void RecreateEnumerator()
    {
        if (_sessionManager is null) return;
        AppLog.Instance.Info("enumerator de sessões: recriando a partir do manager");
        try
        {
            Release(ref _sessionEnumerator);
            _sessionManager.GetSessionEnumerator(out _sessionEnumerator);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("RecreateEnumerator: falha ao obter novo enumerator", ex);
        }
        RefreshSessionCache();
    }

    private void RefreshSessionCache()
    {
        if (_sessionEnumerator is null)
        {
            return;
        }

        int count;
        try
        {
            _sessionEnumerator.GetCount(out count);
        }
        catch (Exception ex)
        {
            // Se o enumerator falhar (ex: dispositivo removido), preserva o cache
            // anterior em vez de deixá-lo vazio.
            AppLog.Instance.Error("RefreshSessionCache: GetCount falhou, preservando cache", ex);
            return;
        }

        AppLog.Instance.Info($"RefreshSessionCache: {count} sessões no enumerator");

        // Só libera o cache antigo depois de confirmar que a re-enumeração é viável.
        ReleaseAllSessions();
        var addedCount = 0;
        for (var i = 0; i < count; i++)
        {
            IAudioSessionControl? control = null;
            ISimpleAudioVolume? volume = null;
            bool transferred = false;
            try
            {
                AppLog.Instance.Info($"RefreshSessionCache: enumerando sessão {i}");
                _sessionEnumerator.GetSession(i, out control);
                var control2 = (IAudioSessionControl2)control;
                control2.GetState(out var state);
                control2.GetProcessId(out var pidNative);
                var pid = (int)pidNative;
                if (pid > 1_000_000)
                {
                    continue;
                }
                var name = ResolveProcessName(pid);

                if (state == AudioSessionState.Expired) { continue; }
                // Filtro por PID: sessões de sistema sempre têm PID 0
                if (pid == 0) { continue; }

                volume = (ISimpleAudioVolume)control;
                var sink = new SessionEventSink(this);
                control.RegisterAudioSessionNotification(sink);

                if (!_sessions.TryGetValue(pid, out var list))
                    _sessions[pid] = list = new List<SessionEntry>();
                list.Add(new SessionEntry(control, sink, volume, state));
                transferred = true;
                addedCount++;
                AppLog.Instance.Info($"  sessão: PID={pid} nome={name} estado={state}");
            }
            catch (Exception ex) { AppLog.Instance.Error($"RefreshSessionCache: exceção na sessão {i}", ex); }
            finally
            {
                if (!transferred)
                {
                    ReleaseControl(control);
                    if (volume is not null) ReleaseComObject(volume);
                }
            }
        }

        AppLog.Instance.Info($"RefreshSessionCache: {addedCount} sessões ativas adicionadas");
    }

    /// <summary>Unregister every sink, release every control + volume RCW, clear.</summary>
    private void ReleaseAllSessions()
    {
        foreach (var list in _sessions.Values)
            foreach (var entry in list)
            {
                try { entry.Control.UnregisterAudioSessionNotification(entry.Sink); } catch { }
                ReleaseControl(entry.Control);
                ReleaseComObject(entry.Volume);
            }
        _sessions.Clear();
    }

    // ──────────────── Callbacks from COM (fire on MTA thread) ────────────────

    /// <summary>Called by session sinks: queues cache refresh + UI notification to the MTA dispatcher.</summary>
    internal void NotifySessionsChanged()
    {
        if (_disposed) return;
        AppLog.Instance.Info("callback: sessões mudaram");
        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            RefreshSessionCache();
            Post(SessionsChanged);
        });
    }

    /// <summary>Called by endpoint/device callbacks on MTA thread: just post to UI.</summary>
    internal void NotifyMasterChanged()
    {
        if (_disposed) return;
        AppLog.Instance.Info("callback: volume master mudou");
        Post(MasterChanged);
    }

    /// <summary>Device-default-changed callback (already on MTA thread from COM).</summary>
    internal void RebuildCore()
    {
        if (_disposed) return;
        AppLog.Instance.Info("rebuild do dispositivo de áudio");
        try
        {
            // Tear down session listeners
            try { _sessionManager?.UnregisterSessionNotification(_sessionNotificationSink); } catch { }
            ReleaseAllSessions();
            Release(ref _sessionEnumerator);
            Release(ref _sessionManager);

            // Tear down endpoint
            try { if (_endpoint is not null) _endpoint.UnregisterControlChangeNotify(_callbacks.EndpointCallback); } catch { }
            Release(ref _endpoint);
            Release(ref _device);

            // Rebuild
            ActivateDefaultDevice();
        }
        catch { /* best-effort */ }

        Post(SessionsChanged);
        NotifyMasterChanged();
    }

    /// <summary>Public entry for device rebuild (marshals to MTA if needed).</summary>
    internal void RebuildAll()
    {
        if (_disposed) return;
        if (Thread.CurrentThread == _dispatcher.MtaThread)
            RebuildCore();
        else
            _dispatcher.Invoke(RebuildCore);
    }

    // ──────────────── Helpers ────────────────

    private static string ResolveProcessName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return "Aplicativo desconhecido"; }
    }

    private static byte[]? ResolveIconPng(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            var exePath = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
            // Cache por exe path: a extração STA só roda uma vez por executável.
            // "?? Array.Empty<byte>()" preserva a semântica original (ícone nulo →
            // cache vazio) e mantém o retorno não-nulo para o ConcurrentDictionary.
            return IconCacheByExe.GetOrAdd(exePath, static path => ExtractIconOnStaThread(path) ?? Array.Empty<byte>());
        }
        catch { return null; }
    }

    /// <summary>GDI+ não é thread-safe e é projetado para STA; extrai o ícone numa
    /// thread STA dedicada para evitar crash nativo (access violation) na thread MTA.</summary>
    private static byte[]? ExtractIconOnStaThread(string path)
    {
        byte[]? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return;
                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                result = ms.ToArray();
            }
            catch { /* ícone opcional; falha não derruba o app */ }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>Marshal event to UI thread (captured SynchronizationContext).</summary>
    private void Post(EventHandler? handler)
    {
        if (handler is null) return;
        if (_syncContext is not null) _syncContext.Post(_ => handler(this, EventArgs.Empty), null);
        else handler(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    // ──────────────── COM release helpers ────────────────

    private static void ReleaseControl(IAudioSessionControl? control)
    {
        if (control is null) return;
        try { _ = Marshal.ReleaseComObject(control); } catch { }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is null) return;
        try { _ = Marshal.ReleaseComObject(comObject); } catch { }
    }

    private void Release<T>(ref T? comObject) where T : class
    {
        if (comObject is null) return;
        try { _ = Marshal.ReleaseComObject(comObject); } catch { }
        comObject = null;
    }

    // ──────────────── Dispose (marshal cleanup to MTA) ────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Marshal all COM cleanup to the MTA thread that owns the objects.
        _dispatcher.Invoke(DisposeComObjects);
        _dispatcher.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Runs on MTA thread — unregisters + releases every COM RCW.</summary>
    private void DisposeComObjects()
    {
        try { _sessionManager?.UnregisterSessionNotification(_sessionNotificationSink); } catch { }
        ReleaseAllSessions();
        Release(ref _sessionEnumerator);
        Release(ref _sessionManager);
        try { if (_endpoint is not null) _endpoint.UnregisterControlChangeNotify(_callbacks.EndpointCallback); } catch { }
        try { _deviceEnumerator?.UnregisterEndpointNotificationCallback(_callbacks.DeviceNotifications); } catch { }
        Release(ref _endpoint);
        Release(ref _device);
        Release(ref _deviceEnumerator);
    }
}
