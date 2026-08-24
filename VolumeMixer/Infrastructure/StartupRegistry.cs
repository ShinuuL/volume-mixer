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
