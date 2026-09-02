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

    [Fact]
    public void GetSessions_repetido_como_polling_reconcilia_sem_lancar()
    {
        // Simula o polling de ~2s: chamadas seguidas disparam RefreshSessionCache e
        // a recriação periódica do enumerator. Nenhuma delas pode lançar (a lista
        // seria vazia se nada estiver tocando, mas o controller não pode quebrar).
        using var audio = new AudioController();
        for (var i = 0; i < 35; i++)
        {
            var sessions = audio.GetSessions();
            Assert.All(sessions, s => Assert.True(s.ProcessId > 0));
        }
    }
}
