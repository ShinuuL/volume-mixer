# Volume Mixer Tray — Plano de Implementação

> **Para agentes executor:** SUB-SKILL OBRIGATÓRIA: usar superpowers:subagent-driven-development (recomendado) ou superpowers:executing-plans para executar este plano tarefa por tarefa. Passos usam sintaxe de checkbox (`- [ ]`) para rastreio.

**Goal:** App de bandeja em WPF que abre flyout com controle de volume master e por aplicativo (slider + digitação + mute).

**Architecture:** Projeto único WPF (`net8.0-windows`, WinForms habilitado só para `NotifyIcon`/`Screen`). Camada `Audio` encapsula interop COM WASAPI atrás de `IAudioController`; ViewModels MVVM leve testáveis com fake; Views em XAML com estilos centralizados.

**Tech Stack:** .NET 8 LTS, WPF, WinForms interop (NotifyIcon), COM interop WASAPI (sem pacotes externos), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-23-volume-mixer-design.md`

## Global Constraints

- TargetFramework: `net8.0-windows` com `<UseWPF>true</UseWPF>` e `<UseWindowsForms>true</UseWindowsForms>`
- Zero dependências NuGet de terceiros no projeto principal (apenas xUnit/Microsoft.NET.Test.Sdk no projeto de testes)
- Volume sempre clampado a 0–100 (`Math.Clamp`)
- Valor digitado aplica em Enter ou perda de foco; inválido/vazio reverte; fora da faixa limita
- Autostart: valor `"VolumeMixer"` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, dado = caminho do exe entre aspas
- Logs em `%LOCALAPPDATA%\VolumeMixer\logs\app-AAAA-MM-DD.log`
- Popup: largura 360 px, altura auto até 60% da área de trabalho, fecha em `Deactivated` e Esc, `ShowInTaskbar=False`
- UI em pt-BR; mensagens de commit sem acentos (padrão do repo: `feat:`, `test:`, `docs:`)
- Interfaces COM declaradas com métodos `void` (CLR converte HRESULT falho em exceção); ordem dos métodos = ordem da vtable, obrigatória

---

### Task 1: Scaffold da solução + Models

**Files:**
- Create: `VolumeMixer.sln`
- Create: `VolumeMixer/VolumeMixer.csproj`
- Create: `VolumeMixer/Models/AppVolume.cs`
- Create: `tests/VolumeMixer.Tests/VolumeMixer.Tests.csproj`
- Test: `tests/VolumeMixer.Tests/Models/AppVolumeRecordTests.cs`

**Interfaces:**
- Consumes: nada
- Produces: `record MasterInfo(double VolumePercent, bool Mute)` e `record AppVolume(int ProcessId, string ProcessName, byte[]? IconPng, double VolumePercent, bool Mute)` no namespace `VolumeMixer.Models`

- [ ] **Step 1: Criar solução e projetos**

```bash
dotnet new sln -n VolumeMixer
dotnet new wpf -n VolumeMixer -o VolumeMixer
dotnet new xunit -n VolumeMixer.Tests -o tests/VolumeMixer.Tests
dotnet sln add VolumeMixer tests/VolumeMixer.Tests
dotnet add tests/VolumeMixer.Tests reference VolumeMixer
```

- [ ] **Step 2: Ajustar os dois `.csproj`**

`VolumeMixer/VolumeMixer.csproj` (substituir o gerado):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>VolumeMixer</RootNamespace>
  </PropertyGroup>
</Project>
```

`tests/VolumeMixer.Tests/VolumeMixer.Tests.csproj` (manter pacotes xUnit gerados, trocar TF e adicionar WPF):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\VolumeMixer\VolumeMixer.csproj" />
  </ItemGroup>
</Project>
```

Apagar `UnitTest1.cs` e `MainWindow.xaml(.cs)` + entrada `StartupUri` do `App.xaml` gerados pelo template (o app não tem janela principal).

- [ ] **Step 3: Escrever teste falhando dos records**

`tests/VolumeMixer.Tests/Models/AppVolumeRecordTests.cs`:

```csharp
using VolumeMixer.Models;

namespace VolumeMixer.Tests.Models;

public class AppVolumeRecordTests
{
    [Fact]
    public void AppVolume_com_mesmos_valores_sao_iguais()
    {
        var a = new AppVolume(123, "chrome", null, 50, false);
        var b = new AppVolume(123, "chrome", null, 50, false);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MasterInfo_guarda_volume_e_mute()
    {
        var m = new MasterInfo(37.5, true);
        Assert.Equal(37.5, m.VolumePercent);
        Assert.True(m.Mute);
    }
}
```

- [ ] **Step 4: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — `Models/AppVolume.cs` não existe (erro de compilação)

- [ ] **Step 5: Criar `VolumeMixer/Models/AppVolume.cs`**

```csharp
namespace VolumeMixer.Models;

/// <summary>Estado do volume master do dispositivo padrão.</summary>
public sealed record MasterInfo(double VolumePercent, bool Mute);

/// <summary>Linha do mixer: todas as sessões de áudio de um processo, agrupadas por PID.</summary>
public sealed record AppVolume(
    int ProcessId,
    string ProcessName,
    byte[]? IconPng,
    double VolumePercent,
    bool Mute);
```

- [ ] **Step 6: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (2 testes)

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold da solucao WPF + records de modelo"
```

---

### Task 2: Helpers MVVM (ViewModelBase + RelayCommand)

**Files:**
- Create: `VolumeMixer/ViewModels/ViewModelBase.cs`
- Create: `VolumeMixer/ViewModels/RelayCommand.cs`
- Test: `tests/VolumeMixer.Tests/ViewModels/MvvmHelperTests.cs`

**Interfaces:**
- Consumes: nada
- Produces: `abstract class ViewModelBase { protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? name = null) }` e `class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand` com `RaiseCanExecuteChanged()`

- [ ] **Step 1: Escrever testes falhando**

`tests/VolumeMixer.Tests/ViewModels/MvvmHelperTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — tipos não existem

- [ ] **Step 3: Implementar os dois arquivos**

`VolumeMixer/ViewModels/ViewModelBase.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VolumeMixer.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

`VolumeMixer/ViewModels/RelayCommand.cs`:

```csharp
using System.Windows.Input;

namespace VolumeMixer.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 4: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (6 testes no total)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: helpers MVVM (ViewModelBase e RelayCommand)"
```

---

### Task 3: Parser da caixa de digitação

**Files:**
- Create: `VolumeMixer/ViewModels/VolumeInputParser.cs`
- Test: `tests/VolumeMixer.Tests/ViewModels/VolumeInputParserTests.cs`

**Interfaces:**
- Consumes: nada
- Produces: `static class VolumeInputParser { static double? Parse(string? text) }` — retorna `null` se vazio/inválido (chamador reverte), senão valor clampado 0–100

- [ ] **Step 1: Escrever testes falhando**

`tests/VolumeMixer.Tests/ViewModels/VolumeInputParserTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — tipo não existe

- [ ] **Step 3: Implementar**

`VolumeMixer/ViewModels/VolumeInputParser.cs`:

```csharp
using System.Globalization;

namespace VolumeMixer.ViewModels;

/// <summary>Regra única de digitação (master e apps): inteiro 0–100.</summary>
public static class VolumeInputParser
{
    /// <returns>null se vazio/inválido (chamador reverte o texto); senão valor clampado.</returns>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return null;
        return Math.Clamp(value, 0, 100);
    }
}
```

- [ ] **Step 4: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: parser da caixa de volume com clamp 0-100"
```

