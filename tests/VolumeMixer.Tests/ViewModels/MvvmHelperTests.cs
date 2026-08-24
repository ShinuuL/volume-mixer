using System.ComponentModel;
using VolumeMixer.ViewModels;

namespace VolumeMixer.Tests.ViewModels;

public sealed class SampleVm : ViewModelBase
{
    private int _value;
    public int Value { get => _value; set => RaiseAndSetIfChanged(ref _value, value); }
}

public class MvvmHelperTests
{
    [Fact]
    public void RaiseAndSetIfChanged_dispara_PropertyChanged_quando_muda()
    {
        var vm = new SampleVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Value = 42;
        Assert.Equal(new[] { nameof(SampleVm.Value) }, raised);
        Assert.Equal(42, vm.Value);
    }

    [Fact]
    public void RaiseAndSetIfChanged_nao_dispara_quando_valor_igual()
    {
        var vm = new SampleVm { Value = 7 };
        var count = 0;
        vm.PropertyChanged += (_, _) => count++;
        vm.Value = 7;
        Assert.Equal(0, count);
    }

    [Fact]
    public void RelayCommand_respeita_CanExecute_e_executa()
    {
        var executed = 0;
        var cmd = new RelayCommand(() => executed++, canExecute: () => executed == 0);
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, executed);
        Assert.False(cmd.CanExecute(null));
    }

    [Fact]
    public void RelayCommand_notifica_CanExecuteChanged()
    {
        var cmd = new RelayCommand(() => { });
        var notified = false;
        cmd.CanExecuteChanged += (_, _) => notified = true;
        cmd.RaiseCanExecuteChanged();
        Assert.True(notified);
    }
}
