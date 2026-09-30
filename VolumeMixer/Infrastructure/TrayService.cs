using System.Windows.Forms;
using VolumeMixer.Models;
using VolumeMixer.ViewModels;

namespace VolumeMixer.Infrastructure;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly Func<Views.PopupWindow> _popupFactory;
    private readonly StartupRegistry _startup;
    private readonly ToolStripMenuItem _startupItem;
    private readonly AppSettings _settings;
    private Views.PopupWindow? _popup;

    public event EventHandler? ClosingRequested;

    public TrayService(MainViewModel viewModel, Func<Views.PopupWindow> popupFactory, StartupRegistry startup)
    {
        _viewModel = viewModel;
        _popupFactory = popupFactory;
        _startup = startup;
        _settings = viewModel.Settings;

        _startupItem = new ToolStripMenuItem("Iniciar com Windows")
        {
            Checked = startup.IsEnabled(),
        };
        _startupItem.Click += (_, _) =>
        {
            if (_startup.IsEnabled()) _startup.Disable(); else _startup.Enable();
            _startupItem.Checked = _startup.IsEnabled();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => TogglePopup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildSettingsMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ClosingRequested?.Invoke(this, EventArgs.Empty));
        menu.Opening += (_, _) => _startupItem.Checked = _startup.IsEnabled();

        _icon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Volume Mixer",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += OnMouseClick;

        // Ícone reflete o mudo do master e acompanha o tema claro/escuro da barra.
        _iconMuted = viewModel.Master.IsMuted;
        viewModel.Master.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MasterViewModel.IsMuted)) UpdateIcon();
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdateIcon(force: true);
    }

    private bool _iconMuted;
    private bool _iconLight;

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General) UpdateIcon();
    }

    /// <summary>Redesenha só quando mudo/tema mudou (cada ícone é um handle GDI).</summary>
    private void UpdateIcon(bool force = false)
    {
        try
        {
            var muted = _viewModel.Master.IsMuted;
            var light = TrayIconFactory.IsLightTaskbar();
            if (!force && muted == _iconMuted && light == _iconLight) return;
            _iconMuted = muted;
            _iconLight = light;
            var old = _icon.Icon;
            _icon.Icon = TrayIconFactory.Create(muted);
            _icon.Text = muted ? "Volume Mixer (mudo)" : "Volume Mixer";
            old?.Dispose();
        }
        catch (Exception ex) { AppLog.Instance.Error("falha ao atualizar ícone da bandeja", ex); }
    }

    private ToolStripMenuItem? _accentMenu;

    private ToolStripMenuItem BuildSettingsMenu()
    {
        var settings = new ToolStripMenuItem("Configurações");

        // Tema
        var theme = new ToolStripMenuItem("Tema");
        var light = new ToolStripMenuItem("Claro") { Checked = _settings.Theme == "light" };
        var dark = new ToolStripMenuItem("Escuro") { Checked = _settings.Theme == "dark" };
        light.Click += (_, _) => SetTheme("light", light, dark);
        dark.Click += (_, _) => SetTheme("dark", light, dark);
        theme.DropDownItems.Add(light);
        theme.DropDownItems.Add(dark);

        // Transparência
        var transparency = new ToolStripMenuItem("Transparência");
        var slider = new ToolStripControlHost(new TrackBar
        {
            Minimum = 50,
            Maximum = 100,
            Value = (int)Math.Round(_settings.Transparency * 100),
            TickStyle = TickStyle.None,
            Width = 120,
        });
        ((TrackBar)slider.Control).ValueChanged += (_, _) =>
        {
            _settings.Transparency = ((TrackBar)slider.Control).Value / 100d;
            _settings.Save();
        };
        transparency.DropDownItems.Add(slider);

        // Cor de destaque
        var accent = new ToolStripMenuItem("Cor de destaque");
        _accentMenu = accent;
        var palette = new (string Name, string Color)[]
        {
            ("Azul", "#0078D7"),
            ("Verde", "#107C10"),
            ("Laranja", "#CA5010"),
            ("Roxo", "#881798"),
            ("Vermelho", "#D13438"),
        };
        foreach (var (name, color) in palette)
        {
            var item = new ToolStripMenuItem(name);
            item.Click += (_, _) =>
            {
                _settings.UseCustomAccent = true;
                _settings.AccentColor = color;
                _settings.Save();
                SyncAccentChecks();
            };
            accent.DropDownItems.Add(item);
        }
        accent.DropDownItems.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("Personalizar...");
        custom.Click += (_, _) => OpenColorPicker();
        accent.DropDownItems.Add(custom);

        settings.DropDownItems.Add(theme);
        settings.DropDownItems.Add(transparency);
        settings.DropDownItems.Add(accent);
        SyncAccentChecks();
        return settings;
    }

    private void SetTheme(string theme, ToolStripMenuItem light, ToolStripMenuItem dark)
    {
        _settings.Theme = theme;
        _settings.Save();
        light.Checked = theme == "light";
        dark.Checked = theme == "dark";
    }

    private void SyncAccentChecks()
    {
        if (_accentMenu is null) return;
        foreach (ToolStripItem item in _accentMenu.DropDownItems)
        {
            if (item is not ToolStripMenuItem menuItem) continue;
            if (menuItem.Text == "Personalizar...") continue;
            menuItem.Checked = _settings.UseCustomAccent &&
                               _settings.AccentColor.Equals(menuItem.Text switch
                               {
                                   "Azul" => "#0078D7",
                                   "Verde" => "#107C10",
                                   "Laranja" => "#CA5010",
                                   "Roxo" => "#881798",
                                   "Vermelho" => "#D13438",
                                   _ => menuItem.Text,
                               }, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OpenColorPicker()
    {
        using var dialog = new ColorDialog
        {
            Color = System.Drawing.ColorTranslator.FromHtml(_settings.AccentColor),
            FullOpen = true,
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _settings.UseCustomAccent = true;
            _settings.AccentColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            _settings.Save();
            SyncAccentChecks();
        }
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) TogglePopup();
    }

    public void TogglePopup()
    {
        if (_popup is { IsLoaded: true })
        {
            ClosePopup();
            return;
        }
        var popup = _popupFactory();
        popup.Closed += (_, _) =>
        {
            _viewModel.StopPolling();
            if (ReferenceEquals(_popup, popup)) _popup = null;
        };
        _popup = popup;
        _viewModel.StartPolling();
        popup.Show();
        popup.Activate();
    }

    /// <summary>Fecha o popup se estiver aberto (também usado para recuperar de
    /// falhas de renderização da janela, ex: Win32Exception 1816).</summary>
    public void ClosePopup()
    {
        var popup = _popup;
        _popup = null;
        try { popup?.Close(); } catch { /* janela já em estado inválido */ }
    }

    /// <summary>Remove o ícone da bandeja; seguro de chamar de outra thread
    /// (Shell_NotifyIcon não depende do loop de mensagens).</summary>
    public void HideIcon()
    {
        try { _icon.Visible = false; } catch { /* best effort */ }
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        ClosePopup();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
