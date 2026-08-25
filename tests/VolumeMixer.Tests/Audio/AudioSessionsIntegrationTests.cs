using VolumeMixer.Audio;

namespace VolumeMixer.Tests.Audio;

[Trait("Category", "Integration")]
public class AudioSessionsIntegrationTests
{
    [Fact]
    public void Enumeracao_de_sessoes_nao_lanca_e_valores_sao_validos()
    {
        using var audio = new AudioController();
        var sessions = audio.GetSessions(); // pode ser vazio se nada estiver tocando
        Assert.All(sessions, s =>
        {
            Assert.True(s.ProcessId > 0);
            Assert.False(string.IsNullOrWhiteSpace(s.ProcessName));
            Assert.InRange(s.VolumePercent, 0, 100);
        });
    }

    [Fact]
    public void SetSessionVolume_em_pid_inexistente_nao_lanca()
    {
        using var audio = new AudioController();
        audio.SetSessionVolume(int.MaxValue, 50);
        audio.SetSessionMute(int.MaxValue, true);
    }
}
