using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolumeMixer.Audio;
using VolumeMixer.Models;

namespace VolumeMixer.ViewModels;

public sealed class AppVolumeViewModel : ViewModelBase
{
    private readonly IAudioController _audio;
    private bool _updatingFromSystem;
    private double _volumePercent;
    private string _volumeText = "";
    private bool _isMuted;

    public AppVolumeViewModel(IAudioController audio, AppVolume source)
    {
        _audio = audio;
        ProcessId = source.ProcessId;
        ProcessName = source.ProcessName;
        Icon = Decode(source.IconPng);
        ToggleMute = new RelayCommand(() => _audio.SetSessionMute(ProcessId, !IsMuted));
        UpdateFrom(source);
    }

    public int ProcessId { get; }
    public string ProcessName { get; private set; }
    public ImageSource? Icon { get; private set; }
    public RelayCommand ToggleMute { get; }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!RaiseAndSetIfChanged(ref _volumePercent, value)) return;
            VolumeText = Format(value);
            if (!_updatingFromSystem) _audio.SetSessionVolume(ProcessId, value);
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

    public void CommitText()
    {
        var parsed = VolumeInputParser.Parse(VolumeText);
        if (parsed is null) { VolumeText = Format(_volumePercent); return; }
        VolumePercent = parsed.Value;
    }

    public void UpdateFrom(AppVolume source)
    {
        _updatingFromSystem = true;
        try
        {
            ProcessName = source.ProcessName;
            RaisePropertyChanged(nameof(ProcessName));
            VolumePercent = source.VolumePercent;
            IsMuted = source.Mute;
        }
        finally { _updatingFromSystem = false; }
    }

    private static ImageSource? Decode(byte[]? png)
    {
        if (png is null || png.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(png);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private static string Format(double v) => Math.Round(v).ToString("0");
}
