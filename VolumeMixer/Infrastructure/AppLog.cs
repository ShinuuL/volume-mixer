using System.IO;

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
