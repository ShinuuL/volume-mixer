using System.Diagnostics;
using System.Windows.Threading;

namespace VolumeMixer.Infrastructure;

/// <summary>
/// Detecta a UI "morta": quando a fila de mensagens do Windows lota
/// (Win32Exception 1816, ERROR_NOT_ENOUGH_QUOTA), o Dispatcher do WPF para de
/// processar para sempre — timers param, e o clique na bandeja não abre mais o
/// popup, embora o processo continue vivo. Não há recuperação dentro do processo,
/// então o watchdog reinicia o app.
///
/// Um DispatcherTimer marca "batimentos"; uma thread de fundo confere. Se a UI
/// ficar <see cref="HangThreshold"/> sem bater, dispara <c>onHang</c>.
/// </summary>
internal sealed class UiWatchdog : IDisposable
{
    private static readonly TimeSpan BeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan HangThreshold = TimeSpan.FromSeconds(45);

    private readonly DispatcherTimer _beat;
    private readonly Thread _monitor;
    private readonly Action _onHang;
    private readonly CancellationTokenSource _cts = new();
    private long _lastBeat = Environment.TickCount64;

    public UiWatchdog(Action onHang)
    {
        _onHang = onHang;
        _beat = new DispatcherTimer(DispatcherPriority.Background) { Interval = BeatInterval };
        _beat.Tick += (_, _) => Interlocked.Exchange(ref _lastBeat, Environment.TickCount64);
        _beat.Start();
        _monitor = new Thread(Monitor) { IsBackground = true, Name = "UI-Watchdog" };
        _monitor.Start();
    }

    private void Monitor()
    {
        var lastLoop = Environment.TickCount64;
        while (!_cts.Token.WaitHandle.WaitOne(CheckInterval))
        {
            var now = Environment.TickCount64;
            // O relógio corre durante suspensão/hibernação: se o próprio monitor
            // ficou parado bem mais que o intervalo, a máquina dormiu — não é travamento.
            if (now - lastLoop > CheckInterval.TotalMilliseconds * 4)
                Interlocked.Exchange(ref _lastBeat, now);
            lastLoop = now;

            if (now - Interlocked.Read(ref _lastBeat) > HangThreshold.TotalMilliseconds)
            {
                try { _onHang(); } catch { /* último recurso: nada a fazer */ }
                return;
            }
        }
    }

    /// <summary>Reinicia o executável atual e encerra este processo.</summary>
    public static void RestartProcess(Action? beforeExit = null)
    {
        AppLog.Instance.Error($"UI sem resposta há mais de {HangThreshold.TotalSeconds:0}s; reiniciando o app");
        try { beforeExit?.Invoke(); } catch { /* best effort */ }
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null) Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = false });
        }
        catch (Exception ex) { AppLog.Instance.Error("falha ao reiniciar", ex); }
        Environment.Exit(3);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _beat.Stop();
    }
}
