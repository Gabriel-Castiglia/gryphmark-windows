using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Quillforge;

/// <summary>About window in the app's own style: icon, version, author, licence and credits (English/Spanish).</summary>
public sealed class AboutDialog : Window
{
    public const string Credits =
        "TOAST UI Editor · ProseMirror · marked · github-markdown-css ·\n" +
        "Mermaid · KaTeX · gemoji — MIT\n" +
        "highlight.js · jsdiff — BSD-3-Clause\n" +
        ".NET (MIT) · Microsoft Edge WebView2";

    public FrameworkElement Card { get; }
    private readonly string _version;

    public AboutDialog(Window owner, string version)
    {
        _version = version;
        Owner = owner;
        Title = Strings.Plain("About");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var font = (FontFamily)FindResource("Qf.Font");
        TextBlock Text(string s, double size, string brush, FontWeight? w = null, double bottom = 0)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontFamily = font, TextWrapping = TextWrapping.Wrap,
                                    TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, bottom) };
            if (w.HasValue) t.FontWeight = w.Value;
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        var stack = new StackPanel { Width = 440 };
        var icon = new Image { Width = 96, Height = 96, Margin = new Thickness(0, 4, 0, 10), Source = LoadIcon() };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        stack.Children.Add(icon);
        stack.Children.Add(Text("Quillforge", 26, "Qf.Text", FontWeights.SemiBold));
        stack.Children.Add(Text(string.Format(Strings.Get("AboutVersion"), version), 12.5, "Qf.TextMuted", null, 12));
        stack.Children.Add(Text(Strings.Get("AboutTagline"), 13.5, "Qf.Text", null, 14));
        stack.Children.Add(Text(Strings.Get("AboutBy"), 14, "Qf.Text", FontWeights.SemiBold));
        stack.Children.Add(Text("© 2026 Gabriel Castiglia", 12.5, "Qf.TextMuted", null, 16));

        var sep = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 14) };
        sep.SetResourceReference(Border.BackgroundProperty, "Qf.Separator");
        stack.Children.Add(sep);

        var lic = Text(Strings.Get("AboutLicense") + " ", 12.5, "Qf.Text", null, 12);
        var link = new Hyperlink(new Run(Strings.Get("AboutLicenseLink"))) { Cursor = Cursors.Hand };
        link.SetResourceReference(TextElement.ForegroundProperty, "Qf.Text");
        link.Click += (_, _) => OpenLicense();
        lic.Inlines.Add(link);
        stack.Children.Add(lic);

        stack.Children.Add(Text(Strings.Get("AboutCreditsTitle"), 11, "Qf.TextMuted", FontWeights.SemiBold, 4));
        stack.Children.Add(Text(Credits + "\n" + Strings.Get("AboutIcon"), 11, "Qf.TextMuted", null, 8));

        var copy = new Button { Content = Strings.Get("AboutCopy"), Style = (Style)FindResource("Qf.DialogButton") };
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, Style = (Style)FindResource("Qf.DialogButton") };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(Info()); copy.Content = Strings.Get("AboutCopied"); } catch { }
        };
        ok.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(copy); buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(26, 22, 26, 18), Margin = new Thickness(24),
            BorderThickness = new Thickness(1), Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 10, Direction = 270, Opacity = 0.18 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Qf.Popup");
        card.SetResourceReference(Border.BorderBrushProperty, "Qf.PopupBorder");
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Card = card;
        Content = card;
    }

    private string Info() =>
        $"Quillforge {_version} — © 2026 Gabriel Castiglia — GPL-2.0-only\n" +
        $"Windows {Environment.OSVersion.Version} · .NET {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")}\n" +
        $"WebView2 {WebView2Version()}";

    private static string WebView2Version()
    {
        try { return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch { return "?"; }
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            var dec = BitmapDecoder.Create(new Uri("pack://application:,,,/Quillforge.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return dec.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch { return null; }
    }

    private static void OpenLicense()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        var target = File.Exists(local) ? local : "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
