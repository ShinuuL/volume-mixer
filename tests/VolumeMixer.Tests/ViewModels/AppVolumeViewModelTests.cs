using System.Windows.Media;
using VolumeMixer.Models;
using VolumeMixer.ViewModels;
using VolumeMixer.Tests.TestDoubles;

namespace VolumeMixer.Tests.ViewModels;

public class AppVolumeViewModelTests
{
    private static readonly byte[] Png1px =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    [Fact]
    public void Carrega_dados_do_app()
    {
        var audio = new FakeAudioController();
        var src = new AppVolume(99, "spotify", Png1px, 70, false);
        var vm = new AppVolumeViewModel(audio, src);
        Assert.Equal(99, vm.ProcessId);
        Assert.Equal("spotify", vm.ProcessName);
        Assert.NotNull(vm.Icon);
        Assert.Equal(70, vm.VolumePercent);
        Assert.Equal("70", vm.VolumeText);
    }

    [Fact]
    public void Icone_corrompido_fica_null()
    {
        var audio = new FakeAudioController();
        var src = new AppVolume(1, "x", new byte[] { 1, 2, 3 }, 10, false);
        var vm = new AppVolumeViewModel(audio, src);
        Assert.Null(vm.Icon);
    }

    [Fact]
    public void CommitText_aplica_no_pid_do_app()
    {
        var audio = new FakeAudioController();
        var vm = new AppVolumeViewModel(audio, new AppVolume(99, "spotify", null, 70, false));
        vm.VolumeText = "25";
        vm.CommitText();
        Assert.Equal((99, 25.0), Assert.Single(audio.VolumeCalls));
        Assert.Equal(25, vm.VolumePercent);
    }

    [Fact]
    public void ToggleMute_chama_controlador_com_pid()
    {
        var audio = new FakeAudioController();
        var vm = new AppVolumeViewModel(audio, new AppVolume(99, "spotify", null, 70, false));
        vm.ToggleMute.Execute(null);
        Assert.Equal((99, true), Assert.Single(audio.MuteCalls));
    }

    [Fact]
    public void UpdateFrom_substitui_estado_sem_eco()
    {
        var audio = new FakeAudioController();
        var vm = new AppVolumeViewModel(audio, new AppVolume(99, "spotify", null, 70, false));
        vm.UpdateFrom(new AppVolume(99, "spotify", null, 33, true));
        Assert.Equal(33, vm.VolumePercent);
        Assert.True(vm.IsMuted);
        Assert.Empty(audio.VolumeCalls);
    }
}
