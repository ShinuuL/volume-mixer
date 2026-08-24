using VolumeMixer.ViewModels;

namespace VolumeMixer.Tests.ViewModels;

public class VolumeInputParserTests
{
    [Theory]
    [InlineData("45", 45)]
    [InlineData(" 30 ", 30)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    public void Valores_validos_sao_convertidos(string text, double expected)
        => Assert.Equal(expected, VolumeInputParser.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("4,5")]
    [InlineData("45.5")]
    public void Entrada_vazia_ou_invalida_retorna_null(string? text)
        => Assert.Null(VolumeInputParser.Parse(text));

    [Theory]
    [InlineData("150", 100)]
    [InlineData("-5", 0)]
    public void Fora_da_faixa_e_limitado(string text, double expected)
        => Assert.Equal(expected, VolumeInputParser.Parse(text));
}
