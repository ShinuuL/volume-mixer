using VolumeMixer.Models;

namespace VolumeMixer.Audio;

public interface IAudioController : IDisposable
{
    MasterInfo GetMaster();
    void SetMasterVolume(double percent);
    void SetMasterMute(bool mute);
    IReadOnlyList<AppVolume> GetSessions();
    void SetSessionVolume(int processId, double percent);
    void SetSessionMute(int processId, bool mute);
    event EventHandler? SessionsChanged;
    event EventHandler? MasterChanged;
}
