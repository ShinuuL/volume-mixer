using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VolumeMixer.ViewModels;

namespace VolumeMixer.Views;

public partial class PopupWindow : Window
{
    private bool _positioned;

    public PopupWindow() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void OnDeactivated(object sender, EventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Altura maxima = 60% da area de trabalho (spec 5)
        MaxHeight = SystemParameters.WorkArea.Height * 0.6;
    }

    private void OnContentRenderedFirstTime(object sender, EventArgs e)
    {
        if (_positioned) return;
        _positioned = true;
        PositionNearTray();
    }

    /// <summary>Canto inferior direito da area de trabalho, 8px de margem, ciente de DPI.</summary>
    private void PositionNearTray()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
        if (area is null) return;
        var dpi = (PresentationSource.FromVisual(this) as HwndSource)
                  ?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var widthPx = ActualWidth * dpi;
        var heightPx = ActualHeight * dpi;
        Left = (area.Value.Right - widthPx - 8) / dpi;
        Top = (area.Value.Bottom - heightPx - 8) / dpi;
    }

    // ===== Caixa de digitacao (compartilhada por master e apps) =====

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

    private void OnVolumeBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        CommitAndReleaseFocus(sender);
        e.Handled = true;
    }

    private void OnVolumeBoxLostFocus(object sender, RoutedEventArgs e)
        => Commit(sender);

    private static void CommitAndReleaseFocus(object sender)
    {
        Commit(sender);
        if (sender is System.Windows.Controls.TextBox box)
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private static void Commit(object sender)
    {
        var vm = (sender as FrameworkElement)?.DataContext;
        switch (vm)
        {
            case MasterViewModel m: m.CommitText(); break;
            case AppVolumeViewModel a: a.CommitText(); break;
        }
    }
}
