using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gryphmark;

/// <summary>
/// The theme colours: each one is a background for the icon (icons/gryphmark_&lt;key&gt;.ico, made by
/// tools/make_theme_icons.py — keep the keys in sync) and the highlight colour of the app
/// (folder icons, check marks, the selected item) for the light and the dark chrome.
/// </summary>
public static class Accents
{
    public sealed record Accent(string Key, string En, string Es, string Light, string Dark)
    {
        public string Name => Strings.Lang == "es" ? Es : En;
    }

    public const string Default = "purple";

    public static readonly Accent[] All =
    {
        new("purple", "Purple",   "Morado",        "#5B2A86", "#B392E0"),   // Advent purple, the default
        new("sky",    "Sky blue", "Celeste",       "#1A8CD8", "#5CB8F0"),
        new("ember",  "Orange",   "Naranja",       "#D9652F", "#F08A5D"),
        new("red",    "Red",      "Rojo",          "#C8102E", "#F2545B"),
        new("yellow", "Yellow",   "Amarillo",      "#B88A00", "#F5C842"),
        new("green",  "Green",    "Verde",         "#1E8A43", "#4CC27A"),
        new("teal",   "Teal",     "Verde azulado", "#00796B", "#4DB6AC"),
        new("blue",   "Blue",     "Azul",          "#1F5FD1", "#6E9BFF"),
        new("pink",   "Pink",     "Rosa",          "#C2185B", "#F06292"),
        new("black",  "Black",    "Negro",         "#1D1D1F", "#C7C7CC"),
        new("white",  "White",    "Blanco",        "#8E8E93", "#F5F5F7"),
    };

    public static Accent Get(string? key) => All.FirstOrDefault(a => a.Key == key) ?? All[0];

    private static Uri IconUri(string key) => new($"pack://application:,,,/icons/gryphmark_{Get(key).Key}.ico");

    /// <summary>The largest frame of the icon in this colour (for previews and the About window).</summary>
    public static ImageSource? Icon(string key, int size = 256)
    {
        try
        {
            var dec = BitmapDecoder.Create(IconUri(key), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).First();
        }
        catch { return null; }
    }

    /// <summary>The whole .ico (all sizes), for the window/taskbar icon.</summary>
    public static ImageSource? WindowIcon(string key)
    {
        try { return BitmapFrame.Create(IconUri(key), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
        catch { return null; }
    }

    /// <summary>
    /// Makes the chosen colour the icon Windows shows: the installer points the Start-menu and desktop
    /// shortcuts, the .md file type and the folder menu at {app}\Gryphmark.ico, so that file is replaced and
    /// the shell is told to refresh. Only for an installed copy (next to the uninstaller); runs off the UI thread.
    /// Shortcuts that use their own icon file are not touched.
    /// </summary>
    public static void ApplyToSystem(string key)
    {
        var dir = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(dir, "unins000.exe"))) return;
        byte[] bytes;
        try
        {
            using var s = Application.GetResourceStream(IconUri(key)).Stream;
            using var ms = new MemoryStream(); s.CopyTo(ms); bytes = ms.ToArray();
        }
        catch { return; }
        Task.Run(() =>
        {
            try
            {
                var target = Path.Combine(dir, "Gryphmark.ico");
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) return;
                File.WriteAllBytes(target, bytes);
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);          // SHCNE_ASSOCCHANGED
                var ie4 = Path.Combine(Environment.SystemDirectory, "ie4uinit.exe");  // refreshes the icon cache
                if (File.Exists(ie4)) Process.Start(new ProcessStartInfo(ie4, "-show") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(10000);
            }
            catch { }
        });
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
