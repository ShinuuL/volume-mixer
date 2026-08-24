using System.Runtime.InteropServices;

namespace VolumeMixer.Audio;

internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

internal static class ComCtx
{
    internal const int ClsCtxAll = 0x17;
    internal static readonly Guid Empty = Guid.Empty;
}

/// <summary>Co-cria o enumerador de dispositivos (CLSID MMDeviceEnumerator).</summary>
/// <remarks>Não selada: o elenco para IMMDeviceEnumerator exige classe aberta (QI em runtime).</remarks>
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices); // não usado; ocupa slot
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    void GetDevice(string deviceId, out IMMDevice device);                          // não usado; ocupa slot
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged(string deviceId, int newState);
    void OnDeviceAdded(string deviceId);
    void OnDeviceRemoved(string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId);
    void OnPropertyValueChanged(string deviceId, PropertyKey key);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

/// <summary>vtable completa até GetMute (slots 1–13) — ordem obrigatória.</summary>
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void GetChannelCount(out int channelCount);
    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute(bool mute, ref Guid eventContext);
    void GetMute(out bool mute);
}

[ComImport, Guid("656805A6-2B99-47A9-AF53-D7CE973A0E4C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    void OnNotify(IntPtr notifyData); // AUDIO_VOLUME_NOTIFICATION_DATA*
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    public bool Muted;
    public float MasterVolume;
    public uint Channels;
    public IntPtr ChannelVolumes; // float[] com Channels elementos
}
