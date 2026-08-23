# Especificação — Volume Mixer Tray (WPF)

**Data:** 2026-08-23
**Status:** Aprovada pelo desenvolvedor em conversa (design validado em 2 partes)

## 1. Visão geral

Aplicativo de bandeja (system tray) para Windows que abre um mini painel
(flyout) com controle de volume **master** e **por aplicativo**, incluindo
digitação do valor exato numa caixa de texto.

- **Plataforma:** Windows 10/11, x64
- **Stack:** WPF, .NET 8 LTS (`net8.0-windows`), MVVM leve
- **Dependências de terceiros:** nenhuma (interop COM direto + `NotifyIcon`
  via interop WinForms embutido no SDK)
- **Distribuição:** executável single-file self-contained (sem instalador)

## 2. Objetivos e não-objetivos

### Objetivos

1. Ícone próprio na bandeja; clique esquerdo abre o painel, clique direito
   abre menu (*Abrir*, *Iniciar com Windows*, *Sair*).
2. Painel flyout sem borda, ancorado perto da bandeja, que fecha ao perder foco.
3. Controle master: slider 0–100, caixa de digitação, botão de mute.
4. Lista ao vivo dos apps reproduzindo áudio no momento, cada um com slider,
   caixa de digitação e mute individuais.
5. Opção "Iniciar com Windows" persistida em `HKCU\...\Run`.
6. Estilos centralizados em XAML para customização fácil de tema.

### Não-objetivos (fase atual)

- Balanceamento de canais (L/R), equalização, dispositivos de saída múltiplos
- Sessões de gravação/captura (microfone)
- Instalador MSI/Inno Setup
- Multi-idioma (UI em pt-BR)

## 3. Arquitetura

Projeto único `VolumeMixer` com pastas por responsabilidade:

```
VolumeMixer/
├── App.xaml(.cs)          → startup sem MainWindow; inicializa tray
├── Audio/
│   ├── CoreAudioInterop.cs   → interfaces/pinvokes COM (WASAPI)
│   ├── AudioController.cs    → facade IAudioController sobre CoreAudio
│   └── SessionEvents.cs      → handler de IAudioSessionEvents
├── Models/
│   └── AppVolume.cs          → records MasterInfo e AppVolume (sem tipos de UI)
├── ViewModels/
│   ├── MainViewModel.cs      → master + coleção de apps
│   ├── AppVolumeViewModel.cs → linha de um app (slider/textbox/mute)
│   └── MasterViewModel.cs    → linha master
├── Views/
│   ├── PopupWindow.xaml(.cs) → flyout sem borda
│   └── Styles.xaml           → dicionário de estilos/tema
├── Infrastructure/
│   ├── TrayService.cs        → NotifyIcon, menu, posicionamento do popup
│   ├── StartupRegistry.cs    → HKCU Run key
│   └── Log.cs                → log em %LOCALAPPDATA%\VolumeMixer\logs
└── VolumeMixer.csproj
```

Regra de dependência: `ViewModels` não referencia COM diretamente — só a
interface `IAudioController`. `Audio` não conhece UI.

## 4. Camada de áudio

### Interfaces COM usadas (WASAPI / CoreAudio)

| Interface | Uso |
|---|---|
| `IMMDeviceEnumerator` (`MMDeviceEnumerator` CLSID) | obter dispositivo padrão de renderização |
| `IMMDevice` + `Activate<IAudioEndpointVolume>` | volume/mute master |
| `IAudioEndpointVolume` | `Get/SetMasterVolumeLevelScalar`, `Get/SetMute`, callback `OnVolumeNotification` |
| `IAudioSessionManager2` (ativado do mesmo `IMMDevice`) | acesso às sessões |
| `IAudioSessionEnumerator` + `IAudioSessionControl2` | enumerar sessões; PID, estado, `ProcessId` |
| `ISimpleAudioVolume` | volume/mute por sessão |
| `IAudioSessionEvents` | notificações: sessão criada/morta, volume/mute mudou |

### Facade exposto à aplicação

```csharp
public sealed record MasterInfo(double VolumePercent, bool Mute);

public sealed record AppVolume(
    int ProcessId,
    string ProcessName,     // "chrome", ou "Aplicativo desconhecido"
    byte[]? IconPng,        // ícone do exe codificado em PNG; null → genérico
    double VolumePercent,
    bool Mute);

public interface IAudioController : IDisposable
{
    MasterInfo GetMaster();
    void SetMasterVolume(double percent);   // clamp 0–100
    void SetMasterMute(bool mute);

    IReadOnlyList<AppVolume> GetSessions();
    void SetSessionVolume(int processId, double percent);
    void SetSessionMute(int processId, bool mute);

    event EventHandler? SessionsChanged;    // sessão criada/expirou
    event EventHandler? MasterChanged;      // teclado multimídia etc.
}
```

### Regras da camada de áudio

- **Agrupamento:** sessões do mesmo PID viram um único `AppVolume`; escrever no
  grupo aplica em todas as sessões daquele PID.
- **Ícones:** `Icon.ExtractAssociatedIcon(exePath)` convertido para PNG
  (`byte[]`) na camada de áudio; o ViewModel converte para `BitmapSource` e
  cacheia por caminho de executável. Falha → `null` → ícone genérico embutido
  como recurso. Os records (`AppVolume`, `MasterInfo`) vivem em `Models/`,
  fora da pasta `Audio/`, para não acoplar a camada de áudio a tipos de UI.
- **Nome do processo:** `Process.GetProcessById(pid)` com try/catch; processo
  protegido ou morto → `"Aplicativo desconhecido"`.
- **Troca de dispositivo padrão:** registrar `IMMNotificationClient`;
  ao trocar o default, recriar endpoint + reenumerar sessões.
