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
    private readonly AppLog _log = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;

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

        try
        {
            var audio = new AudioController();
            _viewModel = new MainViewModel(audio);
            _viewModel.Settings.Apply();
            var startup = new StartupRegistry();
            _tray = new TrayService(_viewModel, () => new PopupWindow { DataContext = _viewModel }, startup);
            _tray.ClosingRequested += OnClosingRequested;
            _log.Info("aplicativo iniciado");
        }
        catch (Exception ex)
        {
            _log.Error("falha ao iniciar", ex);
            System.Windows.MessageBox.Show("Falha ao iniciar o Volume Mixer. Veja os logs em %LOCALAPPDATA%\\VolumeMixer\\logs.",
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
        e.Handled = true;
    }
}