---

### Task 4: Fake de áudio + MasterViewModel

**Files:**
- Create: `tests/VolumeMixer.Tests/TestDoubles/FakeAudioController.cs`
- Create: `VolumeMixer/Audio/IAudioController.cs`
- Create: `VolumeMixer/ViewModels/MasterViewModel.cs`
- Test: `tests/VolumeMixer.Tests/ViewModels/MasterViewModelTests.cs`

**Interfaces:**
- Consumes: `MasterInfo` (Task 1), `ViewModelBase` (Task 2), `VolumeInputParser` (Task 3)
- Produces: interface `VolumeMixer.Audio.IAudioController` (contrato completo abaixo, usado pelas Tasks 4–6 e 9–10) e `class MasterViewModel : ViewModelBase` com propriedades `VolumePercent` (double, slider), `VolumeText` (string), `IsMuted` (bool), comando `ToggleMute`, método `CommitText()`, método `UpdateFrom(MasterInfo)`

Contrato `VolumeMixer/Audio/IAudioController.cs` (criar agora; implementação real nas Tasks 9–10):

```csharp
using VolumeMixer.Models;

namespace VolumeMixer.Audio;

public interface IAudioController : IDisposable
{
    MasterInfo GetMaster();
    void SetMasterVolume(double percent);
    void SetMasterMute(bool mute);
    IReadOnlyList<AppVolume> GetSessions();
    void SetSessionVolume(int processId, double percent);
    void SetSessionMute(int processId, bool mute);
    event EventHandler? SessionsChanged;
    event EventHandler? MasterChanged;
}
```

- [ ] **Step 1: Criar o fake (infraestrutura de teste)**

`tests/VolumeMixer.Tests/TestDoubles/FakeAudioController.cs`:

```csharp
using VolumeMixer.Audio;
using VolumeMixer.Models;

namespace VolumeMixer.Tests.TestDoubles;

public sealed class FakeAudioController : IAudioController
{
    public MasterInfo Master { get; set; } = new(50, false);
    public List<AppVolume> Sessions { get; } = new();
    public bool FailGetSessions { get; set; }
    public List<(int ProcessId, double Percent)> VolumeCalls { get; } = new();
    public List<(int ProcessId, bool Mute)> MuteCalls { get; } = new();
    public List<double> MasterVolumeCalls { get; } = new();
    public List<bool> MasterMuteCalls { get; } = new();
    public bool Disposed { get; private set; }

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public MasterInfo GetMaster() => Master;
    public void SetMasterVolume(double percent) { MasterVolumeCalls.Add(percent); Master = Master with { VolumePercent = percent }; }
    public void SetMasterMute(bool mute) { MasterMuteCalls.Add(mute); Master = Master with { Mute = mute }; }
    public IReadOnlyList<AppVolume> GetSessions()
    {
        if (FailGetSessions) throw new InvalidOperationException("falha simulada");
        return Sessions;
    }
    public void SetSessionVolume(int processId, double percent) => VolumeCalls.Add((processId, percent));
    public void SetSessionMute(int processId, bool mute) => MuteCalls.Add((processId, mute));
    public void Dispose() => Disposed = true;

    public void RaiseSessionsChanged() => SessionsChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseMasterChanged() => MasterChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 2: Escrever testes falhando do MasterViewModel**

`tests/VolumeMixer.Tests/ViewModels/MasterViewModelTests.cs`:

```csharp
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
```

- [ ] **Step 3: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — `MasterViewModel` não existe

- [ ] **Step 4: Implementar `VolumeMixer/ViewModels/MasterViewModel.cs`**

```csharp
using VolumeMixer.Audio;

namespace VolumeMixer.ViewModels;

public sealed class MasterViewModel : ViewModelBase
{
    private readonly IAudioController _audio;
    private bool _updatingFromSystem;
    private double _volumePercent;
    private string _volumeText = "";
    private bool _isMuted;

    public MasterViewModel(IAudioController audio)
    {
        _audio = audio;
        _audio.MasterChanged += (_, _) => UpdateFrom(_audio.GetMaster());
        UpdateFrom(_audio.GetMaster());
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!RaiseAndSetIfChanged(ref _volumePercent, value)) return;
            VolumeText = Format(value);
            if (!_updatingFromSystem) _audio.SetMasterVolume(value);
        }
    }

    public string VolumeText
    {
        get => _volumeText;
        set => RaiseAndSetIfChanged(ref _volumeText, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => RaiseAndSetIfChanged(ref _isMuted, value);
    }

    public RelayCommand ToggleMute { get; }

    /// <summary>Chamado no Enter e na perda de foco da caixa de digitação.</summary>
    public void CommitText()
    {
        var parsed = VolumeInputParser.Parse(VolumeText);
        if (parsed is null) { VolumeText = Format(_volumePercent); return; }
        VolumePercent = parsed.Value; // setter envia ao controlador
    }

    public void UpdateFrom(MasterInfo info)
    {
        _updatingFromSystem = true;
        try
        {
            VolumePercent = info.VolumePercent; // não ecoa: flag ativa
            IsMuted = info.Mute;
        }
        finally { _updatingFromSystem = false; }
    }

    private static string Format(double v) => Math.Round(v).ToString("0");
}
```

Complementar o construtor com o comando (inserir após `UpdateFrom(_audio.GetMaster());`):

```csharp
ToggleMute = new RelayCommand(() => _audio.SetMasterMute(!IsMuted));
```

- [ ] **Step 5: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos, incluindo os 7 novos)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: MasterViewModel com parser, mute e protecao contra eco"
```

---

### Task 5: AppVolumeViewModel (linha de um app)

**Files:**
- Create: `VolumeMixer/ViewModels/AppVolumeViewModel.cs`
- Test: `tests/VolumeMixer.Tests/ViewModels/AppVolumeViewModelTests.cs`

**Interfaces:**
- Consumes: `AppVolume` (Task 1), `ViewModelBase`, `VolumeInputParser`, `RelayCommand`
- Produces: `sealed class AppVolumeViewModel : ViewModelBase` com `int ProcessId`, `string ProcessName`, `ImageSource? Icon` (get), `double VolumePercent`, `string VolumeText`, `bool IsMuted`, `RelayCommand ToggleMute`, `void CommitText()`, `void UpdateFrom(AppVolume)`, construtor `(IAudioController audio, AppVolume source)`

- [ ] **Step 1: Escrever testes falhando**

`tests/VolumeMixer.Tests/ViewModels/AppVolumeViewModelTests.cs`:

