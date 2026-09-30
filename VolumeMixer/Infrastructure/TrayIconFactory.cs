using System.Drawing;
using System.Runtime.InteropServices;

namespace VolumeMixer.Infrastructure;

internal static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private const int SmCxSmIcon = 49;

    /// <summary>Glifo de faders (<see cref="IconArt"/>) no tamanho de ícone pequeno
    /// do DPI atual (16/20/24/32px), na cor do tema da barra de tarefas.</summary>
    public static Icon Create(bool muted = false)
    {
        using var bmp = IconArt.RenderTrayIcon(TraySize(), IsLightTaskbar(), muted);
        var hIcon = bmp.GetHicon();
        try
        {
            // Clone so the returned Icon owns its own GDI handle.
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static int TraySize()
    {
        try { return Math.Clamp(GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForSystem()), 16, 64); }
        catch { return 16; }
    }

    /// <summary>Barra de tarefas clara? (HKCU ...\Themes\Personalize\SystemUsesLightTheme).</summary>
    internal static bool IsLightTaskbar()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }
}
