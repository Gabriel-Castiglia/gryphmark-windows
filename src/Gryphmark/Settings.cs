using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Gryphmark;

public sealed class Settings
{
    public string Lang { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
    public string Theme { get; set; } = "auto";      // light | dark | auto (follows Windows)
    public string Accent { get; set; } = Accents.Default;
    /// <summary>True once the colour was chosen in View → Theme; until then the default colour applies.</summary>
    public bool AccentChosen { get; set; }
    public string Mode { get; set; } = "visual";
    public bool ReadOnly { get; set; }
    public double Zoom { get; set; } = 1.0;
    public List<string> Recent { get; set; } = new();
    /// <summary>1.4 and earlier: the single folder of the Files panel (moved into <see cref="Folders"/>).</summary>
    public string? Folder { get; set; }
    /// <summary>Folders open in the Files panel, in the order they were opened.</summary>
    public List<string> Folders { get; set; } = new();
    /// <summary>Folders pinned to the top of the panel (and opened at start in "pinned" mode).</summary>
    public List<string> PinnedFolders { get; set; } = new();
    /// <summary>At start: "restore" (last session's folders), "clean" (none) or "pinned" (only the pinned ones).</summary>
    public string StartupFolders { get; set; } = "restore";
    /// <summary>Order of the folders: "opened", "az", "za" or "modified".</summary>
    public string FolderSort { get; set; } = "opened";
    /// <summary>Folder colours: "theme" (tones of the theme colour), "multi" or "none".</summary>
    public string FolderColors { get; set; } = "theme";
    /// <summary>Colour chosen by hand for a folder: path → hue in degrees.</summary>
    public Dictionary<string, string> FolderColorMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecentFolders { get; set; } = new();
    public bool FilesPanel { get; set; }
    public double FilesWidth { get; set; } = 260;
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 820;
    public bool Maximized { get; set; }
    public bool Outline { get; set; } = true;
    public bool Spell { get; set; } = true;
    /// <summary>The sample document was opened once on first start.</summary>
    public bool SampleShown { get; set; }
    /// <summary>The user hid the sample from File → Open recent (Help → Sample document still opens it).</summary>
    public bool SampleHidden { get; set; }
    public DateTime LastUpdateCheck { get; set; }

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gryphmark");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Gryphmark was called Quillforge up to 1.1.1: copy its settings, recent files and recovery
    /// drafts the first time. The old folder is left untouched; its WebView2 cache is not copied (rebuilt on start).</summary>
    private static void MigrateFromQuillforge()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quill" + "forge");
        if (Directory.Exists(Dir) || !Directory.Exists(old)) return;
        Directory.CreateDirectory(Dir);
        try { if (File.Exists(Path.Combine(old, "settings.json"))) File.Copy(Path.Combine(old, "settings.json"), FilePath, false); }
        catch { /* best effort: start with defaults */ }
        var oldRecovery = Path.Combine(old, "recovery");
        if (!Directory.Exists(oldRecovery)) return;
        foreach (var src in Directory.EnumerateFiles(oldRecovery))
        {
            try
            {
                var dst = Path.Combine(Dir, "recovery", Path.GetFileName(src));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, false);
            }
            catch { /* skip a draft that cannot be read */ }
        }
    }

    public static Settings Load()
    {
        MigrateFromQuillforge();
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt settings: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience */ }
    }

    public void AddRecentFolder(string dir)
    {
        RecentFolders.RemoveAll(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, dir);
        if (RecentFolders.Count > 6) RecentFolders.RemoveRange(6, RecentFolders.Count - 6);
    }

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 12) Recent.RemoveRange(12, Recent.Count - 12);
    }
}