```csharp
using System.Windows.Media;
using VolumeMixer.Models;
using VolumeMixer.ViewModels;
using VolumeMixer.Tests.TestDoubles;

namespace VolumeMixer.Tests.ViewModels;

public class AppVolumeViewModelTests
{
    private static readonly byte[] Png1px =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    [Fact]
    public void Carrega_dados_do_app()
    {
        var audio = new FakeAudioController();
        var src = new AppVolume(99, "spotify", Png1px, 70, false);
        var vm = new AppVolumeViewModel(audio, src);
        Assert.Equal(99, vm.ProcessId);
        Assert.Equal("spotify", vm.ProcessName);
        Assert.NotNull(vm.Icon);
        Assert.Equal(70, vm.VolumePercent);
        Assert.Equal("70", vm.VolumeText);
    }

    [Fact]
    public void Icone_corrompido_fica_null()
    {
        var audio = new FakeAudioController();
        var src = new AppVolume(1, "x", new byte[] { 1, 2, 3 }, 10, false);
        var vm = new AppVolumeViewModel(audio, src);
        Assert.Null(vm.Icon);
    }

    [Fact]
    public void CommitText_aplica_no_pid_do_app()
    {
        var audio = new FakeAudioController();
        var vm = new AppVolumeViewModel(audio, new AppVolume(99, "spotify", null, 70, false));
        vm.VolumeText = "25";
        vm.CommitText();
        Assert.Equal((99, 25.0), Assert.Single(audio.VolumeCalls));
        Assert.Equal(25, vm.VolumePercent);
    }

    [Fact]
    public void ToggleMute_chama_controlador_com_pid()
    {
        var audio = new FakeAudioController();
        var vm = new AppVolumeViewModel(audio, new AppVolume(99, "spotify", null, 70, false));
        vm.ToggleMute.Execute(null);
        Assert.Equal((99, true), Assert.Single(audio.MuteCalls));
    }

    [Fact]
    public void UpdateFrom_substitui_estado_sem_eco()
    {
        var audio = new FakeAudioController();
        var vm = new AppVolumeViewModel(audio, new AppVolume(99, "spotify", null, 70, false));
        vm.UpdateFrom(new AppVolume(99, "spotify", null, 33, true));
        Assert.Equal(33, vm.VolumePercent);
        Assert.True(vm.IsMuted);
        Assert.Empty(audio.VolumeCalls);
    }
}
```

- [ ] **Step 2: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — `AppVolumeViewModel` não existe

- [ ] **Step 3: Implementar**

`VolumeMixer/ViewModels/AppVolumeViewModel.cs`:

```csharp
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolumeMixer.Audio;
using VolumeMixer.Models;

namespace VolumeMixer.ViewModels;

public sealed class AppVolumeViewModel : ViewModelBase
{
    private readonly IAudioController _audio;
    private bool _updatingFromSystem;
    private double _volumePercent;
    private string _volumeText = "";
    private bool _isMuted;

    public AppVolumeViewModel(IAudioController audio, AppVolume source)
    {
        _audio = audio;
        ProcessId = source.ProcessId;
        ProcessName = source.ProcessName;
        Icon = Decode(source.IconPng);
        ToggleMute = new RelayCommand(() => _audio.SetSessionMute(ProcessId, !IsMuted));
        UpdateFrom(source);
    }

    public int ProcessId { get; }
    public string ProcessName { get; private set; }
    public ImageSource? Icon { get; private set; }
    public RelayCommand ToggleMute { get; }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!RaiseAndSetIfChanged(ref _volumePercent, value)) return;
            VolumeText = Format(value);
            if (!_updatingFromSystem) _audio.SetSessionVolume(ProcessId, value);
        }
    }

    public string VolumeText
    {
        get => _volumeText;
        set => RaiseAndSetIfChanged(ref _volumeText, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => RaiseAndSetIfChanged(ref _isMuted, value);
    }

    public void CommitText()
    {
        var parsed = VolumeInputParser.Parse(VolumeText);
        if (parsed is null) { VolumeText = Format(_volumePercent); return; }
        VolumePercent = parsed.Value;
    }

    public void UpdateFrom(AppVolume source)
    {
        _updatingFromSystem = true;
        try
        {
            ProcessName = source.ProcessName;
            RaisePropertyChanged(nameof(ProcessName));
            VolumePercent = source.VolumePercent;
            IsMuted = source.Mute;
        }
        finally { _updatingFromSystem = false; }
    }

    private static ImageSource? Decode(byte[]? png)
    {
        if (png is null || png.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(png);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private static string Format(double v) => Math.Round(v).ToString("0");
}
```

- [ ] **Step 4: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: AppVolumeViewModel com icone PNG e controle por pid"
```

---

### Task 6: MainViewModel (master + lista ao vivo)

**Files:**
- Create: `VolumeMixer/ViewModels/MainViewModel.cs`
- Test: `tests/VolumeMixer.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `IAudioController`, `MasterViewModel`, `AppVolumeViewModel`
- Produces: `sealed class MainViewModel : ViewModelBase, IDisposable` com `MasterViewModel Master`, `ObservableCollection<AppVolumeViewModel> Apps`, `void RefreshSessions()`; sincroniza com eventos do controller

- [ ] **Step 1: Escrever testes falhando**

`tests/VolumeMixer.Tests/ViewModels/MainViewModelTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — `MainViewModel` não existe

- [ ] **Step 3: Implementar**

`VolumeMixer/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using VolumeMixer.Audio;

namespace VolumeMixer.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioController _audio;
    private bool _disposed;

    public MasterViewModel Master { get; }
    public ObservableCollection<AppVolumeViewModel> Apps { get; } = new();

    private string? _appsError;
    public string? AppsError
    {
        get => _appsError;
        private set => RaiseAndSetIfChanged(ref _appsError, value);
    }

    public MainViewModel(IAudioController audio)
    {
        _audio = audio;
        Master = new MasterViewModel(audio);
        _audio.SessionsChanged += (_, _) => RefreshSessions();
        RefreshSessions();
    }

    /// <summary>Dif por PID: preserva instâncias vivas, adiciona novas, remove mortas.</summary>
    public void RefreshSessions()
    {
        IReadOnlyList<AppVolume> latest;
        try { latest = _audio.GetSessions(); }
        catch
        {
            // Spec §7: falha total da listagem → painel mantém master + aviso.
            Apps.Clear();
            AppsError = "Não foi possível listar os aplicativos";
            return;
        }

        AppsError = null;
        var byPid = latest.ToDictionary(s => s.ProcessId);

        for (var i = Apps.Count - 1; i >= 0; i--)
            if (!byPid.ContainsKey(Apps[i].ProcessId))
                Apps.RemoveAt(i);

        foreach (var (pid, info) in byPid)
        {
            var existing = Apps.FirstOrDefault(a => a.ProcessId == pid);
            if (existing is not null) existing.UpdateFrom(info);
            else Apps.Add(new AppVolumeViewModel(_audio, info));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audio.Dispose();
    }
}
```

- [ ] **Step 4: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: MainViewModel com lista ao vivo de apps"
```

---

### Task 7: Log em arquivo

**Files:**
- Create: `VolumeMixer/Infrastructure/AppLog.cs`
- Test: `tests/VolumeMixer.Tests/Infrastructure/AppLogTests.cs`

**Interfaces:**
- Consumes: nada
- Produces: `sealed class AppLog { AppLog(string directory); void Info(string message); void Error(string message, Exception? ex = null) }` — uma linha por evento, arquivo `app-AAAA-MM-DD.log`

- [ ] **Step 1: Escrever teste falhando**

`tests/VolumeMixer.Tests/Infrastructure/AppLogTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — `AppLog` não existe

- [ ] **Step 3: Implementar**

`VolumeMixer/Infrastructure/AppLog.cs`:

```csharp
namespace VolumeMixer.Infrastructure;

public sealed class AppLog
{
    private readonly string _directory;
    private static readonly object Gate = new();

    public AppLog(string? directory = null)
        => _directory = directory
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "VolumeMixer", "logs");

    public void Info(string message) => Write("INFO", message, ex: null);
    public void Error(string message, Exception? ex = null) => Write("ERRO", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var file = Path.Combine(_directory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
            if (ex is not null) line += Environment.NewLine + ex;
            lock (Gate) File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* logging nunca derruba o app */ }
    }
}
```

- [ ] **Step 4: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: log diario em LOCALAPPDATA"
```

