using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace VolumeMixer.Models;

/// <summary>Configurações visuais do app com persistência em JSON.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VolumeMixer");
    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    // ── Defaults ──
    private string _theme = "dark";
    private double _transparency = 0.85;
    private string _accentColor = "#0078D7";
    private bool _useCustomAccent;

    // Construtor usado pelo System.Text.Json na desserialização: popula os
    // campos sem disparar efeitos colaterais (ApplyTheme/ApplyAccent), que
    // só devem rodar na UI thread após o app estar pronto.
    [System.Text.Json.Serialization.JsonConstructor]
    public AppSettings(string theme, double transparency, string accentColor, bool useCustomAccent)
    {
        _theme = string.IsNullOrWhiteSpace(theme) ? "dark" : theme;
        _transparency = Math.Clamp(transparency, 0.5, 1.0);
        _accentColor = string.IsNullOrWhiteSpace(accentColor) ? "#0078D7" : accentColor;
        _useCustomAccent = useCustomAccent;
    }

    public AppSettings() { }

    public string Theme
    {
        get => _theme;
        set { if (RaiseAndSet(ref _theme, value)) ApplyTheme(); }
    }

    public double Transparency
    {
        get => _transparency;
        set => RaiseAndSet(ref _transparency, Math.Clamp(value, 0.5, 1.0));
    }

    public string AccentColor
    {
        get => _accentColor;
        set { if (RaiseAndSet(ref _accentColor, value)) ApplyAccent(); }
    }

    public bool UseCustomAccent
    {
        get => _useCustomAccent;
        set { if (RaiseAndSet(ref _useCustomAccent, value)) ApplyAccent(); }
    }

    // ── Events for XAML binding ──
    public event PropertyChangedEventHandler? SettingsChanged;

    // ── Persistence ──

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch { /* corrupted file → use defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best effort */ }
    }

    // ── Theme application ──

    /// <summary>Aplica tema + accent aos recursos globais do app. Chamado no startup.</summary>
    public void Apply()
    {
        ApplyTheme();
        ApplyAccent();
    }

    private void ApplyTheme()
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var dictPath = _theme == "dark"
            ? "pack://application:,,,/Views/Themes/Dark.xaml"
            : "pack://application:,,,/Views/Themes/Light.xaml";

        var newDict = new System.Windows.ResourceDictionary { Source = new Uri(dictPath) };

        // Remove old theme dictionaries
        var toRemove = app.Resources.MergedDictionaries
            .Where(d => d.Source?.ToString().Contains("/Themes/") == true)
            .ToList();
        foreach (var old in toRemove)
            app.Resources.MergedDictionaries.Remove(old);

        app.Resources.MergedDictionaries.Add(newDict);
        ApplyAccent();
        SettingsChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
    }

    private void ApplyAccent()
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var color = _useCustomAccent ? _accentColor : GetDefaultAccent();
        var parsed = System.Windows.Media.ColorConverter.ConvertFromString(color) as System.Windows.Media.Color?;
        if (parsed is null)
        {
            // Cor inválida no settings.json → usa o default do tema para nunca
            // deixar o recurso AccentBrush ausente.
            parsed = System.Windows.Media.ColorConverter.ConvertFromString(GetDefaultAccent()) as System.Windows.Media.Color?;
            if (parsed is null) return;
        }

        // Substitui o recurso AccentBrush no nível do app (não muta o brush
        // congelado do dicionário de tema). DynamicResource re-resolve e atualiza.
        app.Resources["AccentBrush"] = new System.Windows.Media.SolidColorBrush(parsed.Value);
        SettingsChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccentColor)));
    }

    private string GetDefaultAccent() => _theme == "dark" ? "#60CDFF" : "#0078D7";

    // ── INotifyPropertyChanged ──

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool RaiseAndSet<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
