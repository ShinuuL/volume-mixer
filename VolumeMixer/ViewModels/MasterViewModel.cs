using VolumeMixer.Audio;
using VolumeMixer.Models;

namespace VolumeMixer.ViewModels;

public sealed class MasterViewModel : ViewModelBase
{
    private readonly IAudioController _audio;
    private bool _updatingFromSystem;
    private double _volumePercent;
    private string _volumeText = "";
    private bool _isMuted;

    public MasterViewModel(IAudioController audio)
    {
        _audio = audio;
        _audio.MasterChanged += (_, _) => UpdateFrom(_audio.GetMaster());
        UpdateFrom(_audio.GetMaster());
        ToggleMute = new RelayCommand(() => _audio.SetMasterMute(!IsMuted));
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!RaiseAndSetIfChanged(ref _volumePercent, value)) return;
            VolumeText = Format(value);
            if (!_updatingFromSystem) _audio.SetMasterVolume(value);
        }
    }

    public string VolumeText
    {
        get => _volumeText;
        set => RaiseAndSetIfChanged(ref _volumeText, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => RaiseAndSetIfChanged(ref _isMuted, value);
    }

    public RelayCommand ToggleMute { get; }

    /// <summary>Chamado no Enter e na perda de foco da caixa de digitação.</summary>
    public void CommitText()
    {
        var parsed = VolumeInputParser.Parse(VolumeText);
        if (parsed is null) { VolumeText = Format(_volumePercent); return; }
        VolumePercent = parsed.Value; // setter envia ao controlador
    }

    public void UpdateFrom(MasterInfo info)
    {
        _updatingFromSystem = true;
        try
        {
            VolumePercent = info.VolumePercent; // não ecoa: flag ativa
            IsMuted = info.Mute;
        }
        finally { _updatingFromSystem = false; }
    }

    private static string Format(double v) => Math.Round(v).ToString("0");
}
