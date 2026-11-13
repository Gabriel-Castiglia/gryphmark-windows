// Gryphmark -- [[name]] links: the Markdown file of that name in the open folders, as in a wiki.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 13, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Text;

namespace Gryphmark;

/// <summary>The same rules as the macOS version (Wiki.swift): a name is looked up without extension, heading,
/// case or accent form, in the folders of the Files panel and the document's own folder; when several files
/// have the name, the nearest to the document wins (longest shared path, then the shortest path).</summary>
public static class Wiki
{
    private static readonly string[] Extensions = { ".md", ".markdown", ".mdown", ".mkd" };
    private static readonly StringComparer PathCmp = StringComparer.OrdinalIgnoreCase;

    /// <summary>The key a name is looked up by: no extension, no heading, no case, composed accents.</summary>
    public static string Key(string name)
    {
        var n = name.Normalize(NormalizationForm.FormC).Trim();
        int cut = n.IndexOfAny(new[] { '#', '|' });
        if (cut >= 0) n = n[..cut];
        n = n.Trim();
        var ext = Path.GetExtension(n);
        if (Extensions.Contains(ext, PathCmp)) n = n[..^ext.Length];
        return n.ToLowerInvariant();
    }

    // Markdown files of the searched folders by key, kept for two seconds: a page with a hundred
    // links asks once, and a file an assistant has just written is found on the next click.
    private static (string Roots, DateTime At, Dictionary<string, List<string>> Files)? _cache;
    private static readonly object Lock = new();

    private static Dictionary<string, List<string>> Files(List<string> roots)
    {
        var sig = string.Join("\n", roots);
        lock (Lock)
        {
            if (_cache is { } c && c.Roots == sig && (DateTime.UtcNow - c.At).TotalSeconds < 2) return c.Files;
            var outp = new Dictionary<string, List<string>>();
            foreach (var root in roots) Walk(root, 0, outp);
            _cache = (sig, DateTime.UtcNow, outp);
            return outp;
        }
    }

    private static void Walk(string dir, int depth, Dictionary<string, List<string>> outp)
    {
        if (depth > 12) return;
        IEnumerable<string> files, dirs;
        try { files = Directory.EnumerateFiles(dir).ToList(); dirs = Directory.EnumerateDirectories(dir).ToList(); }
        catch { return; }   // unreadable folder: skipped
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith('.') || !Extensions.Contains(Path.GetExtension(f), PathCmp)) continue;
            var k = Key(Path.GetFileNameWithoutExtension(f));
            if (!outp.TryGetValue(k, out var list)) outp[k] = list = new();
            if (!list.Contains(f, PathCmp)) list.Add(f);
        }
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.')) continue;
            try { if (new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.Hidden)) continue; } catch { continue; }
            Walk(d, depth + 1, outp);
        }
    }

    /// <summary>The panel folders plus the document's own folder (each folder searched once).</summary>
    private static List<string> Searched(string? docFolder, IEnumerable<string> panel)
    {
        var roots = panel.Select(p => Path.GetFullPath(p).TrimEnd('\\')).Distinct(PathCmp).ToList();
        if (docFolder != null)
        {
            var d = Path.GetFullPath(docFolder).TrimEnd('\\');
            if (!roots.Any(r => Inside(d, r))) roots.Add(d);
        }
        return roots;
    }

    private static bool Inside(string path, string root) =>
        PathCmp.Equals(path, root) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file a [[name]] points to, or null.</summary>
    public static string? Resolve(string name, string? docFolder, IEnumerable<string> panel)
    {
        if (!Files(Searched(docFolder, panel)).TryGetValue(Key(name), out var hits) || hits.Count == 0) return null;
        if (docFolder == null || hits.Count == 1) return hits[0];
        var dir = Path.GetFullPath(docFolder).TrimEnd('\\').Split('\\');
        int Shared(string f) => Path.GetDirectoryName(f)!.Split('\\').Zip(dir).TakeWhile(p => PathCmp.Equals(p.First, p.Second)).Count();
        return hits.OrderByDescending(Shared).ThenBy(f => f.Split('\\').Length).First();
    }

    /// <summary>For each name, whether it points to a file.</summary>
    public static Dictionary<string, bool> Exist(IEnumerable<string> names, string? docFolder, IEnumerable<string> panel)
    {
        var all = Files(Searched(docFolder, panel));
        return names.Distinct().ToDictionary(n => n, n => all.TryGetValue(Key(n), out var l) && l.Count > 0);
    }

    public static void Forget() { lock (Lock) _cache = null; }
}