- **Threading:** callbacks COM chegam em thread de pool — todo evento público é
  reemitido já marshalado via `SynchronizationContext` capturado na criação do
  controller (thread da UI).
- **Descarte:** `Dispose()` libera todos os pontos COM (`Marshal.ReleaseComObject`)
  e cancela callbacks antes da coleta.

## 5. Interface

### Bandeja

- `NotifyIcon` com ícone do app (alto-falante, `.ico` gerado no projeto).
- Clique esquerdo (`MouseClick` button left) → alterna popup aberto/fechado.
- Clique direito → menu nativo:
  - **Abrir**
  - **Iniciar com Windows** (checkbox refletindo o registro)
  - **Sair** → dispose do controller de áudio, remove o ícone da bandeja e
    `Application.Current.Shutdown()`

### Popup

- `Window` com `WindowStyle=None`, `AllowsTransparency=True`,
  `ShowInTaskbar=False`, `ShowActivated=true`; largura fixa de 360 px, altura
  automática conforme a lista, com máximo de 60% da área de trabalho e scroll
  além disso.
- Posição: canto inferior direito da área de trabalho (`Screen.PrimaryScreen.WorkingArea`),
  com margem de 8px acima da barra; recalculada a cada abertura (suporta DPI).
- Fecha em `Deactivated` (clique fora) e na tecla **Esc**.
- Layout:

```
┌────────────────────────────────────┐
│ 🔊 Master  [────●───] [ 45 ] [🔇] │
├────────────────────────────────────┤
│ 🌐 chrome  [──●─────] [ 30 ] [🔇] │
│ 🎵 spotify [──────●─] [ 70 ] [🔇] │
└────────────────────────────────────┘
```

- Cada linha: ícone 20×20 + nome (TextTrimming), `Slider` 0–100 (aplica em
  tempo real durante arraste), `TextBox` de 48px, botão mute (ícone alterna).
- `ItemsControl` + `VirtualizingStackPanel` se a lista passar de ~10 itens;
  altura máxima com scroll.

### Caixa de digitação (regra única para master e apps)

- Aceita apenas dígitos (validação no `TextChanged`/binding).
- Aplica em **Enter** ou perda de foco.
- Valor vazio/inválido → reverte para o valor atual.
- Fora de 0–100 → limita (clamp) ao limite estourado.
- Enquanto o usuário digita, o slider não atualiza até aplicar; quando o valor
  vem do sistema (evento), o TextBox reflete sem roubar o foco.

### Estilos

- `Styles.xaml` com cores, brushes, corner radius, hover/pressed dos sliders e
  botões. Trocar tema = trocar este dicionário.

## 6. Comportamentos especiais

| Situação | Comportamento |
|---|---|
| Novo app começa a tocar áudio com painel aberto | linha aparece na hora (evento de sessão criada) |
| App fecha | linha some (sessão expirada); se era a última do PID, remove grupo |
| Dispositivo padrão muda | re-detecta endpoint, recarrega master + sessões |
| Mudança por teclado multimídia | painel reflete (master + sessão afetada) |
| Painel fechado | eventos continuam baratos; nenhuma polling ativa |
| Duplo clique no ícone | tratado como clique esquerdo único (abrir/fechar) |

## 7. Tratamento de erros

- Toda chamada COM encapsulada em try/catch por operação; falha isolada não
  derruba o app nem o painel.
- Log rotativo simples em `%LOCALAPPDATA%\VolumeMixer\logs\app-AAAA-MM-DD.log`
  (um arquivo por dia).
- Falha ao ler/escrever chave `Run` → mensagem discreta no log; menu mostra o
  estado real do registro.
- Se `GetSessions()` falhar inteiramente, painel exibe apenas master + texto
  "Não foi possível listar os aplicativos".

## 8. Testes

### Unitários (xUnit)

Fake de `IAudioController` cobrindo os ViewModels:

- Parsing do TextBox: dígitos, vazio, >100, negativo, Enter vs foco-perdido
- Clamp e reversão de valor inválido
- Toggle de mute (master e app) e estado visual correspondente
- Agrupamento por PID (duas sessões do mesmo processo → uma linha)
- Atualização por evento (`SessionsChanged`) adicionando/removendo linhas

### Manual (checklist no repositório)

1. YouTube + Spotify simultâneos → duas linhas independentes
2. Digitar `37` + Enter num app → volume aplica e slider acompanha
3. Mute por app e master → ícone alterna; som confirma
4. Conectar/desconectar fone Bluetooth com painel aberto → lista recarrega
5. Autostart liga → chave existe em `HKCU\...\Run`; desliga → some
6. DPI 100% e 150% → popup nasce junto à bandeja, sem cortar
7. Clique fora → fecha; Esc → fecha
8. Tecla de volume do teclado com painel aberto → slider master acompanha

## 9. Entrega

- `dotnet publish -c Release -r win-x64 --self-contained true
  /p:PublishSingleFile=true` → exe único (~60–70 MB).
- Alternativa documentada: framework-dependent (exe ~200 KB, exige
  .NET 8 Desktop Runtime instalado).
- Sem instalador nesta fase; execução direta do `.exe`.

## 10. Decisões registradas

| Decisão | Alternativa descartada | Motivo |
|---|---|---|
| WPF | WinForms, Electron, AHK | usuário quer customização visual |
| Interop COM manual | wrapper NuGet de terceiros | zero dependência; wrappers desatualizados |
| `NotifyIcon` (WinForms interop) | Hardcodet.NotifyIcon.Wpf | evita dependência externa |
| .NET 8 LTS | .NET 9 STS | suporte longo |
| Agrupar sessões por PID | uma linha por sessão | UX igual ao mixer do Windows |
