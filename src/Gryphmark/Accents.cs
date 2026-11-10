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
    /// Makes the chosen colour the icon Windows shows. Explorer caches icons by file path, so rewriting one
    /// file is not enough: each colour gets its own file ({app}\theme\gryphmark_&lt;key&gt;.ico) and the app points
    /// at it the shortcuts that open Gryphmark (desktop, Start menu, pinned taskbar) and the registry entries the
    /// installer wrote (.md file type, folder menu). Only shortcuts whose icon already comes from {app} are
    /// changed — one made with an icon of its own (for example a shortcut to a folder) is left alone.
    /// Only for an installed copy (next to the uninstaller); runs off the UI thread and never throws.
    /// </summary>
    public static void ApplyToSystem(string key)
    {
        var dir = AppContext.BaseDirectory.TrimEnd('\\');
        if (!File.Exists(Path.Combine(dir, "unins000.exe"))) return;
        key = Get(key).Key;
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
                var themeDir = Path.Combine(dir, "theme");
                Directory.CreateDirectory(themeDir);
                var icon = Path.Combine(themeDir, $"gryphmark_{key}.ico");
                if (!File.Exists(icon) || !File.ReadAllBytes(icon).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(icon, bytes);
                try { File.WriteAllBytes(Path.Combine(dir, "Gryphmark.ico"), bytes); } catch { }   // uninstaller icon
                bool changed = UpdateShortcuts(dir, icon) | UpdateRegistry(dir, icon);
                foreach (var old in Directory.GetFiles(themeDir, "gryphmark_*.ico"))
                    if (!old.Equals(icon, StringComparison.OrdinalIgnoreCase)) try { File.Delete(old); } catch { }
                if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED
            }
            catch { }
        });
    }

    private static bool OurIcon(string? location, string dir) =>
        string.IsNullOrEmpty(location) || location.StartsWith(",") ||
        Environment.ExpandEnvironmentVariables(location).StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool UpdateShortcuts(string dir, string icon)
    {
        bool changed = false;
        var exe = Path.Combine(dir, "Gryphmark.exe");
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        };
        Type? t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return false;
        dynamic shell = Activator.CreateInstance(t)!;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList(); } catch { continue; }
            foreach (var path in links)
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(path);
                    string target = lnk.TargetPath ?? "";
                    if (!target.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
                    string loc = lnk.IconLocation ?? "";
                    string locPath = loc.Contains(',') ? loc[..loc.LastIndexOf(',')] : loc;
                    if (!OurIcon(locPath, dir) && !locPath.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (locPath.Equals(icon, StringComparison.OrdinalIgnoreCase)) continue;
                    lnk.IconLocation = icon + ",0";
                    lnk.Save();
                    changed = true;
                }
                catch { }
            }
        }
        return changed;
    }

    private static bool UpdateRegistry(string dir, string icon)
    {
        bool changed = false;
        foreach (var (sub, name) in new[]
        {
            (@"Software\Classes\Gryphmark.md\DefaultIcon", ""),
            (@"Software\Classes\Directory\shell\Gryphmark", "Icon"),
            (@"Software\Classes\Directory\Background\shell\Gryphmark", "Icon"),
        })
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sub, writable: true);
                if (k?.GetValue(name) is not string v || !OurIcon(v, dir) || v.Equals(icon, StringComparison.OrdinalIgnoreCase)) continue;
                k.SetValue(name, icon);
                changed = true;
            }
            catch { }
        }
        return changed;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
