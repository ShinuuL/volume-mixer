using VolumeMixer.Audio;
using VolumeMixer.Models;

namespace VolumeMixer.Tests.TestDoubles;

public sealed class FakeAudioController : IAudioController
{
    public MasterInfo Master { get; set; } = new(50, false);
    public List<AppVolume> Sessions { get; } = new();
    public bool FailGetSessions { get; set; }
    public List<(int ProcessId, double Percent)> VolumeCalls { get; } = new();
    public List<(int ProcessId, bool Mute)> MuteCalls { get; } = new();
    public List<double> MasterVolumeCalls { get; } = new();
    public List<bool> MasterMuteCalls { get; } = new();
    public bool Disposed { get; private set; }

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public MasterInfo GetMaster() => Master;
    public void SetMasterVolume(double percent) { MasterVolumeCalls.Add(percent); Master = Master with { VolumePercent = percent }; RaiseMasterChanged(); }
    public void SetMasterMute(bool mute) { MasterMuteCalls.Add(mute); Master = Master with { Mute = mute }; RaiseMasterChanged(); }
    public IReadOnlyList<AppVolume> GetSessions()
    {
        if (FailGetSessions) throw new InvalidOperationException("falha simulada");
        return Sessions;
    }
    public void SetSessionVolume(int processId, double percent) => VolumeCalls.Add((processId, percent));
    public void SetSessionMute(int processId, bool mute) => MuteCalls.Add((processId, mute));
    public void Dispose() => Disposed = true;

    public void RaiseSessionsChanged() => SessionsChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseMasterChanged() => MasterChanged?.Invoke(this, EventArgs.Empty);
}
