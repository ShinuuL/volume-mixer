using VolumeMixer.Audio;

namespace VolumeMixer.Tests.Audio;

[Trait("Category", "Integration")]
public class AudioMasterIntegrationTests
{
    [Fact]
    public void Leitura_do_master_esta_na_faixa_0_a_100()
    {
        using var audio = new AudioController();
        var master = audio.GetMaster();
        Assert.InRange(master.VolumePercent, 0, 100);
    }

    [Fact]
    public void Set_e_get_do_master_sao_consistentes()
    {
        using var audio = new AudioController();
        var original = audio.GetMaster();
        try
        {
            audio.SetMasterVolume(40);
            var read = audio.GetMaster();
            Assert.Equal(40, read.VolumePercent);
        }
        finally { audio.SetMasterVolume(original.VolumePercent); }
    }
}
