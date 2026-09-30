using System.Collections.ObjectModel;
using VolumeMixer.Audio;
using VolumeMixer.Infrastructure;
using VolumeMixer.Models;

namespace VolumeMixer.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioController _audio;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private bool _disposed;

    public MasterViewModel Master { get; }
    public ObservableCollection<AppVolumeViewModel> Apps { get; } = new();
    public AppSettings Settings { get; } = AppSettings.Load();

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

        // Polling de segurança: os callbacks COM de sessão do Windows podem não
        // disparar em algumas configurações; um timer garante que a lista se
        // mantenha atualizada mesmo assim. Só roda com o popup visível
        // (StartPolling/StopPolling) — na bandeja não há o que atualizar.
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) => RefreshSessions();
    }

    /// <summary>Popup abriu: atualiza já e passa a reconciliar periodicamente.</summary>
    public void StartPolling()
    {
        if (_disposed) return;
        RefreshSessions();
        Master.Refresh();
        _refreshTimer.Start();
    }

    public void StopPolling() => _refreshTimer.Stop();

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

        AppLog.Instance.Debug($"UI: {Apps.Count} apps na lista");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
        _audio.Dispose();
    }
}
