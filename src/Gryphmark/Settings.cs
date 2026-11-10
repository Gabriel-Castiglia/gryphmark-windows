using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Gryphmark;

public sealed class Settings
{
    public string Lang { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
    public string Theme { get; set; } = "light";
    public string Mode { get; set; } = "visual";
    public double Zoom { get; set; } = 1.0;
    public List<string> Recent { get; set; } = new();
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 820;
    public bool Maximized { get; set; }
    public bool Outline { get; set; } = true;
    public bool Spell { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gryphmark");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Gryphmark was called Quillforge up to 1.1.1: copy its settings, recent files and recovery
    /// drafts the first time. The old folder is left untouched.</summary>
    private static void MigrateFromQuillforge()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quill" + "forge");
        if (Directory.Exists(Dir) || !Directory.Exists(old)) return;
        try
        {
            foreach (var src in Directory.EnumerateFiles(old, "*", SearchOption.AllDirectories))
            {
                var dst = Path.Combine(Dir, Path.GetRelativePath(old, src));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, false);
            }
        }
        catch { /* best effort: start with defaults */ }
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

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 12) Recent.RemoveRange(12, Recent.Count - 12);
    }
}
