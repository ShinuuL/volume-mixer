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
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
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

/// <summary>vtable completa de IAudioSessionControl (9 slots) — ordem obrigatória.</summary>
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out int state);                                                // slot 3
    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);     // slot 4
    void SetDisplayName(string name, ref Guid eventContext);                     // slot 5
    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);        // slot 6
    void SetIconPath(string path, ref Guid eventContext);                        // slot 7
    void GetGroupingParam(out Guid groupingId);                                  // slot 8
    void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);           // slot 9
    void RegisterAudioSessionNotification(IAudioSessionEvents events);           // slot 10
    void UnregisterAudioSessionNotification(IAudioSessionEvents events);         // slot 11
}

/// <summary>vtable completa de IAudioSessionControl2 — 9 slots de IAudioSessionControl + 5 próprios.</summary>
[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    void GetState(out int state);                                                // slot 3
    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);     // slot 4
    void SetDisplayName(string name, ref Guid eventContext);                     // slot 5
    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);        // slot 6
    void SetIconPath(string path, ref Guid eventContext);                        // slot 7
    void GetGroupingParam(out Guid groupingId);                                  // slot 8
    void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);           // slot 9
    void RegisterAudioSessionNotification(IAudioSessionEvents events);           // slot 10
    void UnregisterAudioSessionNotification(IAudioSessionEvents events);         // slot 11
    void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); // slot 12
    void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); // slot 13
    void GetProcessId(out uint processId);                                       // slot 14
    [PreserveSig] int IsSystemSoundsSession();                                   // slot 15
    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);      // slot 16
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

/// <summary>vtable completa (5 slots) — ordem obrigatória.</summary>
[ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    void OnDisplayNameChanged(string displayName, ref Guid eventContext);
    void OnIconPathChanged(string iconPath, ref Guid eventContext);
    void OnVolumeChanged(float newVolume, bool newMute, ref Guid eventContext);
    void OnStateChanged(int newState);
    void OnSessionDisconnected(int disconnectReason);
}

[ComImport, Guid("67598B03-F5E7-4AFB-80EC-EAAF029EF668"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    void OnSessionCreated(IAudioSessionControl newSession);
}

/// <summary>vtable: 2 herdados de IAudioSessionManager + 3 próprios — ordem obrigatória.</summary>
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IAudioSessionControl control); // slot herdado
    void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out ISimpleAudioVolume volume);      // slot herdado
    void GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    void RegisterSessionNotification(IAudioSessionNotification notification);
    void UnregisterSessionNotification(IAudioSessionNotification notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int sessionCount);
    void GetSession(int index, out IAudioSessionControl session);
}

internal static class AudioSessionState
{
    internal const int Inactive = 0;
    internal const int Active = 1;
    internal const int Expired = 2;
}

internal static class SessionControlExtensions
{
    /// <summary>IsSystemSoundsSession retorna HRESULT: S_OK (0) = true, S_FALSE (1) = false.</summary>
    public static bool IsSystemSoundsSessionSafe(this IAudioSessionControl2 control)
    {
        try { return control.IsSystemSoundsSession() == 0; }
        catch (COMException) { return false; }
    }
}
