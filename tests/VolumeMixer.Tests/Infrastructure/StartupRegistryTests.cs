using Microsoft.Win32;
using VolumeMixer.Infrastructure;

namespace VolumeMixer.Tests.Infrastructure;

public class StartupRegistryTests : IDisposable
{
    private const string TestKeyPath = @"Software\VolumeMixerTests\Run";
    private readonly StartupRegistry _registry = new(TestKeyPath);

    [Fact]
    public void Desabilitado_por_padrao()
    {
        _registry.Disable();
        Assert.False(_registry.IsEnabled());
    }

    [Fact]
    public void Enable_grava_caminho_do_exe_e_IsEnabled_true()
    {
        _registry.Enable();
        Assert.True(_registry.IsEnabled());

        using var key = Registry.CurrentUser.OpenSubKey(TestKeyPath);
        var value = key?.GetValue(StartupRegistry.AppValueName) as string;
        Assert.NotNull(value);
        Assert.Contains("VolumeMixer", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disable_remove_o_valor()
    {
        _registry.Enable();
        _registry.Disable();
        Assert.False(_registry.IsEnabled());
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\VolumeMixerTests", throwOnMissingSubKey: false); }
        catch { /* limpeza best-effort */ }
    }
}
