using System.Runtime.InteropServices;

namespace VolumeMixer.Audio;

/// <summary>Agrupa todos os callbacks COM; repassa ao controller via Post().</summary>
internal sealed class ComCallbacks :
    IAudioEndpointVolumeCallback,
    IMMNotificationClient
{
    private readonly AudioController _owner;

    public ComCallbacks(AudioController owner) => _owner = owner;

    public IAudioEndpointVolumeCallback EndpointCallback => this;
    public IMMNotificationClient DeviceNotifications => this;

    public void OnNotify(IntPtr notifyData)
    {
        // O buffer é propriedade do Windows — apenas ler, nunca liberar.
        try { Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData); } catch { }
        _owner.NotifyMasterChanged();
    }

    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        if (flow == EDataFlow.Render) _owner.RebuildAll();
    }

    public void OnDeviceStateChanged(string deviceId, int newState) { }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
}
