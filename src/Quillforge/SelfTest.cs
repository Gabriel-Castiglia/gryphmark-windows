using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Quillforge;

/// <summary>Quillforge.exe --render-chrome &lt;dir&gt;: renders the menu bar and every top-level popup to PNG (light and dark).</summary>
public static class SelfTest
{
    public static void RenderChrome(MainWindow w, string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var dark in new[] { false, true })
        {
            ThemeManager.Apply(w, dark);
            w.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            var menu = (Menu)w.FindName("MainMenu");
            Save(menu, Path.Combine(dir, $"menubar_{(dark ? "dark" : "light")}.png"));
            int i = 0;
            foreach (MenuItem top in menu.Items)
            {
                top.IsSubmenuOpen = true;
                w.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                top.ApplyTemplate();
                if (top.Template.FindName("PART_Popup", top) is Popup p && p.Child is FrameworkElement child)
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    child.Arrange(new Rect(child.DesiredSize));
                    child.UpdateLayout();
                    Save(child, Path.Combine(dir, $"popup{i}_{(dark ? "dark" : "light")}.png"), dark);
                }
                top.IsSubmenuOpen = false;
                i++;
            }
        }
    }

    private static void Save(FrameworkElement el, string path, bool? darkBg = null)
    {
        const double scale = 1.5;
        int wpx = (int)Math.Ceiling(Math.Max(el.ActualWidth, el.DesiredSize.Width) * scale);
        int hpx = (int)Math.Ceiling(Math.Max(el.ActualHeight, el.DesiredSize.Height) * scale);
        if (wpx <= 0 || hpx <= 0) return;
        var rtb = new RenderTargetBitmap(wpx, hpx, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        if (darkBg.HasValue)
        {
            // page colour behind the popup so the shadow is visible
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(darkBg.Value ? Color.FromRgb(13, 17, 23) : Colors.White), null, new Rect(0, 0, wpx / scale, hpx / scale));
            rtb.Render(dv);
        }
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
