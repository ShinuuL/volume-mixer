using System.Runtime.InteropServices;

namespace VolumeMixer.Audio;

/// <summary>Assina eventos de UMA sessão; qualquer mudança relevante invalida a lista.</summary>
internal sealed class SessionEventSink : IAudioSessionEvents
{
    private readonly AudioController _owner;
    public SessionEventSink(AudioController owner) => _owner = owner;

    public void OnSimpleVolumeChanged(float newVolume, bool newMute, ref Guid ctx) => _owner.NotifySessionVolumeChanged();
    public void OnChannelVolumeChanged(uint channelCount, float[] newChannelVolumeArray, uint changedChannel, ref Guid ctx) => _owner.NotifySessionVolumeChanged();
    public void OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid ctx) { }
    public void OnStateChanged(int newState) => _owner.NotifySessionsChanged();
    public void OnSessionDisconnected(int reason)
    {
        // DisconnectReasonDeviceRemoval (0) / FormatChanged (3): o endpoint inteiro mudou.
        if (reason is 0 or 3) _owner.QueueRebuild();
        else _owner.NotifySessionsChanged();
    }
    public void OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid ctx) { }
    public void OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid ctx) { }
}

internal sealed class SessionNotificationSink : IAudioSessionNotification
{
    private readonly AudioController _owner;
    public SessionNotificationSink(AudioController owner) => _owner = owner;

    public void OnSessionCreated(IAudioSessionControl newSession) => _owner.NotifySessionsChanged();
}
