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
        // O buffer é propriedade do Windows — nunca ler/liberar. Apenas avisa
        // que o volume master mudou; o controller lê o estado atual via GetMute/
        // GetMasterVolumeLevelScalar. (Não fazemos Marshal.PtrToStructure aqui:
        // o layout do AUDIO_VOLUME_NOTIFICATION_DATA com array variável é frágil
        // e a leitura desnecessária era um risco de crash nativo.)
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
