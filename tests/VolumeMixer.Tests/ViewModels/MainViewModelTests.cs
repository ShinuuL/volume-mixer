using System.Collections.Specialized;
using VolumeMixer.Models;
using VolumeMixer.ViewModels;
using VolumeMixer.Tests.TestDoubles;

namespace VolumeMixer.Tests.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void Carrega_master_e_sessoes_iniciais()
    {
        var audio = new FakeAudioController();
        audio.Sessions.Add(new AppVolume(1, "chrome", null, 30, false));
        audio.Sessions.Add(new AppVolume(2, "spotify", null, 80, true));
        using var vm = new MainViewModel(audio);
        Assert.Equal(2, vm.Apps.Count);
        Assert.Equal("chrome", vm.Apps[0].ProcessName);
        Assert.Equal(50, vm.Master.VolumePercent);
    }

    [Fact]
    public void SessionsChanged_adiciona_e_remove_linhas()
    {
        var audio = new FakeAudioController();
        using var vm = new MainViewModel(audio);
        audio.Sessions.Add(new AppVolume(1, "chrome", null, 30, false));
        audio.RaiseSessionsChanged();
        Assert.Single(vm.Apps);

        audio.Sessions.Clear();
        audio.Sessions.Add(new AppVolume(2, "games", null, 90, false));
        audio.RaiseSessionsChanged();
        Assert.Single(vm.Apps);
        Assert.Equal("games", vm.Apps[0].ProcessName);
    }

    [Fact]
    public void Linha_existente_e_preservada_para_pid_que_continua()
    {
        var audio = new FakeAudioController();
        audio.Sessions.Add(new AppVolume(1, "chrome", null, 30, false));
        using var vm = new MainViewModel(audio);
        var original = vm.Apps[0];

        audio.Sessions.Clear();
        audio.Sessions.Add(new AppVolume(1, "chrome", null, 55, false));
        audio.RaiseSessionsChanged();

        Assert.Same(original, vm.Apps[0]); // mesma instância, estado atualizado
        Assert.Equal(55, original.VolumePercent);
    }

    [Fact]
    public void Dispose_descarta_o_controlador()
    {
        var audio = new FakeAudioController();
        var vm = new MainViewModel(audio);
        vm.Dispose();
        Assert.True(audio.Disposed);
    }

    [Fact]
    public void Falha_total_da_listagem_exibe_aviso_e_mantem_master()
    {
        var audio = new FakeAudioController { FailGetSessions = true };
        using var vm = new MainViewModel(audio); // ctor chama RefreshSessions
        Assert.Empty(vm.Apps);
        Assert.Equal("Não foi possível listar os aplicativos", vm.AppsError);
        Assert.NotNull(vm.Master); // master segue disponível
    }
}
