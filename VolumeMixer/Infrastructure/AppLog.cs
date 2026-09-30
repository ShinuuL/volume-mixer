using System.IO;

namespace VolumeMixer.Infrastructure;

public sealed class AppLog
{
    private readonly string _directory;
    private static readonly object Gate = new();
    private const int RetentionDays = 7;
    private bool _cleaned;

    /// <summary>Singleton compartilhado para logging de qualquer thread (ex: MTA).</summary>
    public static AppLog Instance { get; } = new();

    /// <summary>Log detalhado (cada poll/sessão/first-chance). Desligado por padrão:
    /// chegava a 7 MB/dia. Liga com VOLUMEMIXER_DEBUG=1 ou criando o arquivo
    /// <c>logs\verbose</c>.</summary>
    public bool Verbose { get; set; }

    public AppLog(string? directory = null)
    {
        _directory = directory
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "VolumeMixer", "logs");
        Verbose = Environment.GetEnvironmentVariable("VOLUMEMIXER_DEBUG") == "1"
                  || File.Exists(Path.Combine(_directory, "verbose"));
    }

    public void Debug(string message) { if (Verbose) Write("DBG ", message, ex: null); }
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
            lock (Gate)
            {
                if (!_cleaned) { _cleaned = true; DeleteOldLogs(); }
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch { /* logging nunca derruba o app */ }
    }

    private void DeleteOldLogs()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var old in Directory.EnumerateFiles(_directory, "app-*.log"))
        {
            try { if (File.GetLastWriteTime(old) < cutoff) File.Delete(old); }
            catch { /* arquivo em uso: tenta na próxima execução */ }
        }
    }
}