---

### Task 8: Autostart no registro

**Files:**
- Create: `VolumeMixer/Infrastructure/StartupRegistry.cs`
- Test: `tests/VolumeMixer.Tests/Infrastructure/StartupRegistryTests.cs`

**Interfaces:**
- Consumes: nada
- Produces: `sealed class StartupRegistry { StartupRegistry(string? runKeyPath = null); bool IsEnabled(); void Enable(); void Disable(); }` — padrão usa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `"VolumeMixer"`

- [ ] **Step 1: Escrever testes falhando (usam chave temporária real em HKCU)**

`tests/VolumeMixer.Tests/Infrastructure/StartupRegistryTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e verificar falha**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: FAIL — `StartupRegistry` não existe

- [ ] **Step 3: Implementar**

`VolumeMixer/Infrastructure/StartupRegistry.cs`:

```csharp
using Microsoft.Win32;

namespace VolumeMixer.Infrastructure;

public sealed class StartupRegistry
{
    public const string AppValueName = "VolumeMixer";
    private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _runKeyPath;

    public StartupRegistry(string? runKeyPath = null) => _runKeyPath = runKeyPath ?? DefaultRunKeyPath;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
        return key?.GetValue(AppValueName) is string;
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("ProcessPath indisponível");
        key.SetValue(AppValueName, $"\"{exe}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (key?.GetValue(AppValueName) is not null)
            key.DeleteValue(AppValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 4: Rodar e verificar passagem**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: autostart via chave Run do usuario"
```

---

### Task 9: Interop CoreAudio — parte A (endpoint/master)

**Files:**
- Create: `VolumeMixer/Audio/CoreAudioInterop.cs`
- Create: `VolumeMixer/Audio/AudioController.cs` (parte A: master; sessões chegam na Task 10)
- Test: `tests/VolumeMixer.Tests/Audio/AudioMasterIntegrationTests.cs`

**Interfaces:**
- Consumes: `IAudioController`, `MasterInfo`
- Produces: `internal` interfaces COM (`IMMDeviceEnumerator`, `IMMDevice`, `IAudioEndpointVolume`, `IAudioEndpointVolumeCallback`, `IMMNotificationClient`, `MMDeviceEnumeratorComObject`) e `sealed class AudioController : IAudioController` — nesta task implementa só o grupo master + ciclo de vida; `GetSessions()` retorna lista vazia provisória até a Task 10

- [ ] **Step 1: Criar `VolumeMixer/Audio/CoreAudioInterop.cs`**

Declarações COM completas (ordem da vtable é contratual — não reordenar nem remover métodos intermediários):

```csharp
using System.Runtime.InteropServices;

namespace VolumeMixer.Audio;

internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

internal static class ComCtx
{
    internal const int ClsCtxAll = 0x17;
    internal static readonly Guid Empty = Guid.Empty;
}

/// <summary>Co-cria o enumerador de dispositivos (CLSID MMDeviceEnumerator).</summary>
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject { }

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices); // não usado; ocupa slot
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    void GetDevice(string deviceId, out IMMDevice device);                          // não usado; ocupa slot
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged(string deviceId, int newState);
    void OnDeviceAdded(string deviceId);
    void OnDeviceRemoved(string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId);
    void OnPropertyValueChanged(string deviceId, PropertyKey key);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

/// <summary>vtable completa até GetMute (slots 1–13) — ordem obrigatória.</summary>
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void GetChannelCount(out int channelCount);
    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute(bool mute, ref Guid eventContext);
    void GetMute(out bool mute);
}

[ComImport, Guid("656805A6-2B99-47A9-AF53-D7CE973A0E4C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    void OnNotify(IntPtr notifyData); // AUDIO_VOLUME_NOTIFICATION_DATA*
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    public bool Muted;
    public float MasterVolume;
    public uint Channels;
    public IntPtr ChannelVolumes; // float[] com Channels elementos
}
```

- [ ] **Step 2: Criar `VolumeMixer/Audio/AudioController.cs` (parte A)**

```csharp
using System.Runtime.InteropServices;
using VolumeMixer.Models;

namespace VolumeMixer.Audio;

public sealed partial class AudioController : IAudioController
{
    private readonly SynchronizationContext? _syncContext;
    private readonly ComCallbacks _callbacks;
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _endpoint;
    private bool _disposed;

    public event EventHandler? SessionsChanged;
    public event EventHandler? MasterChanged;

    public AudioController()
    {
        _syncContext = SynchronizationContext.Current;
        _callbacks = new ComCallbacks(this);
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _deviceEnumerator.RegisterEndpointNotificationCallback(_callbacks.DeviceNotifications);
        ActivateDefaultDevice();
    }

    private void ActivateDefaultDevice()
    {
        _deviceEnumerator!.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out _device);
        var iidEndpoint = typeof(IAudioEndpointVolume).GUID;
        _device!.Activate(ref iidEndpoint, ComCtx.ClsCtxAll, IntPtr.Zero, out var endpointObj);
        _endpoint = (IAudioEndpointVolume)endpointObj;
        _endpoint.RegisterControlChangeNotify(_callbacks.EndpointCallback);
    }

    public MasterInfo GetMaster()
    {
        ThrowIfDisposed();
        _endpoint!.GetMasterVolumeLevelScalar(out var level);
        _endpoint.GetMute(out var mute);
        return new MasterInfo(Math.Round(level * 100d), mute);
    }

    public void SetMasterVolume(double percent)
    {
        ThrowIfDisposed();
        var level = (float)(Math.Clamp(percent, 0, 100) / 100d);
        var ctx = ComCtx.Empty;
        _endpoint!.SetMasterVolumeLevelScalar(level, ref ctx);
    }

    public void SetMasterMute(bool mute)
    {
        ThrowIfDisposed();
        var ctx = ComCtx.Empty;
        _endpoint!.SetMute(mute, ref ctx);
    }

    public IReadOnlyList<AppVolume> GetSessions() => Array.Empty<AppVolume>(); // Task 10
    public void SetSessionVolume(int processId, double percent) { }             // Task 10
    public void SetSessionMute(int processId, bool mute) { }                    // Task 10

    /// <summary>Reenvia evento na thread da UI (capturada no ctor).</summary>
    private void Post(EventHandler? handler)
    {
        if (handler is null) return;
        if (_syncContext is not null) _syncContext.Post(_ => handler(this, EventArgs.Empty), null);
        else handler(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_endpoint is not null) _endpoint.UnregisterControlChangeNotify(_callbacks.EndpointCallback); } catch { }
        try { _deviceEnumerator?.UnregisterEndpointNotificationCallback(_callbacks.DeviceNotifications); } catch { }
        Release(ref _endpoint);
        Release(ref _device);
        Release(ref _deviceEnumerator);
        GC.SuppressFinalize(this);
    }

    private static void Release<T>(ref T? comObject) where T : class
    {
        if (comObject is null) return;
        try { _ = Marshal.ReleaseComObject(comObject); } catch { }
        comObject = null;
    }
}
```

- [ ] **Step 3: Criar `VolumeMixer/Audio/ComCallbacks.cs` (classes de callback)**

```csharp
using System.Runtime.InteropServices;

namespace VolumeMixer.Audio;

