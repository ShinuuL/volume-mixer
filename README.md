# Volume Mixer

Mixer de volume por aplicativo para Windows, residente na bandeja do sistema. Permite ajustar o volume mestre e o volume de cada aplicativo em execução, com suporte a mudo, personalização visual e inicialização automática com o Windows.

## Funcionalidades

- **Volume mestre** — controle do volume geral do sistema com slider e valor numérico.
- **Volume por aplicativo** — lista os aplicativos que estão reproduzindo áudio, com slider, valor numérico e botão de mudo para cada um.
- **Detecção automática** — novos aplicativos de áudio aparecem automaticamente, sem reiniciar o app.
- **Personalização visual** — tema claro/escuro, transparência da janela e cor de destaque (accent).
- **Inicialização com o Windows** — opção de iniciar automaticamente ao fazer login.
- **Configurações persistentes** — salvas em `%LOCALAPPDATA%\VolumeMixer\settings.json`.

## Requisitos de sistema

Para rodar o `.exe` publicado (single-file self-contained):

| Requisito | Detalhe |
|-----------|---------|
| **Sistema operacional** | Windows 10 ou Windows 11 (64 bits) |
| **Arquitetura** | x64 |
| **.NET Runtime** | **Não é necessário** — o `.exe` é self-contained e embute o runtime .NET 8 |
| **GDI+** | Necessário (usado para ícones). Presente por padrão em qualquer Windows desktop |
| **Dispositivo de áudio** | Necessário um dispositivo de reprodução ativo (o app controla o volume do sistema) |
| **Permissão de escrita em `%TEMP%`** | O single-file extrai o runtime para um diretório temporário na primeira execução |

> **Importante:** o app usa APIs de áudio do Windows (WASAPI/CoreAudio), disponíveis desde o Windows Vista. Não requer privilégios de administrador.

## Como instalar

1. Baixe o `VolumeMixer.exe` da página de **Releases**.
2. Coloque o arquivo em um diretório de sua preferência (ex: `C:\Program Files\VolumeMixer\`).
3. Execute o `VolumeMixer.exe`. O ícone aparecerá na bandeja do sistema.
4. (Opcional) No menu da bandeja → **Configurações** → marque **Iniciar com Windows** para iniciar automaticamente.

> **Nota:** se você mover o `.exe` de lugar depois de ativar "Iniciar com Windows", reative a opção para atualizar o caminho no registro.

## Como compilar (para desenvolvedores)

Pré-requisitos:
- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) ou superior

```powershell
# Restaurar e compilar
dotnet build VolumeMixer/VolumeMixer.csproj -c Debug

# Rodar os testes
dotnet test tests/VolumeMixer.Tests/VolumeMixer.Tests.csproj -c Debug

# Publicar o .exe single-file self-contained
dotnet publish VolumeMixer/VolumeMixer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "VolumeMixer\bin\publish"
```

O `.exe` publicado fica em `VolumeMixer\bin\publish\VolumeMixer.exe`.

## Estrutura do projeto

```
VolumeMixer/
├── Audio/                  # Interop COM CoreAudio (WASAPI) + dispatcher MTA
│   ├── AudioController.cs  # Controlador principal (volume mestre + sessões)
│   ├── ComDispatcher.cs    # Thread MTA dedicada para objetos COM
│   ├── CoreAudioInterop.cs # Declarações COM (IMMDevice, IAudioSessionManager2, etc.)
│   └── ...
├── Infrastructure/         # Bandeja, registro de inicialização, logging
├── Models/                 # AppSettings, AppVolume
├── ViewModels/             # MVVM
├── Views/                  # XAML (PopupWindow, temas, estilos)
└── App.xaml.cs             # Ponto de entrada
```

## Solução de problemas

### O app fecha sozinho após algum tempo

O app registra logs em `%LOCALAPPDATA%\VolumeMixer\logs\app-YYYY-MM-DD.log`. Se o app fechar sem aviso:

1. Verifique o log mais recente em `%LOCALAPPDATA%\VolumeMixer\logs\`.
2. Procure por linhas com `[ERRO]` ou `exceção não capturada`.

Causas comuns:
- **Versão antiga do `.exe`** — versões anteriores tinham um bug em que uma exceção na thread de áudio (MTA) derrubava o processo silenciosamente. **Use sempre a versão mais recente do Release.**
- **Dispositivo de áudio removido/alterado** — se o dispositivo de áudio for removido ou trocado, o app tenta se recuperar automaticamente. Se o problema persistir, reinicie o app.

### O app não lista nenhum aplicativo

- Verifique se há algum aplicativo reproduzindo áudio no momento.
- Alguns aplicativos (ex: navegadores com abas em silêncio) podem não aparecer até reproduzirem som.

### O volume de um aplicativo não muda

- Alguns aplicativos controlam o próprio volume e podem sobrescrever o ajuste do sistema.
- Reinicie o aplicativo de áudio se o controle não responder.

## Licença

Este projeto é de uso pessoal. Sinta-se livre para usar e modificar.
