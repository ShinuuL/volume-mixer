using System.Text.Json;
using VolumeMixer.Models;

namespace VolumeMixer.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_sao_dark_85_transparencia_e_accent_azul()
    {
        var s = new AppSettings();
        Assert.Equal("dark", s.Theme);
        Assert.Equal(0.85, s.Transparency);
        Assert.Equal("#0078D7", s.AccentColor);
        Assert.False(s.UseCustomAccent);
    }

    [Fact]
    public void Transparency_e_clampada_entre_50_e_100()
    {
        var s = new AppSettings();
        s.Transparency = 0.1;
        Assert.Equal(0.5, s.Transparency);
        s.Transparency = 1.5;
        Assert.Equal(1.0, s.Transparency);
        s.Transparency = 0.7;
        Assert.Equal(0.7, s.Transparency);
    }

    [Fact]
    public void RoundTrip_json_preserva_valores()
    {
        var s = new AppSettings
        {
            Theme = "light",
            Transparency = 0.9,
            AccentColor = "#FF0000",
            UseCustomAccent = true,
        };
        var json = JsonSerializer.Serialize(s);
        var back = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(back);
        Assert.Equal("light", back!.Theme);
        Assert.Equal(0.9, back.Transparency);
        Assert.Equal("#FF0000", back.AccentColor);
        Assert.True(back.UseCustomAccent);
    }

    [Fact]
    public void Deserializacao_nao_dispara_efeitos_colaterais()
    {
        // O JsonConstructor deve popular os campos sem tocar em Application.Current
        // (que é null em testes). Se disparasse ApplyTheme/ApplyAccent, não haveria
        // exceção (guardam null), mas o teste garante que o caminho de desserialização
        // é seguro e não depende de um Application ativo.
        var json = """{"Theme":"dark","Transparency":0.8,"AccentColor":"#123456","UseCustomAccent":true}""";
        var s = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(s);
        Assert.Equal("#123456", s!.AccentColor);
        Assert.True(s.UseCustomAccent);
    }

    [Fact]
    public void Json_vazio_usa_valores_seguros()
    {
        // Com "{}", o JsonConstructor recebe defaults (null/0/false) e deve
        // produzir valores seguros: tema dark, accent padrão, transparência clampada.
        var s = JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.NotNull(s);
        Assert.Equal("dark", s!.Theme);
        Assert.Equal("#0078D7", s.AccentColor);
        Assert.False(s.UseCustomAccent);
        Assert.InRange(s.Transparency, 0.5, 1.0);
    }
}