/// <summary>Agrupa todos os callbacks COM; repassa ao controller via Post().</summary>
internal sealed class ComCallbacks :
    IAudioEndpointVolumeCallback,
    IMMNotificationClient
{
    private readonly AudioController _owner;

    public ComCallbacks(AudioController owner) => _owner = owner;

    public IAudioEndpointVolumeCallback EndpointCallback => this;
    public IMMNotificationClient DeviceNotifications => this;

    public void OnNotify(IntPtr notifyData)
    {
        // O buffer é propriedade do Windows — apenas ler, nunca liberar.
        try { Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData); } catch { }
        _owner.NotifyMasterChanged();
    }

    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        if (flow == EDataFlow.Render) _owner.RebuildAll();
    }

    public void OnDeviceStateChanged(string deviceId, int newState) { }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
}
```

Adicionar em `AudioController.cs` (partial) os métodos chamados pelos callbacks:

```csharp
public partial class AudioController
{
    internal void NotifyMasterChanged() => Post(MasterChanged);

    internal void RebuildAll()
    {
        if (_disposed) return;
        try
        {
            try { if (_endpoint is not null) _endpoint.UnregisterControlChangeNotify(_callbacks.EndpointCallback); } catch { }
            Release(ref _endpoint);
            Release(ref _device);
            ActivateDefaultDevice();
            OnSessionsCacheInvalidated();
            NotifyMasterChanged();
        }
        catch { Post(SessionsChanged); }
    }

    partial void OnSessionsCacheInvalidated(); // Task 10 implementa
}
```

Nota: como `OnSessionsCacheInvalidated` é `partial`, criar já a declaração acima dentro de um segundo bloco `partial class` no mesmo arquivo ou em `ComCallbacks.cs` — a implementação vazia implícita vale até a Task 10. (Se preferir não usar partial, substitua por um delegate interno `internal Action? SessionsCacheInvalidated;` e chame-o com null-check.)

- [ ] **Step 4: Escrever teste de integração (usa dispositivo de áudio real)**

`tests/VolumeMixer.Tests/Audio/AudioMasterIntegrationTests.cs`:

```csharp
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
```

- [ ] **Step 5: Rodar testes de integração (áudio real da máquina)**

Run: `dotnet test tests/VolumeMixer.Tests --filter Category=Integration`
Expected: PASS (2 testes). Se falhar com COMException, conferir se há dispositivo de saída padrão ativo.

- [ ] **Step 6: Rodar suíte completa**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (unitários + integração)

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: interop CoreAudio do endpoint master com notificacoes"
```

---

### Task 10: Interop CoreAudio — parte B (sessões por aplicativo)

**Files:**
- Modify: `VolumeMixer/Audio/CoreAudioInterop.cs` (adicionar interfaces de sessão)
- Modify: `VolumeMixer/Audio/AudioController.cs` (implementar grupo de sessões)
- Create: `VolumeMixer/Audio/SessionCallbacks.cs`
- Test: `tests/VolumeMixer.Tests/Audio/AudioSessionsIntegrationTests.cs`

**Interfaces:**
- Consumes: interop da Task 9, `AppVolume`
- Produces: `GetSessions()/SetSessionVolume(pid,%)/SetSessionMute(pid,bool)` funcionais; `SessionsChanged` disparado em criação/término/volume externo; ícones PNG por processo

- [ ] **Step 1: Adicionar interfaces de sessão ao `CoreAudioInterop.cs`**

```csharp
/// <summary>vtable completa de IAudioSessionControl (9 slots) — ordem obrigatória.</summary>
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out int state); // 0=Inactive 1=Active 2=Expired
    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void SetDisplayName(string name, ref Guid eventContext);
    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    void SetIconPath(string path, ref Guid eventContext);
    void GetGroupingParam(out Guid groupingId);
    void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents events);
    void UnregisterAudioSessionNotification(IAudioSessionEvents events);
}

/// <summary>Extensão: mesmos 9 slots + 3 próprios, numa única declaração (QI direto).</summary>
[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    void GetState(out int state);
    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void SetDisplayName(string name, ref Guid eventContext);
    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    void SetIconPath(string path, ref Guid eventContext);
    void GetGroupingParam(out Guid groupingId);
    void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents events);
    void UnregisterAudioSessionNotification(IAudioSessionEvents events);
    void GetProcessId(out uint processId);
    void IsSystemSoundsSession();
    void SetDuckingPreference(bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute(bool mute, ref Guid eventContext);
    void GetMute(out bool mute);
}

/// <summary>vtable completa (5 slots) — ordem obrigatória.</summary>
[ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    void OnDisplayNameChanged(string displayName, ref Guid eventContext);
    void OnIconPathChanged(string iconPath, ref Guid eventContext);
    void OnVolumeChanged(float newVolume, bool newMute, ref Guid eventContext);
    void OnStateChanged(int newState);
    void OnSessionDisconnected(int disconnectReason);
}

[ComImport, Guid("67598B03-F5E7-4AFB-80EC-EAAF029EF668"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    void OnSessionCreated(IAudioSessionControl newSession);
}

/// <summary>vtable: 2 herdados de IAudioSessionManager + 3 próprios — ordem obrigatória.</summary>
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A37B6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IAudioSessionControl control); // slot herdado
    void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out ISimpleAudioVolume volume);      // slot herdado
    void GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    void RegisterSessionNotification(IAudioSessionNotification notification);
    void UnregisterSessionNotification(IAudioSessionNotification notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int sessionCount);
    void GetSession(int index, out IAudioSessionControl session);
}

internal static class AudioSessionState
{
    internal const int Inactive = 0;
    internal const int Active = 1;
    internal const int Expired = 2;
}
```

- [ ] **Step 2: Criar `VolumeMixer/Audio/SessionCallbacks.cs`**

```csharp
using System.Runtime.InteropServices;

namespace VolumeMixer.Audio;

/// <summary>Assina eventos de UMA sessão; qualquer mudança relevante invalida a lista.</summary>
internal sealed class SessionEventSink : IAudioSessionEvents
{
    private readonly AudioController _owner;
    public SessionEventSink(AudioController owner) => _owner = owner;

    public void OnVolumeChanged(float newVolume, bool newMute, ref Guid ctx) => _owner.NotifySessionsChanged();
    public void OnStateChanged(int newState) => _owner.NotifySessionsChanged();
    public void OnSessionDisconnected(int reason) => _owner.NotifySessionsChanged();
    public void OnDisplayNameChanged(string displayName, ref Guid ctx) { }
    public void OnIconPathChanged(string iconPath, ref Guid ctx) { }
}

internal sealed class SessionNotificationSink : IAudioSessionNotification
{
    private readonly AudioController _owner;
    public SessionNotificationSink(AudioController owner) => _owner = owner;

    public void OnSessionCreated(IAudioSessionControl newSession) => _owner.NotifySessionsChanged();
}
```

- [ ] **Step 3: Implementar o grupo de sessões em `AudioController.cs`**

Substituir os três stubs da Task 9 e adicionar campos/métodos:

```csharp
// Novos campos:
private IAudioSessionManager2? _sessionManager;
private IAudioSessionEnumerator? _sessionEnumerator;
private readonly Dictionary<int, List<ISimpleAudioVolume>> _pidVolumes = new();
private readonly Dictionary<int, SessionEventSink> _sessionSinks = new();
private readonly SessionNotificationSink _sessionNotificationSink;
private static readonly ConcurrentDictionary<string, byte[]> IconCacheByExe = new(StringComparer.OrdinalIgnoreCase);

// No construtor, antes de ActivateDefaultDevice():
_sessionNotificationSink = new SessionNotificationSink(this);

// ActivateDefaultDevice(), após registrar o endpoint:
var iidManager = typeof(IAudioSessionManager2).GUID;
_device!.Activate(ref iidManager, ComCtx.ClsCtxAll, IntPtr.Zero, out var managerObj);
_sessionManager = (IAudioSessionManager2)managerObj;
_sessionManager.RegisterSessionNotification(_sessionNotificationSink);
_sessionManager.GetSessionEnumerator(out _sessionEnumerator);
RefreshSessionCache();

// Substituir partial void OnSessionsCacheInvalidated() e os stubs:
partial void OnSessionsCacheInvalidated() => RefreshSessionCache();

internal void NotifySessionsChanged() => Post(SessionsChanged);

private void RefreshSessionCache()
{
    ReleasePidVolumes();
    if (_sessionEnumerator is null) return;

    _sessionEnumerator.GetCount(out var count);
    for (var i = 0; i < count; i++)
    {
        try
        {
            _sessionEnumerator.GetSession(i, out var control);
            var control2 = (IAudioSessionControl2)control;
            control2.GetState(out var state);
            if (state == AudioSessionState.Expired) continue;

            control2.GetProcessId(out var pidNative);
            var pid = (int)pidNative;
            if (pid == 0 || control2.IsSystemSoundsSessionSafe()) continue;

            var volume = (ISimpleAudioVolume)control; // QI direto no objeto da sessão
            if (!_pidVolumes.TryGetValue(pid, out var list))
                _pidVolumes[pid] = list = new List<ISimpleAudioVolume>();
            list.Add(volume);

            if (!_sessionSinks.ContainsKey(pid))
            {
                var sink = new SessionEventSink(this);
                control.RegisterAudioSessionNotification(sink);
                _sessionSinks[pid] = sink;
            }
        }
        catch { /* sessão pode morrer no meio da enumeração */ }
    }
}

private void ReleasePidVolumes()
{
    foreach (var sink in _sessionSinks.Values) { /* sinks são managed; GC cuida */ }
    _sessionSinks.Clear();
    foreach (var list in _pidVolumes.Values)
        foreach (var vol in list)
            try { _ = Marshal.ReleaseComObject(vol); } catch { }
    _pidVolumes.Clear();
}

public IReadOnlyList<AppVolume> GetSessions()
{
    ThrowIfDisposed();
    var result = new List<AppVolume>();
    foreach (var (pid, volumes) in _pidVolumes)
    {
        if (volumes.Count == 0) continue;
        volumes[0].GetMasterVolume(out var level);
        volumes[0].GetMute(out var mute);
        result.Add(new AppVolume(pid, ResolveProcessName(pid), ResolveIconPng(pid), Math.Round(level * 100d), mute));
    }
    return result.OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
}

public void SetSessionVolume(int processId, double percent)
{
    ThrowIfDisposed();
    var level = (float)(Math.Clamp(percent, 0, 100) / 100d);
    if (_pidVolumes.TryGetValue(processId, out var volumes))
        foreach (var volume in volumes)
        {
            var ctx = ComCtx.Empty;
            try { volume.SetMasterVolume(level, ref ctx); } catch { }
        }
}

public void SetSessionMute(int processId, bool mute)
{
    ThrowIfDisposed();
    if (_pidVolumes.TryGetValue(processId, out var volumes))
        foreach (var volume in volumes)
        {
            var ctx = ComCtx.Empty;
            try { volume.SetMute(mute, ref ctx); } catch { }
        }
}

private static string ResolveProcessName(int pid)
{
    try { return Process.GetProcessById(pid).ProcessName; }
    catch { return "Aplicativo desconhecido"; }
}

private static byte[]? ResolveIconPng(int pid)
{
    try
    {
        var process = Process.GetProcessById(pid);
        var exePath = process.MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
        return IconCacheByExe.GetOrAdd(exePath, static path =>
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path)!;
            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        });
    }
    catch { return null; }
}
```

Helper de QI seguro (adicionar à classe estática interna, ex.: fim do `CoreAudioInterop.cs`):

```csharp
internal static class SessionControlExtensions
{
    /// <summary>IsSystemSoundsSession retorna HRESULT; alguns hosts lançam — tratar como falso.</summary>
    public static bool IsSystemSoundsSessionSafe(this IAudioSessionControl2 control)
    {
        try { control.IsSystemSoundsSession(); return true; }
        catch (COMException) { return false; } // S_FALSE chega como exceção → não é som do sistema
    }
}
```

Atualizar também `Dispose()` para liberar sessões antes do endpoint:

```csharp
try { _sessionManager?.UnregisterSessionNotification(_sessionNotificationSink); } catch { }
ReleasePidVolumes();
Release(ref _sessionEnumerator);
Release(ref _sessionManager);
```

- [ ] **Step 4: Escrever teste de integração de sessões**

`tests/VolumeMixer.Tests/Audio/AudioSessionsIntegrationTests.cs`:

```csharp
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
```

- [ ] **Step 5: Rodar suíte completa**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

Verificação manual desta etapa (documentada, sem automação): abrir YouTube num navegador, rodar o teste anterior e confirmar que `sessions` contém o PID do navegador com nome correto.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: sessoes de audio por aplicativo com agrupamento por pid"
```

---

### Task 11: TrayService (ícone, menu, alternância do popup)

**Files:**
- Create: `VolumeMixer/Infrastructure/TrayIconFactory.cs`
- Create: `VolumeMixer/Infrastructure/TrayService.cs`
- Verify: `dotnet build` + checklist manual

**Interfaces:**
- Consumes: `MainViewModel`, `StartupRegistry`, `PopupWindow` (Task 12 — nesta task, factory injetável `Func<PopupWindow>`; compilar com stub mínimo se necessário)
- Produces: `sealed class TrayService : IDisposable` com `event EventHandler? ClosingRequested`, construtor `(MainViewModel vm, Func<PopupWindow> popupFactory, StartupRegistry startup)`

- [ ] **Step 1: Criar `TrayIconFactory.cs` (ícone desenhado em runtime — sem binário no repo)**

```csharp
using System.Drawing;

namespace VolumeMixer.Infrastructure;

internal static class TrayIconFactory
{
    /// <summary>Alto-falante simples desenhado em 16x16; evita asset binário.</summary>
    public static Icon Create()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
        // caixa do alto-falante
        g.FillPolygon(brush, new[] { new Point(2, 6), new Point(6, 6), new Point(10, 2), new Point(10, 14), new Point(6, 10), new Point(2, 10) });
        // ondas
        using var pen = new Pen(brush, 1.6f);
        g.DrawArc(pen, 10, 4, 4, 8, -60, 120);
        g.DrawArc(pen, 12, 2, 6, 12, -60, 120);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
```

- [ ] **Step 2: Criar `TrayService.cs`**

```csharp
using System.Windows.Forms;
using VolumeMixer.ViewModels;

namespace VolumeMixer.Infrastructure;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly Func<Views.PopupWindow> _popupFactory;
    private readonly StartupRegistry _startup;
    private readonly ToolStripMenuItem _startupItem;
    private Views.PopupWindow? _popup;

    public event EventHandler? ClosingRequested;

