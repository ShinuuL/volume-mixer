using VolumeMixer.Models;

namespace VolumeMixer.Tests.Models;

public class AppVolumeRecordTests
{
    [Fact]
    public void AppVolume_com_mesmos_valores_sao_iguais()
    {
        var a = new AppVolume(123, "chrome", null, 50, false);
        var b = new AppVolume(123, "chrome", null, 50, false);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MasterInfo_guarda_volume_e_mute()
    {
        var m = new MasterInfo(37.5, true);
        Assert.Equal(37.5, m.VolumePercent);
        Assert.True(m.Mute);
    }
}
