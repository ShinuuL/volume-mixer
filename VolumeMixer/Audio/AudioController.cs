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
    private bool _disposed;

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public AudioController()
    {
        _syncContext = SynchronizationContext.Current;
        _callbacks = new ComCallbacks(this);
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

    public IReadOnlyList<AppVolume> GetSessions() => Array.Empty<AppVolume>(); // Task 10
    public void SetSessionVolume(int processId, double percent) { }             // Task 10
    public void SetSessionMute(int processId, bool mute) { }                    // Task 10

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
}

public partial class AudioController
{
    internal void NotifyMasterChanged() => Post(MasterChanged);

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

    partial void OnSessionsCacheInvalidated(); // Task 10 implementa
}
