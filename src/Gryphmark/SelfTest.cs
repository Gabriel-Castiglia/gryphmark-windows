using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gryphmark;

/// <summary>Gryphmark.exe --render-chrome &lt;dir&gt;: renders the menu bar and every top-level popup to PNG (light and dark).</summary>
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
            Save((FrameworkElement)w.FindName("ToolBarHost"), Path.Combine(dir, $"toolbar_{(dark ? "dark" : "light")}.png"));
            // narrow window: the buttons that do not fit go into the overflow menu instead of overlapping
            var bar = (FrameworkElement)w.FindName("ToolBarHost");
            double keepW = bar.Width;
            foreach (var narrow in new[] { 760.0, 560.0 })
            {
                bar.Width = narrow; bar.UpdateLayout(); w.FitToolbarForTest(); bar.UpdateLayout();
                Save(bar, Path.Combine(dir, $"toolbar_{narrow}_{(dark ? "dark" : "light")}.png"));
            }
            bar.Width = keepW; bar.UpdateLayout(); w.FitToolbarForTest();
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
            // About window and the mode-button tooltips, in both languages
            var keepLang = Strings.Lang;
            foreach (var lang in new[] { "en", "es" })
            {
                Strings.Lang = lang;
                string sfx = $"{lang}_{(dark ? "dark" : "light")}";
                var about = new AboutDialog(w, "1.5.0");
                Layout(about.Card);
                Save(about.Card, Path.Combine(dir, $"about_{sfx}.png"), dark);
                about.Close();
                var theme = new ThemeDialog(w, "auto", Accents.Default, _ => { }, _ => { });
                Layout(theme.Card);
                Save(theme.Card, Path.Combine(dir, $"theme_{sfx}.png"), dark);
                theme.Close();
                var tips = new StackPanel();
                foreach (var (n, t, g) in new[] { ("ModeVisual", "TipVisual", "Ctrl+1"), ("ModeSource", "TipSource", "Ctrl+2"), ("ModePreview", "TipPreview", "Ctrl+3") })
                {
                    // same look as the ToolTip template in Theme.xaml (a real ToolTip cannot be parented)
                    var tt = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(6, 4, 6, 6),
                                          BorderThickness = new Thickness(1), Child = (UIElement)MainWindow.SegTip(n, t, g),
                                          HorizontalAlignment = HorizontalAlignment.Left };
                    tt.SetResourceReference(Border.BackgroundProperty, "Gm.Popup");
                    tt.SetResourceReference(Border.BorderBrushProperty, "Gm.PopupBorder");
                    tt.SetResourceReference(TextElement.ForegroundProperty, "Gm.Text");
                    tt.SetResourceReference(TextElement.FontFamilyProperty, "Gm.Font");
                    TextElement.SetFontSize(tt, 12);
                    tips.Children.Add(tt);
                }
                Layout(tips);
                Save(tips, Path.Combine(dir, $"tooltips_{sfx}.png"), dark);

                // Files panel: empty, then a small folder with one file selected
                var pane = (FrameworkElement)w.FindName("FilesPane");
                w.TestFolder(null);
                w.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                Save(pane, Path.Combine(dir, $"files_empty_{sfx}.png"));
                var demo = DemoFolder();
                var second = Path.Combine(Path.GetDirectoryName(demo)!, "Website");
                foreach (var f in new[] { "index.md", "docs/setup.md" }) { var p = Path.Combine(second, f); Directory.CreateDirectory(Path.GetDirectoryName(p)!); if (!File.Exists(p)) File.WriteAllText(p, "# x"); }
                w.TestFolder(Path.Combine(demo, "Notes", "Projects", "Plan.md"), demo, second);
                w.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                w.UpdateLayout();
                Save(pane, Path.Combine(dir, $"files_tree_{sfx}.png"));
                w.TestFolder(null);
            }
            Strings.Lang = keepLang;
        }
    }

    /// <summary>A throw-away folder for the Files panel pictures (hidden and tool folders must not show).</summary>
    private static string DemoFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "gm_files_demo", "My notes");
        foreach (var f in new[] { "README.md", "Changelog.md", "Notes/Ideas.md", "Notes/Projects/Plan.md", "Notes/Projects/Budget.md",
                                  "Archive/2025.md", "node_modules/skip.md", "image.png" })
        {
            var p = Path.Combine(root, f);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            if (!File.Exists(p)) File.WriteAllText(p, "# " + Path.GetFileNameWithoutExtension(p));
        }
        var hidden = Directory.CreateDirectory(Path.Combine(root, ".obsidian"));
        hidden.Attributes |= FileAttributes.Hidden;
        return root;
    }

    private static void Layout(FrameworkElement el)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        el.Arrange(new Rect(el.DesiredSize));
        el.UpdateLayout();
    }

    private static void Save(FrameworkElement el, string path, bool? darkBg = null)
    {
        const double scale = 1.5;
        int wpx = (int)Math.Ceiling(Math.Max(el.ActualWidth, el.DesiredSize.Width) * scale);
        int hpx = (int)Math.Ceiling(Math.Max(el.ActualHeight, el.DesiredSize.Height) * scale);
        if (wpx <= 0 || hpx <= 0) return;
        var rtb = new RenderTargetBitmap(wpx, hpx, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var r = new Rect(0, 0, wpx / scale, hpx / scale);
            // page colour behind popups so the shadow is visible
            if (darkBg.HasValue) dc.DrawRectangle(new SolidColorBrush(darkBg.Value ? Color.FromRgb(13, 17, 23) : Colors.White), null, r);
            // VisualBrush ignores the element's offset inside the window
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, r);
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
