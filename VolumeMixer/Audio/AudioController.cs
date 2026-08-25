using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

    /// <summary>Tracks one audio session: control RCW (for unregister), sink, and volume RCW.</summary>
    private sealed class SessionEntry(
        IAudioSessionControl Control,
        SessionEventSink Sink,
        ISimpleAudioVolume Volume)
    {
        internal IAudioSessionControl Control { get; } = Control;
        internal SessionEventSink Sink { get; } = Sink;
        internal ISimpleAudioVolume Volume { get; } = Volume;
    }

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public AudioController()
    {
        _syncContext = SynchronizationContext.Current;
        _callbacks = new ComCallbacks(this);
        _sessionNotificationSink = new SessionNotificationSink(this);
        _dispatcher = new ComDispatcher();

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
        }
        catch
        {
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
            var ctx = ComCtx.Empty;
            _endpoint!.SetMasterVolumeLevelScalar(level, ref ctx);
        });
    }

    public void SetMasterMute(bool mute)
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(() =>
        {
            var ctx = ComCtx.Empty;
            _endpoint!.SetMute(mute, ref ctx);
        });
    }

    public IReadOnlyList<AppVolume> GetSessions()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(() =>
        {
            var result = new List<AppVolume>();
            foreach (var (pid, entries) in _sessions)
            {
                if (entries.Count == 0) continue;
                try
                {
                    entries[0].Volume.GetMasterVolume(out var level);
                    entries[0].Volume.GetMute(out var mute);
                    result.Add(new AppVolume(pid, ResolveProcessName(pid), ResolveIconPng(pid),
                        Math.Round(level * 100d), mute));
                }
                catch { /* session may have died */ }
            }
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
                var ctx = ComCtx.Empty;
                try { entry.Volume.SetMute(mute, ref ctx); } catch { }
            }
        });
    }

    // ──────────────── Session cache (runs on MTA thread) ────────────────

    private void RefreshSessionCache()
    {
        ReleaseAllSessions();
        if (_sessionEnumerator is null) return;

        _sessionEnumerator.GetCount(out var count);
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
                if (state == AudioSessionState.Expired) continue;

                control2.GetProcessId(out var pidNative);
                var pid = (int)pidNative;
                if (pid == 0 || control2.IsSystemSoundsSessionSafe()) continue;

                volume = (ISimpleAudioVolume)control;
                var sink = new SessionEventSink(this);
                control.RegisterAudioSessionNotification(sink);

                if (!_sessions.TryGetValue(pid, out var list))
                    _sessions[pid] = list = new List<SessionEntry>();
                list.Add(new SessionEntry(control, sink, volume));
                transferred = true;
            }
            catch { /* session may die during enumeration */ }
            finally
            {
                // Release locally-acquired control + volume RCW on every skip / failure path.
                if (!transferred)
                {
                    ReleaseControl(control);
                    if (volume is not null) ReleaseComObject(volume);
                }
            }
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
        _dispatcher.Post(() =>
        {
            RefreshSessionCache();
            Post(SessionsChanged);
        });
    }

    /// <summary>Called by endpoint/device callbacks on MTA thread: just post to UI.</summary>
    internal void NotifyMasterChanged() => Post(MasterChanged);

    /// <summary>Device-default-changed callback (already on MTA thread from COM).</summary>
    internal void RebuildCore()
    {
        if (_disposed) return;
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
            return IconCacheByExe.GetOrAdd(exePath, static path =>
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path)!;
                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            });
        }
        catch { return null; }
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
