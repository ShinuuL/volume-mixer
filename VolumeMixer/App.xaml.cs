using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using VolumeMixer.Audio;
using VolumeMixer.Infrastructure;
using VolumeMixer.ViewModels;
using VolumeMixer.Views;

namespace VolumeMixer;

public partial class App : Application
{
    private TrayService? _tray;
    private MainViewModel? _viewModel;
    private UiWatchdog? _watchdog;
    private readonly AppLog _log = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ferramenta de build: regenera os ícones versionados a partir do IconArt.
        if (e.Args is ["--export-icons", var repoRoot])
        {
            IconArt.ExportAll(repoRoot);
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        SessionEnding += (_, _) => { _log.Info("sessão do Windows encerrando"); Cleanup(); };

        // Em sessão remota (RDP/RustDesk/AnyDesk) a janela com transparência
        // renderizada por GPU falha com Win32Exception 1816 ("não há cota
        // suficiente"). Renderização por software evita o problema.
        if (SystemParameters.IsRemoteSession)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            _log.Info("sessão remota detectada: renderização por software");
        }

        // Diagnóstico de crash nativo (SEH) + heartbeat. O app morre em silêncio
        // quando uma exceção nativa derruba o processo sem disparar os handlers
        // gerenciados abaixo — este helper captura o crash e mede a vida do app.
        CrashDiagnostics.Install(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VolumeMixer", "logs"));

        // Captura exceções não-capturadas em threads de background (ex: MTA,
        // Task) que, sem tratamento, derrubariam o processo em silêncio.
        // Loga para diagnóstico em vez de deixar o app fechar sem rastro.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log.Error("exceção não capturada (AppDomain)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("exceção não observada em Task", args.Exception);
            args.SetObserved();
        };

        // FirstChanceException captura TODAS as exceções gerenciadas (mesmo as
        // tratadas) para revelar exceções repetidas antes de um crash nativo.
        // Logado como INFO (não ERRO) para não poluir; pode gerar muitos logs de
        // propósito durante o diagnóstico.
        if (_log.Verbose)
            AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
                _log.Debug($"exceção (first-chance): {args.Exception.GetType().Name}: {args.Exception.Message}");

        // ProcessExit: registra quando o processo está saindo, distinguindo um
        // crash (sem este log) de uma saída normal (menu / Shutdown).
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            _log.Info("processo saindo (ProcessExit)");

        try
        {
            _log.Info("iniciando: criando AudioController");
            var audio = new AudioController();
            _log.Info("iniciando: criando MainViewModel");
            _viewModel = new MainViewModel(audio);
            _log.Info("iniciando: aplicando settings");
            _viewModel.Settings.Apply();
            _log.Info("iniciando: criando TrayService");
            var startup = new StartupRegistry();
            _tray = new TrayService(_viewModel, () => new PopupWindow { DataContext = _viewModel }, startup);
            _tray.ClosingRequested += OnClosingRequested;
            // Tira o ícone da bandeja antes de reiniciar (Environment.Exit não
            // roda o Dispose e deixaria um ícone "fantasma").
            _watchdog = new UiWatchdog(() => UiWatchdog.RestartProcess(() => _tray?.HideIcon()));
            _log.Info(e.Args.Contains("--restarted") ? "aplicativo reiniciado pelo watchdog" : "aplicativo iniciado");
        }
        catch (Exception ex)
        {
            _log.Error("falha ao iniciar", ex);
            System.Windows.MessageBox.Show("Falha ao iniciar o Volume Mixer. Veja os logs em %LOCALAPPDATA%\\VolumeMixer\\logs.",
                "Volume Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log.Info($"aplicativo encerrando (ExitCode={e.ApplicationExitCode})");
        base.OnExit(e);
    }

    private void OnClosingRequested(object? sender, EventArgs e)
    {
        _log.Info("encerrando pelo menu");
        Cleanup();
        Shutdown();
    }

    private void Cleanup()
    {
        _watchdog?.Dispose();
        _watchdog = null;
        try { _viewModel?.Dispose(); } catch (Exception ex) { _log.Error("falha ao liberar áudio", ex); }
        try { _tray?.Dispose(); } catch (Exception ex) { _log.Error("falha ao remover ícone da bandeja", ex); }
        _viewModel = null;
        _tray = null;
    }

    private void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (e.Exception is System.ComponentModel.Win32Exception { NativeErrorCode: 1816 })
        {
            // Rajada de 1816 na HwndTarget: loga uma vez, fecha o popup (será
            // recriado no próximo clique) e troca para renderização por software.
            if (System.Windows.Media.RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly)
            {
                _log.Error("falha de renderização da janela (1816); usando renderização por software", e.Exception);
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            _tray?.ClosePopup();
            return;
        }
        _log.Error("exceção não tratada na UI", e.Exception);
    }
}

