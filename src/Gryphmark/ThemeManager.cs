using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Gryphmark;

/// <summary>Swaps the chrome colours (menu, popups, status bar, title bar) between GitHub light and dark.</summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["Gm.Chrome"] = ("#F5F5F7", "#1C1C1E"),
        ["Gm.ChromeBorder"] = ("#E3E3E8", "#2C2C2E"),
        ["Gm.Popup"] = ("#FCFCFD", "#2A2A2D"),
        ["Gm.PopupBorder"] = ("#E0E0E5", "#3A3A3C"),
        ["Gm.Text"] = ("#1D1D1F", "#F5F5F7"),
        ["Gm.TextMuted"] = ("#86868B", "#98989D"),
        ["Gm.Hover"] = ("#EBEBF0", "#3A3A3D"),
        ["Gm.Pressed"] = ("#E1E1E6", "#454548"),
        ["Gm.Accent"] = ("#1D1D1F", "#F5F5F7"),
        ["Gm.Separator"] = ("#E8E8ED", "#38383A"),
    };

    public static void Apply(Window window, bool dark)
    {
        var res = Application.Current.Resources;
        foreach (var (key, (light, darkHex)) in Palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(dark ? darkHex : light);
            res[key] = new SolidColorBrush(c);
        }
        ApplyTitleBar(window, dark);
    }

    // Windows 11: dark title bar + caption colour matching the menu bar, so the header reads as one piece.
    private static void ApplyTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));                       // DWMWA_USE_IMMERSIVE_DARK_MODE
        var c = (Color)ColorConverter.ConvertFromString(dark ? Palette["Gm.Chrome"].Dark : Palette["Gm.Chrome"].Light);
        int colorref = c.R | (c.G << 8) | (c.B << 16);
        DwmSetWindowAttribute(hwnd, 35, ref colorref, sizeof(int));                // DWMWA_CAPTION_COLOR (Win11)
        var t = (Color)ColorConverter.ConvertFromString(dark ? Palette["Gm.Text"].Dark : Palette["Gm.Text"].Light);
        int textref = t.R | (t.G << 8) | (t.B << 16);
        DwmSetWindowAttribute(hwnd, 36, ref textref, sizeof(int));                 // DWMWA_TEXT_COLOR (Win11)
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
