// Gryphmark -- settings and session, per profile.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 10, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    public bool FilesPanel { get; set; } = true;   // open by default, as the sidebar of the macOS version
    public double FilesWidth { get; set; } = 260;       // width of the sidebar (Files over Contents), 180 to 700
    public double OutlineShare { get; set; } = 0.45;    // part of the sidebar's height for Contents when both show
    public bool TreeLocked { get; set; } = true;        // unlocked: files and folders can be dragged in the tree (moved on disk)
    public int TextWidth { get; set; } = 980;           // text column in px (980 = GitHub's); 0 = the whole window
    /// <summary>2.0: stacked | columns | right. From 2.1 the layout is the four values below; an old value is
    /// turned into them once at start (MainWindow.Layout.cs) and this is left empty.</summary>
    public string SideLayout { get; set; } = "stacked";
    public string FilesSide { get; set; } = "left";     // left | right of the text
    public string OutlineSide { get; set; } = "left";   // left | right of the text
    public string SideArrange { get; set; } = "rows";   // left side, two panes or more: rows (one above the other) | cols (side by side)
    public string RightArrange { get; set; } = "";      // the same for the right side ("" until 2.2 copies SideArrange)
    public bool OutlineFirst { get; set; }              // 2.1: Contents before Files on one side (2.2: PaneOrder)
    public string SearchSide { get; set; } = "left";    // the Search pane (2.2), left | right of the text
    public bool SearchPanel { get; set; }               // the Search pane is shown
    /// <summary>Order of the panes on a side, first = top / left (2.2): "files", "outline", "search".</summary>
    public List<string> PaneOrder { get; set; } = new();
    /// <summary>Room of each pane against the others on its side (star weights), by pane name.</summary>
    public Dictionary<string, double> PaneWeights { get; set; } = new();
    /// <summary>Width of a sidebar per layout (2.2): key = side and the panes on it with their arrangement.</summary>
    public Dictionary<string, double> LayoutWidths { get; set; } = new();
    public bool RecentPanel { get; set; }               // a RECENT block in the Files panel (off by default)
    public double SideWidthColumns { get; set; } = 520; // sidebar width when Files and Contents sit side by side
    public double OutlineShareColumns { get; set; } = 0.45; // part of that width for Contents
    public double OutlineWidthRight { get; set; } = 260; // width of the right sidebar with one pane
    /// <summary>Favourite files: one list for all the open folders, shown above the trees (MainWindow.Favorites.cs).</summary>
    public List<string> FavoriteFiles { get; set; } = new();
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 820;
    public bool Maximized { get; set; }
    /// <summary>Position of the window when it was last closed (null: centred on the screen).</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Outline { get; set; } = true;
    public bool Spell { get; set; } = true;
    public bool AutoSave { get; set; } = true;   // saves a moment after the typing stops (MainWindow.AutoSave)
    /// <summary>The sample document was opened once on first start.</summary>
    public bool SampleShown { get; set; }
    /// <summary>The user hid the sample from File → Open recent (Help → Sample document still opens it).</summary>
    public bool SampleHidden { get; set; }
    public DateTime LastUpdateCheck { get; set; }

    // ------------------------------------------------------------------ instances
    // Main instance: %APPDATA%\Gryphmark. A dedicated instance (--profile "Name") has its own folder under
    // profiles\, so its folders, window and recent files never mix with the main one; it starts with the
    // main instance's preferences (language, theme, colour…). A plain extra window (--new-window) shares the
    // main settings but only writes back preferences, never the session (open folders, window size…).

    /// <summary>Name of the dedicated instance, or null for the main one (set once at start-up).</summary>
    public static string? Profile { get; set; }
    /// <summary>True for an extra window of the main instance (another one already owns the session).</summary>
    public static bool IsSecondary { get; set; }

    public static string BaseDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gryphmark");
    public static string Dir => Profile == null ? BaseDir : Path.Combine(BaseDir, "profiles", SafeName(Profile));
    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Profiles left by --self-test and --render-chrome (their browser still held files when they ended):
    /// deleted at a later start once they are a few minutes old. Only names of that exact form, never a user's.</summary>
    internal static readonly System.Text.RegularExpressions.Regex TestProfileName = new(@"^(SelfTest|RenderChrome)-[0-9a-f]{8}$");

    public static void CleanupTestProfiles()
    {
        try
        {
            var dir = new DirectoryInfo(Path.Combine(BaseDir, "profiles"));
            if (!dir.Exists) return;
            foreach (var p in dir.EnumerateDirectories())
                if (TestProfileName.IsMatch(p.Name) && DateTime.UtcNow - p.LastWriteTimeUtc > TimeSpan.FromMinutes(5))
                    try { p.Delete(true); } catch { }
        }
        catch { }
    }

    public static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "profile" : s;
    }

    /// <summary>The session of one window: what an extra window must not write over.</summary>
    private static readonly HashSet<string> SessionKeys = new()
    {
        nameof(Folder), nameof(Folders), nameof(PinnedFolders), nameof(Width), nameof(Height), nameof(Left), nameof(Top), nameof(Maximized),
        nameof(FilesPanel), nameof(FilesWidth), nameof(OutlineShare), nameof(SampleShown),
    };

    /// <summary>Gryphmark was called Quillforge up to 1.1.1: copy its settings, recent files and recovery
    /// drafts the first time. The old folder is left untouched; its WebView2 cache is not copied (rebuilt on start).</summary>
    private static void MigrateFromQuillforge()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quill" + "forge");
        if (Directory.Exists(BaseDir) || !Directory.Exists(old)) return;
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
        if (Profile == null) MigrateFromQuillforge();
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            if (Profile != null)   // new dedicated instance: the main preferences, an empty session
            {
                var main = Path.Combine(BaseDir, "settings.json");
                var s = File.Exists(main) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(main)) ?? new Settings() : new Settings();
                s.Folder = null; s.Folders = new(); s.PinnedFolders = new(); s.Recent = new(); s.RecentFolders = new();
                s.FilesPanel = false; s.SampleShown = true; s.StartupFolders = "restore";
                return s;
            }
        }
        catch { /* corrupt settings: start fresh */ }
        return new Settings();
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (IsSecondary && File.Exists(FilePath))
            {
                // extra window: keep the owner's session, take this window's preferences
                var disk = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
                var mine = JsonSerializer.SerializeToNode(this)!.AsObject();
                foreach (var (k, v) in mine)
                    if (!SessionKeys.Contains(k)) disk[k] = v?.DeepClone();
                File.WriteAllText(FilePath, disk.ToJsonString(Indented));
                return;
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Indented));
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
