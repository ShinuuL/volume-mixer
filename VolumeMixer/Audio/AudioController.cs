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
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _endpoint;
    private IAudioSessionManager2? _sessionManager;
    private IAudioSessionEnumerator? _sessionEnumerator;
    private readonly Dictionary<int, List<ISimpleAudioVolume>> _pidVolumes = new();
    private readonly Dictionary<int, SessionEventSink> _sessionSinks = new();
    private readonly SessionNotificationSink _sessionNotificationSink;
    private static readonly ConcurrentDictionary<string, byte[]> IconCacheByExe = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public AudioController()
    {
        _syncContext = SynchronizationContext.Current;
        _callbacks = new ComCallbacks(this);
        _sessionNotificationSink = new SessionNotificationSink(this);
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _deviceEnumerator.RegisterEndpointNotificationCallback(_callbacks.DeviceNotifications);
        ActivateDefaultDevice();
    }

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
            // Alguns ambientes não expõem o gerenciador de sessões (Activate falha com
            // E_NOINTERFACE — ex.: builds Insider recentes, RDP com áudio redirecionado).
            // Segue sem sessões por aplicativo; o controle master permanece funcional.
            Release(ref _sessionEnumerator);
            Release(ref _sessionManager);
        }
        RefreshSessionCache();
    }

    public MasterInfo GetMaster()
    {
        ThrowIfDisposed();
        _endpoint!.GetMasterVolumeLevelScalar(out var level);
        _endpoint.GetMute(out var mute);
        return new MasterInfo(Math.Round(level * 100d), mute);
    }

    public void SetMasterVolume(double percent)
    {
        ThrowIfDisposed();
        var level = (float)(Math.Clamp(percent, 0, 100) / 100d);
        var ctx = ComCtx.Empty;
        _endpoint!.SetMasterVolumeLevelScalar(level, ref ctx);
    }

    public void SetMasterMute(bool mute)
    {
        ThrowIfDisposed();
        var ctx = ComCtx.Empty;
        _endpoint!.SetMute(mute, ref ctx);
    }

    private void RefreshSessionCache()
    {
        ReleasePidVolumes();
        if (_sessionEnumerator is null) return;

        _sessionEnumerator.GetCount(out var count);
        for (var i = 0; i < count; i++)
        {
            try
            {
                _sessionEnumerator.GetSession(i, out var control);
                var control2 = (IAudioSessionControl2)control;
                control2.GetState(out var state);
                if (state == AudioSessionState.Expired) continue;

                control2.GetProcessId(out var pidNative);
                var pid = (int)pidNative;
                if (pid == 0 || control2.IsSystemSoundsSessionSafe()) continue;

                var volume = (ISimpleAudioVolume)control; // QI direto no objeto da sessão
                if (!_pidVolumes.TryGetValue(pid, out var list))
                    _pidVolumes[pid] = list = new List<ISimpleAudioVolume>();
                list.Add(volume);

                if (!_sessionSinks.ContainsKey(pid))
                {
                    var sink = new SessionEventSink(this);
                    control.RegisterAudioSessionNotification(sink);
                    _sessionSinks[pid] = sink;
                }
            }
            catch { /* sessão pode morrer no meio da enumeração */ }
        }
    }

    private void ReleasePidVolumes()
    {
        foreach (var sink in _sessionSinks.Values) { /* sinks são managed; GC cuida */ }
        _sessionSinks.Clear();
        foreach (var list in _pidVolumes.Values)
            foreach (var vol in list)
                try { _ = Marshal.ReleaseComObject(vol); } catch { }
        _pidVolumes.Clear();
    }

    public IReadOnlyList<AppVolume> GetSessions()
    {
        ThrowIfDisposed();
        var result = new List<AppVolume>();
        foreach (var (pid, volumes) in _pidVolumes)
        {
            if (volumes.Count == 0) continue;
            volumes[0].GetMasterVolume(out var level);
            volumes[0].GetMute(out var mute);
            result.Add(new AppVolume(pid, ResolveProcessName(pid), ResolveIconPng(pid), Math.Round(level * 100d), mute));
        }
        return result.OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetSessionVolume(int processId, double percent)
    {
        ThrowIfDisposed();
        var level = (float)(Math.Clamp(percent, 0, 100) / 100d);
        if (_pidVolumes.TryGetValue(processId, out var volumes))
            foreach (var volume in volumes)
            {
                var ctx = ComCtx.Empty;
                try { volume.SetMasterVolume(level, ref ctx); } catch { }
            }
    }

    public void SetSessionMute(int processId, bool mute)
    {
        ThrowIfDisposed();
        if (_pidVolumes.TryGetValue(processId, out var volumes))
            foreach (var volume in volumes)
            {
                var ctx = ComCtx.Empty;
                try { volume.SetMute(mute, ref ctx); } catch { }
            }
    }

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

    /// <summary>Reenvia evento na thread da UI (capturada no ctor).</summary>
    private void Post(EventHandler? handler)
    {
        if (handler is null) return;
        if (_syncContext is not null) _syncContext.Post(_ => handler(this, EventArgs.Empty), null);
        else handler(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_endpoint is not null) _endpoint.UnregisterControlChangeNotify(_callbacks.EndpointCallback); } catch { }
        try { _deviceEnumerator?.UnregisterEndpointNotificationCallback(_callbacks.DeviceNotifications); } catch { }
        try { _sessionManager?.UnregisterSessionNotification(_sessionNotificationSink); } catch { }
        ReleasePidVolumes();
        Release(ref _sessionEnumerator);
        Release(ref _sessionManager);
        Release(ref _endpoint);
        Release(ref _device);
        Release(ref _deviceEnumerator);
        GC.SuppressFinalize(this);
    }

    private static void Release<T>(ref T? comObject) where T : class
    {
        if (comObject is null) return;
        try { _ = Marshal.ReleaseComObject(comObject); } catch { }
        comObject = null;
    }

    partial void OnSessionsCacheInvalidated() => RefreshSessionCache();
}

public partial class AudioController
{
    internal void NotifyMasterChanged() => Post(MasterChanged);

    internal void NotifySessionsChanged() => Post(SessionsChanged);

    internal void RebuildAll()
    {
        if (_disposed) return;
        try
        {
            try { if (_endpoint is not null) _endpoint.UnregisterControlChangeNotify(_callbacks.EndpointCallback); } catch { }
            Release(ref _endpoint);
            Release(ref _device);
            ActivateDefaultDevice();
            OnSessionsCacheInvalidated();
            NotifyMasterChanged();
        }
        catch { Post(SessionsChanged); }
    }

    partial void OnSessionsCacheInvalidated(); // implementado na outra parte parcial
}
