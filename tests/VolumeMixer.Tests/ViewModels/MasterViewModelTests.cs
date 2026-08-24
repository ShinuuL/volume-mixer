using VolumeMixer.Models;
using VolumeMixer.ViewModels;
using VolumeMixer.Tests.TestDoubles;

namespace VolumeMixer.Tests.ViewModels;

public class MasterViewModelTests
{
    private static MasterViewModel Create(FakeAudioController audio, MasterInfo? initial = null)
    {
        if (initial is not null) audio.Master = initial;
        return new MasterViewModel(audio);
    }

    [Fact]
    public void Carrega_estado_inicial_do_controlador()
    {
        var vm = Create(new FakeAudioController(), new MasterInfo(45, true));
        Assert.Equal(45, vm.VolumePercent);
        Assert.Equal("45", vm.VolumeText);
        Assert.True(vm.IsMuted);
    }

    [Fact]
    public void Mudar_VolumePercent_envia_ao_controlador_clampado()
    {
        var audio = new FakeAudioController();
        var vm = Create(audio);
        vm.VolumePercent = 120;
        Assert.Equal(100, vm.VolumePercent);
        Assert.Equal(new[] { 100.0 }, audio.MasterVolumeCalls);
    }

    [Fact]
    public void CommitText_aplica_valor_valido()
    {
        var audio = new FakeAudioController();
        var vm = Create(audio);
        vm.VolumeText = "37";
        vm.CommitText();
        Assert.Equal(37, vm.VolumePercent);
        Assert.Equal(new[] { 37.0 }, audio.MasterVolumeCalls);
    }

    [Fact]
    public void CommitText_invalido_reverte_texto_sem_chamar_controlador()
    {
        var audio = new FakeAudioController();
        var vm = Create(audio, new MasterInfo(45, false));
        vm.VolumeText = "abc";
        vm.CommitText();
        Assert.Equal(45, vm.VolumePercent);
        Assert.Equal("45", vm.VolumeText);
        Assert.Empty(audio.MasterVolumeCalls);
    }

    [Fact]
    public void CommitText_fora_da_faixa_limita()
    {
        var audio = new FakeAudioController();
        var vm = Create(audio);
        vm.VolumeText = "250";
        vm.CommitText();
        Assert.Equal(100, vm.VolumePercent);
    }

    [Fact]
    public void ToggleMute_alterna_e_chama_controlador()
    {
        var audio = new FakeAudioController();
        var vm = Create(audio, new MasterInfo(50, false));
        vm.ToggleMute.Execute(null);
        Assert.True(vm.IsMuted);
        Assert.Equal(new[] { true }, audio.MasterMuteCalls);
    }

    [Fact]
    public void Evento_externo_atualiza_sem_reenviar_ao_controlador()
    {
        var audio = new FakeAudioController();
        var vm = Create(audio);
        audio.RaiseMasterChanged(); // fake.Master segue (50,false); VM deve reler
        Assert.Equal(50, vm.VolumePercent);
        Assert.Empty(audio.MasterVolumeCalls); // sem eco
    }
}