    public TrayService(MainViewModel viewModel, Func<Views.PopupWindow> popupFactory, StartupRegistry startup)
    {
        _viewModel = viewModel;
        _popupFactory = popupFactory;
        _startup = startup;

        _startupItem = new ToolStripMenuItem("Iniciar com Windows")
        {
            Checked = startup.IsEnabled(),
        };
        _startupItem.Click += (_, _) =>
        {
            if (_startup.IsEnabled()) _startup.Disable(); else _startup.Enable();
            _startupItem.Checked = _startup.IsEnabled();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => TogglePopup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ClosingRequested?.Invoke(this, EventArgs.Empty));
        menu.Opening += (_, _) => _startupItem.Checked = _startup.IsEnabled();

        _icon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Volume Mixer",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += OnMouseClick;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) TogglePopup();
    }

    public void TogglePopup()
    {
        if (_popup is { IsLoaded: true })
        {
            _popup.Close();
            _popup = null;
            return;
        }
        _popup = _popupFactory();
        _popup.Show();
        _popup.Activate();
    }

    public void Dispose()
    {
        _popup?.Close();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

- [ ] **Step 3: Stub temporário do PopupWindow (se a Task 12 ainda não foi executada)**

Criar `VolumeMixer/Views/PopupWindow.xaml(.cs)` mínimo para compilar (será expandido na Task 12):

```xml
<Window x:Class="VolumeMixer.Views.PopupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Volume Mixer" Width="360" SizeToContent="Height"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True">
  <Border Background="#F3F3F3" CornerRadius="8" Padding="12">
    <TextBlock Text="Popup placeholder" />
  </Border>
</Window>
```

```csharp
namespace VolumeMixer.Views;

public partial class PopupWindow : System.Windows.Window
{
    public PopupWindow() => InitializeComponent();
}
```

- [ ] **Step 4: Verificar compilação**

Run: `dotnet build VolumeMixer`
Expected: BUILD SUCCESSFUL, 0 erros

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: TrayService com menu autostart e toggle do popup"
```

---

### Task 12: PopupWindow completa (layout, estilos, posicionamento, fechamento)

**Files:**
- Create: `VolumeMixer/Views/Styles.xaml`
- Modify: `VolumeMixer/Views/PopupWindow.xaml` (substituir stub)
- Modify: `VolumeMixer/Views/PopupWindow.xaml.cs`
- Verify: `dotnet build` + checklist manual

**Interfaces:**
- Consumes: `MainViewModel`, `MasterViewModel`, `AppVolumeViewModel` (propriedades das Tasks 4–6)
- Produces: janela flyout funcional ligada por binding; handlers de código `CommitText` (Enter/LostFocus) e filtro de dígitos

- [ ] **Step 1: Criar `VolumeMixer/Views/Styles.xaml`**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- ======== TEMA: ajuste aqui cores, raios e fontes ======== -->
    <SolidColorBrush x:Key="PanelBrush" Color="#F7F7F7" />
    <SolidColorBrush x:Key="RowBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="ForegroundBrush" Color="#1B1B1B" />
    <SolidColorBrush x:Key="AccentBrush" Color="#0078D7" />
    <SolidColorBrush x:Key="HoverBrush" Color="#EAEAEA" />

    <Style x:Key="PanelBorder" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource PanelBrush}" />
        <Setter Property="CornerRadius" Value="10" />
        <Setter Property="Padding" Value="12" />
        <Setter Property="BorderBrush" Value="#CCCCCC" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Effect">
            <Setter.Value>
                <DropShadowEffect BlurRadius="16" ShadowDepth="2" Opacity="0.35" />
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="RowBorder" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource RowBrush}" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="10,8" />
        <Setter Property="Margin" Value="0,4" />
    </Style>

    <Style x:Key="PanelText" TargetType="TextBlock">
        <Setter Property="Foreground" Value="{StaticResource ForegroundBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="FontSize" Value="13" />
    </Style>

    <Style x:Key="AppIcon" TargetType="Image">
        <Setter Property="Width" Value="20" />
        <Setter Property="Height" Value="20" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Margin" Value="0,0,8,0" />
    </Style>

    <!-- Slider com acento do tema -->
    <Style x:Key="VolumeSlider" TargetType="Slider">
        <Setter Property="Minimum" Value="0" />
        <Setter Property="Maximum" Value="100" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Width" Value="140" />
        <Setter Property="Margin" Value="0,0,8,0" />
        <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
    </Style>

    <Style x:Key="VolumeBox" TargetType="TextBox">
        <Setter Property="Width" Value="48" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="HorizontalAlignment" Value="Center" />
        <Setter Property="MaxLength" Value="3" />
        <Setter Property="Margin" Value="0,0,6,0" />
        <Setter Property="Padding" Value="2,2" />
        <Setter Property="TextAlignment" Value="Center" />
    </Style>

    <Style x:Key="MuteButton" TargetType="Button">
        <Setter Property="Width" Value="28" />
        <Setter Property="Height" Value="28" />
        <Setter Property="Content" Value="🔇" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Opacity" Value="0.35" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsMuted}" Value="True">
                <Setter Property="Opacity" Value="1" />
            </DataTrigger>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Opacity" Value="0.8" />
            </Trigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 2: Substituir `PopupWindow.xaml`**

```xml
<Window x:Class="VolumeMixer.Views.PopupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Volume Mixer" Width="360" SizeToContent="Height"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True"
        Deactivated="OnDeactivated" PreviewKeyDown="OnPreviewKeyDown"
        Loaded="OnLoaded" ContentRendered="OnContentRenderedFirstTime">
  <Window.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Styles.xaml" />
      </ResourceDictionary.MergedDictionaries>

      <!-- Template da linha MASTER -->
      <DataTemplate x:Key="MasterTemplate">
        <Border Style="{StaticResource RowBorder}">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="🔊" FontSize="16" VerticalAlignment="Center" Margin="0,0,8,0" />
            <TextBlock Grid.Column="1" Text="Master" Style="{StaticResource PanelText}" FontWeight="SemiBold"
                       Margin="0,0,150,0" />
            <Slider Grid.Column="1" Style="{StaticResource VolumeSlider}"
                    HorizontalAlignment="Right" Maximum="100"
                    Value="{Binding VolumePercent, Mode=TwoWay}" />
            <TextBox Grid.Column="2" Style="{StaticResource VolumeBox}"
                     Text="{Binding VolumeText, UpdateSourceTrigger=PropertyChanged}"
                     PreviewTextInput="OnDigitsOnly"
                     KeyDown="OnVolumeBoxKeyDown" LostFocus="OnVolumeBoxLostFocus" />
            <Button Grid.Column="3" Style="{StaticResource MuteButton}" Command="{Binding ToggleMute}" />
          </Grid>
        </Border>
      </DataTemplate>

      <!-- Template da linha de APP -->
      <DataTemplate x:Key="AppTemplate">
        <Border Style="{StaticResource RowBorder}">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Image Grid.Column="0" Style="{StaticResource AppIcon}"
                   Source="{Binding Icon}" Stretch="Uniform" />
            <TextBlock Grid.Column="1" Text="{Binding ProcessName}" Style="{StaticResource PanelText}"
                       TextTrimming="CharacterEllipsis" Margin="0,0,150,0" />
            <Slider Grid.Column="1" Style="{StaticResource VolumeSlider}"
                    HorizontalAlignment="Right" Maximum="100"
                    Value="{Binding VolumePercent, Mode=TwoWay}" />
            <TextBox Grid.Column="2" Style="{StaticResource VolumeBox}"
                     Text="{Binding VolumeText, UpdateSourceTrigger=PropertyChanged}"
                     PreviewTextInput="OnDigitsOnly"
                     KeyDown="OnVolumeBoxKeyDown" LostFocus="OnVolumeBoxLostFocus" />
            <Button Grid.Column="3" Style="{StaticResource MuteButton}" Command="{Binding ToggleMute}" />
          </Grid>
        </Border>
      </DataTemplate>
    </ResourceDictionary>
  </Window.Resources>

  <Border Style="{StaticResource PanelBorder}">
    <StackPanel>
      <ContentControl Content="{Binding Master}" ContentTemplate="{StaticResource MasterTemplate}" />
      <ScrollViewer MaxHeight="340" VerticalScrollBarVisibility="Auto">
        <ItemsControl ItemsSource="{Binding Apps}"
                      ItemTemplate="{StaticResource AppTemplate}" />
      </ScrollViewer>
    </StackPanel>
  </Border>
</Window>
```

- [ ] **Step 3: Substituir `PopupWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VolumeMixer.ViewModels;

namespace VolumeMixer.Views;

public partial class PopupWindow : Window
{
    private bool _positioned;

    public PopupWindow() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void OnDeactivated(object sender, EventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Altura máxima = 60% da área de trabalho (spec §5)
        MaxHeight = SystemParameters.WorkArea.Height * 0.6;
    }

    private void OnContentRenderedFirstTime(object sender, EventArgs e)
    {
        if (_positioned) return;
        _positioned = true;
        PositionNearTray();
    }

    /// <summary>Canto inferior direito da área de trabalho, 8px de margem, ciente de DPI.</summary>
    private void PositionNearTray()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
        if (area is null) return;
        var dpi = (PresentationSource.FromVisual(this) as HwndSource)
                  ?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var widthPx = ActualWidth * dpi;
        var heightPx = ActualHeight * dpi;
        Left = (area.Value.Right - widthPx - 8) / dpi;
        Top = (area.Value.Bottom - heightPx - 8) / dpi;
    }

    // ===== Caixa de digitação (compartilhada por master e apps) =====

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

    private void OnVolumeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        CommitAndReleaseFocus(sender);
        e.Handled = true;
    }

