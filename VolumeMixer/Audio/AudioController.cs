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
    // Nome/ícone por PID: evita Process.GetProcessById + MainModule a cada poll
    // (abria handles e gerava ArgumentException em loop para processos mortos).
    private readonly Dictionary<int, (string Name, byte[]? Icon)> _processInfo = new();
    private bool _disposed;
    private int _refreshQueued;
    private int _rebuildQueued;

    /// <summary>AUDCLNT_E_DEVICE_INVALIDATED: o endpoint foi removido/trocado
    /// (fone, RDP/RustDesk/AnyDesk, driver). Todos os RCWs ficam inválidos.</summary>
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    // Polling de reconciliação: os callbacks COM de sessão do Windows nem sempre
    // disparam quando novas sessões surgem (OnSessionCreated é notoriamente flaky).
    // Como o GetSessions() é o único método chamado periodicamente pela UI (a cada
    // ~2s), ele também reconciliará o cache com o enumerator em intervalos fixos —
    // caso contrário a lista congelaria no estado inicial quando um app novo tocasse
    // áudio sem o sink notificar.
    // Baseado em tempo (não em nº de polls): o polling da UI agora só roda com
    // o popup aberto, então ao reabrir depois de horas a reconciliação é imediata.
    private readonly Stopwatch _sinceReconcile = Stopwatch.StartNew();
    private readonly Stopwatch _sinceRecreate = Stopwatch.StartNew();
    private static readonly TimeSpan ReconcileEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecreateEvery = TimeSpan.FromSeconds(60);

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
        try
        {
            _deviceEnumerator!.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out _device);
        }
        catch (COMException ex)
        {
            // Sem dispositivo de reprodução (ex: sessão remota recém-aberta).
            // Não derruba: o próximo OnDeviceStateChanged/DefaultDeviceChanged reconstrói.
            AppLog.Instance.Error("nenhum dispositivo de reprodução padrão disponível", ex);
            _device = null;
            return;
        }

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
            if (_endpoint is null) return new MasterInfo(0, false);
            try
            {
                _endpoint.GetMasterVolumeLevelScalar(out var level);
                _endpoint.GetMute(out var mute);
                return new MasterInfo(Math.Round(level * 100d), mute);
            }
            catch (COMException ex) when (ex.HResult == DeviceInvalidated)
            {
                QueueRebuild();
                return new MasterInfo(0, false);
            }
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
                _endpoint?.SetMasterVolumeLevelScalar(level, ref ctx);
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
                _endpoint?.SetMute(mute, IntPtr.Zero);
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
                    entries[0].Volume.GetMasterVolume(out var level);
                    entries[0].Volume.GetMute(out var mute);
                    var (name, icon) = ResolveProcessInfo(pid);
                    result.Add(new AppVolume(pid, name, icon, Math.Round(level * 100d), mute));
                }
                catch (COMException ex) when (ex.HResult == DeviceInvalidated)
                {
                    QueueRebuild();
                    break;
                }
                catch (Exception ex) { AppLog.Instance.Error($"GetSessions: exceção PID={pid}", ex); }
            }
            AppLog.Instance.Debug($"GetSessions: {result.Count} apps retornados");
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
        if (_sinceRecreate.Elapsed >= RecreateEvery)
        {
            _sinceRecreate.Restart();
            _sinceReconcile.Restart();
            RecreateEnumerator();
        }
        else if (_sinceReconcile.Elapsed >= ReconcileEvery)
        {
            _sinceReconcile.Restart();
            RefreshSessionCache();
        }
    }

    /// <summary>Release the current session enumerator and obtain a fresh one, then
    /// refresh the cache. Ensures the enumerator reflects sessions created since
    /// startup (an existing enumerator may hold a stale snapshot).</summary>
    private void RecreateEnumerator()
    {
        if (_sessionManager is null) return;
        AppLog.Instance.Debug("enumerator de sessões: recriando a partir do manager");
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
            if (ex is COMException { HResult: DeviceInvalidated }) QueueRebuild();
            return;
        }

        AppLog.Instance.Debug($"RefreshSessionCache: {count} sessões no enumerator");

        // Só libera o cache antigo depois de confirmar que a re-enumeração é viável.
        ReleaseAllSessions();
        var addedCount = 0;
        var invalidated = false;
        for (var i = 0; i < count; i++)
        {
            IAudioSessionControl? control = null;
            ISimpleAudioVolume? volume = null;
            bool transferred = false;
            try
            {
                _sessionEnumerator.GetSession(i, out control);
                var control2 = (IAudioSessionControl2)control;
                control2.GetState(out var state);
                control2.GetProcessId(out var pidNative);
                var pid = (int)pidNative;
                if (pid > 1_000_000)
                {
                    continue;
                }
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
                AppLog.Instance.Debug($"  sessão: PID={pid} estado={state}");
            }
            catch (COMException ex) when (ex.HResult == DeviceInvalidated)
            {
                // Uma sessão inválida = dispositivo inteiro inválido: para de
                // enumerar (evita N erros seguidos) e reconstrói do zero.
                invalidated = true;
                break;
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

        // Descarta nome/ícone de PIDs que sumiram (PIDs são reutilizados pelo Windows).
        foreach (var stale in _processInfo.Keys.Where(k => !_sessions.ContainsKey(k)).ToList())
            _processInfo.Remove(stale);

        AppLog.Instance.Debug($"RefreshSessionCache: {addedCount} sessões ativas adicionadas");
        if (invalidated)
        {
            AppLog.Instance.Info("dispositivo de áudio invalidado; agendando rebuild");
            QueueRebuild();
        }
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
        AppLog.Instance.Debug("callback: sessões mudaram");
        // Coalesce: uma rajada de callbacks (ex: vários apps iniciando) vira
        // uma única re-enumeração em vez de N.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (_disposed) return;
            RefreshSessionCache();
            PostSessionsChanged();
        });
    }

    /// <summary>Volume/mudo de uma sessão mudou: o cache continua válido, só a
    /// UI precisa reler. Antes isso re-enumerava e re-registrava todas as
    /// sessões a cada passo do slider.</summary>
    internal void NotifySessionVolumeChanged()
    {
        if (_disposed) return;
        PostSessionsChanged();
    }

    /// <summary>Agenda (uma vez) a reconstrução do dispositivo na thread MTA.
    /// Seguro de chamar de callbacks COM, que não devem bloquear.</summary>
    internal void QueueRebuild()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 1) return;
        _dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref _rebuildQueued, 0);
            if (_disposed) return;
            RebuildCore();
        });
    }

    /// <summary>Called by endpoint/device callbacks on MTA thread: just post to UI.</summary>
    internal void NotifyMasterChanged()
    {
        if (_disposed) return;
        AppLog.Instance.Debug("callback: volume master mudou");
        PostMasterChanged();
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
        catch (Exception ex) { AppLog.Instance.Error("rebuild do dispositivo falhou", ex); }

        PostSessionsChanged();
        NotifyMasterChanged();
    }

    // ──────────────── Helpers ────────────────

    private (string Name, byte[]? Icon) ResolveProcessInfo(int pid)
    {
        if (_processInfo.TryGetValue(pid, out var cached)) return cached;
        (string Name, byte[]? Icon) info = ("Aplicativo desconhecido", null);
        try
        {
            using var process = Process.GetProcessById(pid);
            info = (process.ProcessName, ResolveIconPng(process));
        }
        catch { /* processo já encerrou: mantém o nome genérico */ }
        _processInfo[pid] = info;
        return info;
    }

    private static byte[]? ResolveIconPng(Process process)
    {
        try
        {
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

    private sealed class PendingFlag { public int Value; }
    private readonly PendingFlag _sessionsPostPending = new();
    private readonly PendingFlag _masterPostPending = new();

    private void PostSessionsChanged() => PostToUi(_sessionsPostPending, () => SessionsChanged);
    private void PostMasterChanged() => PostToUi(_masterPostPending, () => MasterChanged);

    /// <summary>Marshal event to UI thread (captured SynchronizationContext).
    /// No máximo UM aviso pendente por evento: rajadas de callbacks COM (slider,
    /// vários apps tocando) viravam centenas de mensagens na fila da UI, e a fila
    /// cheia (Win32Exception 1816) mata o Dispatcher do WPF.</summary>
    private void PostToUi(PendingFlag pending, Func<EventHandler?> handler)
    {
        if (_syncContext is null) { handler()?.Invoke(this, EventArgs.Empty); return; }
        if (Interlocked.Exchange(ref pending.Value, 1) == 1) return;
        _syncContext.Post(_ =>
        {
            Interlocked.Exchange(ref pending.Value, 0);
            if (!_disposed) handler()?.Invoke(this, EventArgs.Empty);
        }, null);
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
