using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Gryphmark;

/// <summary>Narrow windows and zoom: the formatting buttons that do not fit go into a ⋯ menu instead of
/// sliding under the view switches, and the status bar has a zoom bar (−, slider, +, percentage).</summary>
public partial class MainWindow
{
    private const double ZoomMin = 0.5, ZoomMax = 3.0;
    private bool _zoomSync;

    private void InitFit()
    {
        ToolsHost.SizeChanged += (_, _) => Dispatcher.BeginInvoke(FitToolbar);
        ToolsMore.Click += (_, _) => ShowToolsMore();

        ZoomOutBtn.Click += (_, _) => StepZoom(-1);
        ZoomInBtn.Click += (_, _) => StepZoom(+1);
        ZoomPct.Click += (_, _) => SetZoom(1);
        ZoomSlider.Minimum = ZoomMin * 100; ZoomSlider.Maximum = ZoomMax * 100;
        ZoomSlider.ValueChanged += (_, e) => { if (!_zoomSync) SetZoom(Math.Round(e.NewValue / 10) / 10); };
        ZoomSlider.PreviewMouseWheel += (_, e) => { StepZoom(e.Delta > 0 ? 1 : -1); e.Handled = true; };
        // Ctrl + mouse wheel: the page zooms by itself; elsewhere in the window (panels, bars) it is done here
        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || Web.IsMouseOver) return;
            StepZoom(e.Delta > 0 ? 1 : -1); e.Handled = true;
        };
        Web.ZoomFactorChanged += (_, _) => SyncZoom();
        LocalizeFit();
        SyncZoom();
    }

    private void LocalizeFit()
    {
        ToolsMore.ToolTip = Strings.Get("MoreTools");
        ZoomOutBtn.ToolTip = Strings.Plain("ZoomOut") + "   Ctrl+-";
        ZoomInBtn.ToolTip = Strings.Plain("ZoomIn") + "   Ctrl++";
        ZoomPct.ToolTip = Strings.Get("ZoomPctTip");
        ZoomSlider.ToolTip = Strings.Get("ZoomSliderTip");
    }

    private void StepZoom(int dir) => SetZoom(Math.Round(Web.ZoomFactor * 10 + dir) / 10);

    private void SetZoom(double z)
    {
        z = Math.Clamp(z, ZoomMin, ZoomMax);
        if (Math.Abs(Web.ZoomFactor - z) > 0.001) Web.ZoomFactor = z;
        SyncZoom();
    }

    private void SyncZoom()
    {
        _zoomSync = true;
        double z = Web.ZoomFactor;
        ZoomSlider.Value = z * 100;
        ZoomPct.Content = $"{Math.Round(z * 100)} %";
        _zoomSync = false;
    }

    // ------------------------------------------------------------------ toolbar overflow
    /// <summary>Shows as many formatting buttons as fit; the rest go into the ⋯ menu.</summary>
    private void FitToolbar()
    {
        double avail = ToolsHost.ActualWidth;
        if (avail <= 0) return;
        var kids = Tools.Children.OfType<FrameworkElement>().ToList();
        foreach (var k in kids) k.Visibility = Visibility.Visible;
        double Width(FrameworkElement k) { k.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return k.DesiredSize.Width; }
        double total = kids.Sum(Width);
        if (total <= avail) { ToolsMore.Visibility = Visibility.Collapsed; return; }
        ToolsMore.Visibility = Visibility.Visible;
        double room = avail - 36, used = 0;
        bool hiding = false;
        foreach (var k in kids)
        {
            used += Width(k);
            if (used > room) hiding = true;
            if (hiding) k.Visibility = Visibility.Collapsed;
        }
        // no separator left dangling at the end
        var last = kids.LastOrDefault(k => k.Visibility == Visibility.Visible);
        if (last is Border) last.Visibility = Visibility.Collapsed;
    }

    internal void FitToolbarForTest() => FitToolbar();

    private void ShowToolsMore()
    {
        var menu = new ContextMenu { PlacementTarget = ToolsMore, Placement = PlacementMode.Bottom };
        bool pendingSep = false;
        foreach (var k in Tools.Children.OfType<FrameworkElement>().Where(k => k.Visibility != Visibility.Visible))
        {
            if (k is Border) { pendingSep = menu.Items.Count > 0; continue; }
            if (k is not Button b) continue;
            if (pendingSep) { menu.Items.Add(new Separator()); pendingSep = false; }
            var icon = new TextBlock { Text = b.Content as string ?? "", FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center };
            var mi = new MenuItem { Header = b.ToolTip as string ?? "", Icon = icon, IsEnabled = Tools.IsEnabled };
            mi.Click += (_, _) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));
            menu.Items.Add(mi);
        }
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }
}