    private void OnVolumeBoxLostFocus(object sender, RoutedEventArgs e)
        => Commit(sender);

    private static void CommitAndReleaseFocus(object sender)
    {
        Commit(sender);
        if (sender is System.Windows.Controls.TextBox box)
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private static void Commit(object sender)
    {
        var vm = (sender as FrameworkElement)?.DataContext;
        switch (vm)
        {
            case MasterViewModel m: m.CommitText(); break;
            case AppVolumeViewModel a: a.CommitText(); break;
        }
    }
}
```

- [ ] **Step 4: Verificar compilação**

Run: `dotnet build VolumeMixer`
Expected: BUILD SUCCESSFUL, 0 erros

- [ ] **Step 5: Verificação manual (executar `dotnet run --project VolumeMixer` com DataContext de teste via App temporário ou aguardar Task 13)**

Checklist parcial desta task: slider arrasta; digitar `37` + Enter aplica; `abc` + Enter reverte; Esc fecha; clique fora fecha. (Validação completa acontece na Task 13, quando o DataContext real estiver plugado.)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: popup flyout com templates master/app, posicionamento e fechamento"
```

---

### Task 13: Composition root (App.xaml) + verificação ponta a ponta

**Files:**
- Modify: `VolumeMixer/App.xaml`
- Modify: `VolumeMixer/App.xaml.cs`
- Verify: execução manual do app completo

**Interfaces:**
- Consumes: tudo (Tasks 1–12)
- Produces: app executável de ponta a ponta

- [ ] **Step 1: Ajustar `App.xaml`**

```xml
<Application x:Class="VolumeMixer.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Views/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Substituir `App.xaml.cs`**

```csharp
using System.Windows;
using VolumeMixer.Audio;
using VolumeMixer.Infrastructure;
using VolumeMixer.ViewModels;
using VolumeMixer.Views;

namespace VolumeMixer;

public partial class App : Application
{
    private TrayService? _tray;
    private MainViewModel? _viewModel;
    private readonly AppLog _log = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;

        try
        {
            var audio = new AudioController();
            _viewModel = new MainViewModel(audio);
            var startup = new StartupRegistry();
            _tray = new TrayService(_viewModel, () => new PopupWindow { DataContext = _viewModel }, startup);
            _tray.ClosingRequested += OnClosingRequested;
            _log.Info("aplicativo iniciado");
        }
        catch (Exception ex)
        {
            _log.Error("falha ao iniciar", ex);
            MessageBox.Show("Falha ao iniciar o Volume Mixer. Veja os logs em %LOCALAPPDATA%\\VolumeMixer\\logs.",
                "Volume Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnClosingRequested(object? sender, EventArgs e)
    {
        _log.Info("encerrando pelo menu");
        _viewModel?.Dispose();
        _tray?.Dispose();
        Shutdown();
    }

    private void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("exceção não tratada na UI", e.Exception);
        e.Handled = true; // utilitário de bandeja não pode morrer
    }
}
```

- [ ] **Step 3: Rodar a suíte de testes (regressão)**

Run: `dotnet test tests/VolumeMixer.Tests`
Expected: PASS (todos)

- [ ] **Step 4: Verificação manual ponta a ponta (checklist da spec §8)**

Run: `dotnet run --project VolumeMixer` (deixar rodando)

Executar e registrar resultado de cada item:

1. Ícone aparece na bandeja; clique esquerdo abre popup ancorado à direita/baixo
2. YouTube + Spotify tocando → duas linhas independentes
3. Digitar `37` + Enter num app → aplica e slider acompanha
4. Digitar `abc` + Enter → texto reverte ao valor atual
5. Digitar `250` + Enter → limita a 100
6. Mute por app e master → botão fica opaco quando mutado
7. Abrir painel e conectar fone Bluetooth → lista recarrega
8. Tecla de volume do teclado com painel aberto → slider master acompanha
9. Menu direito → "Iniciar com Windows" liga; chave existe em `HKCU\...\Run`; desligar remove
10. Clique fora fecha; Esc fecha; "Sair" encerra sem deixar processo órfão
11. Escala do Windows em 100% e depois 150% → popup nasce junto à bandeja, sem cortar fora da tela

Critério: todos os itens OK antes do commit.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: composition root com tray, popup e tratamento de erros"
```

---

### Task 14: Publicação single-file

**Files:**
- Create: `VolumeMixer/bin/publish/` (saída, não versionada)
- Modify: `.gitignore` (raiz)

**Interfaces:**
- Consumes: projeto completo
- Produces: `VolumeMixer.exe` único self-contained win-x64

- [ ] **Step 1: Criar `.gitignore` na raiz**

```
bin/
obj/
*.user
```

- [ ] **Step 2: Publicar**

```bash
dotnet publish VolumeMixer -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o VolumeMixer/bin/publish
```

- [ ] **Step 3: Verificar artefato**

Run: `Get-ChildItem VolumeMixer\bin\publish`
Expected: `VolumeMixer.exe` presente (único executável grande, ~60–70 MB)

- [ ] **Step 4: Executar o publicado e repetir o essencial do checklist**

Run: `.\VolumeMixer\bin\publish\VolumeMixer.exe`
Expected: bandeja + popup funcionando igual ao passo dev; encerrar pelo menu "Sair".

- [ ] **Step 5: Commit**

```bash
git add .gitignore
git commit -m "build: publicacao single-file self-contained win-x64"
```
