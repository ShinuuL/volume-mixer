using System.IO;
using VolumeMixer.Infrastructure;

namespace VolumeMixer.Tests.Infrastructure;

public class AppLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"volmixer-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Escreve_linha_info_criando_diretorio()
    {
        var log = new AppLog(_dir);
        log.Info("painel aberto");

        var file = Path.Combine(_dir, $"app-{DateTime.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(file));
        Assert.Contains("painel aberto", File.ReadAllText(file));
    }

    [Fact]
    public void Erro_inclui_excecao()
    {
        var log = new AppLog(_dir);
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { log.Error("falhou", ex); }

        var text = File.ReadAllText(Path.Combine(_dir, $"app-{DateTime.Now:yyyy-MM-dd}.log"));
        Assert.Contains("falhou", text);
        Assert.Contains("boom", text);
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
}
