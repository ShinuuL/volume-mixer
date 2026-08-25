using System.Windows.Forms;
using VolumeMixer.ViewModels;

namespace VolumeMixer.Infrastructure;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly Func<Views.PopupWindow> _popupFactory;
    private readonly StartupRegistry _startup;
    private readonly ToolStripMenuItem _startupItem;
    private Views.PopupWindow? _popup;

    public event EventHandler? ClosingRequested;

    public TrayService(MainViewModel viewModel, Func<Views.PopupWindow> popupFactory, StartupRegistry startup)
    {
        _viewModel = viewModel;
        _popupFactory = popupFactory;
        _startup = startup;

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
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) TogglePopup();
    }

    public void TogglePopup()
    {
        if (_popup is { IsLoaded: true })
        {
            _popup.Close();
            _popup = null;
            return;
        }
        _popup = _popupFactory();
        _popup.Show();
        _popup.Activate();
    }

    public void Dispose()
    {
        _popup?.Close();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
