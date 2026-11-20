// Vigia -- main window: the document's files for the page (pictures, links), as on the Mac.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 16, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Vigia;

/// <summary>The page reaches the document's files at https://doc.local/&lt;absolute path&gt; (C%3A/Docs/…, or
/// UNC/server/share/… for a network path), served here, as the Mac serves gm-doc://doc/&lt;path&gt;. Before 2.0.0
/// doc.local was the document's folder itself, so ../picture.png and [link](../other.md) pointed into that folder
/// and never reached the file. What is served: what the document may reach (its folder, and every folder of the
/// Files panel when it is inside one of them, as Wiki.swift's reach), and a picture from anywhere on the computer;
/// nothing else.</summary>
public partial class MainWindow
{
    private const string DocHost = "https://doc.local/";
    private static readonly string[] PictureExt = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".avif", ".ico" };
    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".svg"] = "image/svg+xml", [".avif"] = "image/avif",
        [".ico"] = "image/x-icon", [".pdf"] = "application/pdf", [".md"] = "text/plain; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    };

    private static bool IsPicture(string p) => PictureExt.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase);

    /// <summary>The page's address of a file or folder: https://doc.local/C%3A/a%20b/c.</summary>
    internal static string DocUrl(string fullPath)
    {
        var p = Path.GetFullPath(fullPath);
        var segs = p.StartsWith(@"\\") ? new[] { "UNC" }.Concat(p[2..].Split('\\')) : p.Split('\\');
        return DocHost + string.Join("/", segs.Where(s => s.Length > 0).Select(Uri.EscapeDataString));
    }

    /// <summary>The page base for a document: its folder's address with a final slash, as JavaScript ("null": none).</summary>
    private static string DocBaseJs(string? docPath) =>
        docPath == null ? "null" : Q(DocUrl(Path.GetDirectoryName(Path.GetFullPath(docPath))!) + "/");

    /// <summary>The file a doc.local address stands for, or null.</summary>
    internal static string? DocPath(string uri)
    {
        if (!uri.StartsWith(DocHost, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = uri[DocHost.Length..];
        int cut = rest.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) rest = rest[..cut];
        var segs = rest.Split('/').Where(s => s.Length > 0).Select(Uri.UnescapeDataString).ToList();
        if (segs.Count == 0) return null;
        string joined;
        if (segs[0].Equals("UNC", StringComparison.OrdinalIgnoreCase) && segs.Count >= 3) joined = @"\\" + string.Join("\\", segs.Skip(1));
        else if (segs[0].Length == 2 && char.IsAsciiLetter(segs[0][0]) && segs[0][1] == ':') joined = segs[0] + "\\" + string.Join("\\", segs.Skip(1));
        else return null;
        if (joined.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        try { return Path.GetFullPath(joined); } catch { return null; }
    }

    /// <summary>True when the open document may reach this path (its folder, or the panel's folders it is in).</summary>
    private bool InReach(string path) =>
        _path != null && Wiki.Reach(Path.GetDirectoryName(_path), _roots).Any(r => Wiki.Inside(path, r));

    private void InitDocFiles(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter(DocHost + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnDocRequest;
    }

    private async void OnDocRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = Web.CoreWebView2.Environment;
        var path = DocPath(e.Request.Uri);
        if (path == null || !(IsPicture(path) || InReach(path)))
        {
            e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        var deferral = e.GetDeferral();
        try
        {
            var bytes = await Task.Run(() => File.Exists(path) ? File.ReadAllBytes(path) : null);   // a large picture: off the window's thread
            if (bytes == null) { e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
            var type = Mime.TryGetValue(Path.GetExtension(path), out var t) ? t : "application/octet-stream";
            e.Response = env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", $"Content-Type: {type}\r\nCache-Control: no-cache");
        }
        catch { e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", ""); }
        finally { deferral.Complete(); }
    }

    /// <summary>A ![[name]] picture that did not load beside the document: the file of that name in the open folders.</summary>
    private async Task FindFileAsync(string name)
    {
        string? docDir = _path != null ? Path.GetDirectoryName(_path) : null;
        var panel = _roots.ToList();
        var f = await Task.Run(() => Wiki.FindFile(name, docDir, panel));   // a disk walk: off the window's thread
        if (_ready) await Js($"mdApp.fileFound({Q(name)},{(f != null ? Q(DocUrl(f)) : "null")})");
    }
}
