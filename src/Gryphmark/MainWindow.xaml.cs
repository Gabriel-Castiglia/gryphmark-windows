// Gryphmark -- main window: the web view, files and commands.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 10, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Gryphmark;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private string? _path;
    private bool _dirty;
    private bool _ready;
    private string? _pendingOpen;
    private bool _closingConfirmed;
    private int _words;

    private static string AppDir => AppContext.BaseDirectory;
    private static string Version => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public MainWindow(string? startPath)
    {
        InitializeComponent();
        Strings.Lang = _settings.Lang;
        string? startFolder = null;
        if (startPath != null && Directory.Exists(startPath)) { startFolder = startPath; startPath = null; }
        _pendingOpen = startPath;
        Width = _settings.Width; Height = _settings.Height;
        if (_settings.Maximized) WindowState = WindowState.Maximized;
        if (!_settings.AccentChosen) _settings.Accent = Accents.Default;
        ThemeManager.Apply(this, IsDark, _settings.Accent);
        Icon = Accents.WindowIcon(_settings.Accent) ?? Icon;
        // page colour from the first frame, so there is no white/black flash while the editor loads
        Web.DefaultBackgroundColor = IsDark ? System.Drawing.Color.FromArgb(13, 17, 23) : System.Drawing.Color.White;
        BuildMenu();
        UpdateTitle();
        SourceInitialized += (_, _) => ThemeManager.Apply(this, IsDark, _settings.Accent);
        Accents.ApplyToSystem(_settings.Accent);   // e.g. after an update reinstalled the default icon
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreference;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreference;
        Loaded += async (_, _) => await InitWebAsync();
        Closing += OnClosing;
        InitFeatures();
        InitFit();
        InitFolder(startFolder);
        PreviewKeyDown += OnKey;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) { if (Directory.Exists(f[0])) OpenFolder(f[0]); else OpenPath(f[0]); }
        };
    }

    // ------------------------------------------------------------------ WebView
    // Spell check dictionary of each interface language (Windows spell checker tags).
    private static string SpellLang(string lang) => lang == "es" ? "es-419" : "en-US";
    private string? _webSpell;                         // dictionary the running browser was started with
    private (string Text, bool Dirty)? _restore;       // document carried over a browser restart

    // The browser keeps its spell check languages in its own profile and only reads them at
    // start-up, so each dictionary gets its own profile folder, seeded before the browser starts.
    private static string WebProfile(string spell)
    {
        var dir = Path.Combine(Settings.Dir, "WebView2", spell);
        try
        {
            var prefsFile = Path.Combine(dir, "EBWebView", "Default", "Preferences");
            Directory.CreateDirectory(Path.GetDirectoryName(prefsFile)!);
            var prefs = (File.Exists(prefsFile) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(prefsFile)) as System.Text.Json.Nodes.JsonObject : null)
                        ?? new System.Text.Json.Nodes.JsonObject();
            prefs["spellcheck"] ??= new System.Text.Json.Nodes.JsonObject();
            prefs["spellcheck"]!["dictionaries"] = new System.Text.Json.Nodes.JsonArray(spell);
            prefs["intl"] ??= new System.Text.Json.Nodes.JsonObject();
            prefs["intl"]!["selected_languages"] = spell;
            File.WriteAllText(prefsFile, prefs.ToJsonString());
        }
        catch { /* the browser then falls back to its own default */ }
        return dir;
    }

    private async Task InitWebAsync()
    {
        try
        {
            _webSpell = SpellLang(_settings.Lang);
            var env = await CoreWebView2Environment.CreateAsync(null, WebProfile(_webSpell),
                new CoreWebView2EnvironmentOptions { Language = _webSpell });
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception)
        {
            MessageBox.Show(this, Strings.Get("NoWebView"), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }
        var core = Web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(AppDir, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenLink(e.Uri); };
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith("https://app.local/", StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; OpenLink(e.Uri); }
        };
        Web.ZoomFactor = _settings.Zoom;
        core.Navigate("https://app.local/index.html");
    }

    private async Task<string> Js(string script) => await Web.CoreWebView2.ExecuteScriptAsync(script);
    private static string Q(string s) => JsonSerializer.Serialize(s);

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json = e.WebMessageAsJson;
        if (json == "\"drop\"")
        {
            if (e.AdditionalObjects?.FirstOrDefault() is CoreWebView2File f) OpenPath(f.Path);
            return;
        }
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                await Js($"mdApp.init({Q(_settings.Lang)},{Q(IsDark ? "dark" : "light")},{Q(_settings.Mode)},{(_settings.Spell ? "true" : "false")},{(_settings.ReadOnly ? "true" : "false")},true)");
                _ready = true;
                _lastEditSeq = 0;   // a new page counts its edits from zero
                if (_restore is { } r) { _restore = null; await RestoreDocAsync(r.Text, r.Dirty); }
                else if (_pendingOpen != null) { var p = _pendingOpen; _pendingOpen = null; await LoadFileAsync(p); }
                else if (!_settings.SampleShown && SamplePath != null) { _settings.SampleShown = true; _settings.Save(); await LoadSampleAsync(); }
                else UpdateStatus();
                if (SelfTestLog != null) { _ = Dispatcher.BeginInvoke(async () => await RunSelfTestAsync()); break; }
                await OfferRecoveryAsync();
                break;
            case "dirty":
                if (root.TryGetProperty("words", out var w)) _words = w.GetInt32();
                if (root.TryGetProperty("seq", out var sq)) _lastEditSeq = Math.Max(_lastEditSeq, sq.GetInt64());
                if (!_dirty) { _dirty = true; UpdateTitle(); }
                UpdateStatus();
                ScheduleAutoSave();
                break;
            case "lockedHint":
                Flash(Strings.Get("LockedHint"));
                break;
            case "cmd":
                await RunCommand(root.GetProperty("cmd").GetString() ?? "");
                break;
            case "find":
                UpdateFindCount(root.GetProperty("index").GetInt32(), root.GetProperty("total").GetInt32());
                break;
            case "outline":
                UpdateOutline(root.GetProperty("items"));
                break;
            case "export":
                WriteExport(root);
                break;
            case "printReady":
                Web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                break;
            case "link":
                OpenLink(root.GetProperty("href").GetString() ?? "");
                break;
            case "wikilink":
                OpenWiki(root.GetProperty("name").GetString() ?? "");
                break;
            case "wikinames":
                var names = root.GetProperty("names").EnumerateArray().Select(n => n.GetString() ?? "").ToList();
                string? docDir = _path != null ? Path.GetDirectoryName(_path) : null;
                var panel = _roots.ToList();
                var found = await Task.Run(() => Wiki.Exist(names, docDir, panel));   // a disk walk: off the window's thread
                if (_ready) await Js($"mdApp.wikiState({JsonSerializer.Serialize(found)})");
                break;
        }
    }

    private void OpenLink(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return;
        try
        {
            if (uri.StartsWith("https://doc.local/", StringComparison.OrdinalIgnoreCase) && _path != null)
            {
                var rel = Uri.UnescapeDataString(new Uri(uri).AbsolutePath.TrimStart('/'));
                var local = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_path)!, rel));
                if (IsMarkdown(local) && File.Exists(local)) { OpenPath(local); return; }
                // A document must never be able to run a program: only pictures and PDFs open
                // with their viewer; any other file (.exe, .bat, .lnk...) is shown in Explorer.
                if (File.Exists(local))
                {
                    if (IsViewable(local)) Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
                    else Process.Start("explorer.exe", $"/select,\"{local}\"");
                    return;
                }
                return;
            }
            if (uri.StartsWith("https://doc.local/", StringComparison.OrdinalIgnoreCase)) return;   // no document folder
            if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeMailto))
                Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* nothing sensible to do with a broken link */ }
    }

    /// <summary>A [[name]] link of the GitHub view: the file of that name in the open folders (Wiki.cs).</summary>
    private void OpenWiki(string name)
    {
        Wiki.Forget();
        var f = Wiki.Resolve(name, _path != null ? Path.GetDirectoryName(_path) : null, _roots);
        if (f != null) OpenPath(f);
        else Flash(string.Format(Strings.Get("WikiMissing"), Wiki.Key(name)));
    }

    private static bool IsViewable(string p) =>
        new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".pdf" }.Contains(Path.GetExtension(p).ToLowerInvariant());

    private static bool IsMarkdown(string p) =>
        new[] { ".md", ".markdown", ".mdown", ".mkd", ".txt" }.Contains(Path.GetExtension(p).ToLowerInvariant());

    // ------------------------------------------------------------------ Files
    private async void OpenPath(string path)
    {
        if (Directory.Exists(path)) { OpenFolder(path); return; }
        if (!_ready) { _pendingOpen = path; return; }
        if (!await ConfirmDiscardAsync()) return;
        await LoadFileAsync(path);
    }

    /// <summary>The file as text; remembers its BOM and line endings so a save writes them back.
    /// Shared read access: another program may still have it open for writing.</summary>
    private string ReadDocument(string path)
    {
        byte[] bytes;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }
        _bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = new UTF8Encoding(false).GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0));
        _crlf = text.Contains("\r\n");
        return text;
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, string.Format(Strings.Get("NotFound"), path), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Warning);
                _settings.Recent.Remove(path); BuildMenu();
                return;
            }
            string text = ReadDocument(path);
            var dir = Path.GetDirectoryName(path)!;
            Web.CoreWebView2.ClearVirtualHostNameToFolderMapping("doc.local");
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping("doc.local", dir, CoreWebView2HostResourceAccessKind.Allow);
            await Js($"mdApp.load({Q(text)},'https://doc.local/')");
            _path = path; _dirty = false; _isSample = false; _diskText = text;
            Watch(path);
            _words = int.TryParse(await Js("mdApp.words()"), out var n) ? n : 0;
            _settings.AddRecent(path); _settings.Save();
            BuildMenu(); UpdateTitle(); UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Get("ErrOpen"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Writes the document. auto: an autosave, which reports a failure in the status bar
    /// instead of a dialog.</summary>
    private async Task<bool> SaveAsync(bool forceDialog, bool auto = false)
    {
        var target = _path;
        if (forceDialog || target == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = Strings.Get("MdFilter"),
                DefaultExt = ".md",
                FileName = target != null ? Path.GetFileName(target) : Strings.Get("Untitled") + ".md",
                InitialDirectory = target != null ? Path.GetDirectoryName(target) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dlg.ShowDialog(this) != true) return false;
            target = dlg.FileName;
        }
        _saving = true;
        try
        {
            // untouched blocks keep their original Markdown; line endings and BOM as the file had them.
            // The text is taken and marked as saved in one call; seq is the last edit it holds.
            using var snap = JsonDocument.Parse(await Js("mdApp.takeForSave()"));
            var md = snap.RootElement.GetProperty("text").GetString() ?? "";
            long seq = snap.RootElement.GetProperty("seq").GetInt64();
            var disk = _crlf ? md.Replace("\r\n", "\n").Replace("\n", "\r\n") : md;
            File.WriteAllText(target!, disk, new UTF8Encoding(_bom));
            _diskText = disk;   // the watcher sees this write; the same text on disk is not a change
            bool moved = !string.Equals(target, _path, StringComparison.OrdinalIgnoreCase);
            _path = target;
            _dirty = _lastEditSeq > seq;   // typed while saving: still modified, and saved again shortly
            if (_dirty) ScheduleAutoSave();
            Watch(target!); ClearRecovery();
            if (moved)
            {
                Web.CoreWebView2.ClearVirtualHostNameToFolderMapping("doc.local");
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping("doc.local", Path.GetDirectoryName(target)!, CoreWebView2HostResourceAccessKind.Allow);
            }
            _settings.AddRecent(target!); _settings.Save();
            BuildMenu(); UpdateTitle(); UpdateStatus();
            return true;
        }
        catch (Exception ex)
        {
            if (auto) Flash(string.Format(Strings.Get("ErrAutoSave"), ex.Message));
            else MessageBox.Show(this, string.Format(Strings.Get("ErrSave"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally { _saving = false; }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        // With autosave on, a file with a name is saved without asking, unless another program
        // changed it meanwhile: then the question below decides.
        if (CanAutoSave() && await SaveAsync(false, auto: true)) return true;
        var name = _path != null ? Path.GetFileName(_path) : Strings.Get("Untitled");
        // Don't save · Cancel · Save (default), as on the Mac
        int r = ChoiceDialog.Ask(this, string.Format(Strings.Get("AskSave"), name), new[] { Strings.Get("DontSave"), Strings.Get("Cancel"), Strings.Get("SaveBtn") }, 1);
        if (r == 1) return false;
        if (r == 2) return await SaveAsync(false);
        return true;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { _settings.Width = Width; _settings.Height = Height; }
        if (_ready) _settings.Zoom = Web.ZoomFactor;
        _settings.Save();
        if (_closingConfirmed || !_dirty) { Hide(); ClearRecovery(); StopFeatures(); return; }
        e.Cancel = true;
        if (await ConfirmDiscardAsync()) { _closingConfirmed = true; ClearRecovery(); StopFeatures(); Close(); }
    }

    // ------------------------------------------------------------------ Sample document
    // Installed hidden in {app}\samples (one per language). It opens as an untitled copy, so the
    // installed file is never overwritten; Save asks for a new name.
    private bool _isSample;

    private static string? SamplePath
    {
        get
        {
            foreach (var lang in new[] { Strings.Lang, "en" })
            {
                var p = Path.Combine(AppDir, "samples", $"sample_{lang}.md");
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }

    private async Task OpenSampleAsync()
    {
        if (!_ready || !await ConfirmDiscardAsync()) return;
        await LoadSampleAsync();
    }

    private async Task LoadSampleAsync()
    {
        var p = SamplePath;
        if (p == null) return;
        try
        {
            var text = File.ReadAllText(p, Encoding.UTF8);
            Web.CoreWebView2.ClearVirtualHostNameToFolderMapping("doc.local");
            await Js($"mdApp.load({Q(text)}, null)");
            _path = null; _dirty = false; _crlf = false; _bom = false; _isSample = true;
            Watch(null); ClearRecovery();
            _words = int.TryParse(await Js("mdApp.words()"), out var n) ? n : 0;
            UpdateTitle(); UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Get("ErrOpen"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task NewDocAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        Web.CoreWebView2.ClearVirtualHostNameToFolderMapping("doc.local");
        await Js("mdApp.load('', null)");
        _path = null; _dirty = false; _words = 0; _crlf = false; _bom = false; _isSample = false;
        Watch(null); ClearRecovery();
        UpdateTitle(); UpdateStatus();
    }

    private async Task ExportHtmlAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = Strings.Get("HtmlFilter"), DefaultExt = ".html",
            FileName = Path.GetFileNameWithoutExtension(_path ?? Strings.Get("Untitled")) + ".html",
            InitialDirectory = _path != null ? Path.GetDirectoryName(_path) : null,
        };
        if (dlg.ShowDialog(this) != true) return;
        _pendingExport = dlg.FileName;
        // the page renders (diagrams included) and answers with an "export" message
        await Js($"mdApp.exportHtml({Q(Path.GetFileNameWithoutExtension(dlg.FileName))})");
    }

    private string? _pendingExport;

    private void WriteExport(JsonElement r)
    {
        if (_pendingExport == null) return;
        var file = _pendingExport; _pendingExport = null;
        var title = r.GetProperty("title").GetString() ?? "";
        bool dark = r.GetProperty("theme").GetString() == "dark";
        bool math = r.TryGetProperty("math", out var mv) && mv.GetBoolean();
        string css = File.ReadAllText(Path.Combine(AppDir, "wwwroot", "lib", dark ? "github-markdown-dark.css" : "github-markdown-light.css"));
        string hl = File.ReadAllText(Path.Combine(AppDir, "wwwroot", "lib", dark ? "hljs-dark.css" : "hljs-light.css"));
        string bg = dark ? "#0d1117" : "#ffffff";
        string body = r.GetProperty("body").GetString() ?? "";
        string html = $$"""
            <!doctype html>
            <html lang="{{Strings.Lang}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="Gryphmark {{Version}}">
            <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
            {{(math ? "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/katex@0.19.1/dist/katex.min.css\">" : "")}}
            <style>
            {{css}}
            {{hl}}
            body { margin: 0; background: {{bg}}; }
            .markdown-body { box-sizing: border-box; max-width: 980px; margin: 0 auto; padding: 45px; }
            @media (max-width: 767px) { .markdown-body { padding: 15px; } }
            </style>
            </head>
            <body>
            <article class="markdown-body">
            {{body}}
            </article>
            </body>
            </html>
            """;
        try { File.WriteAllText(file, html, new UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, string.Format(Strings.Get("ErrSave"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // ------------------------------------------------------------------ Commands
    private async Task RunCommand(string cmd)
    {
        if (!_ready && cmd != "exit") return;
        switch (cmd)
        {
            case "new": await NewDocAsync(); break;
            case "newwindow": Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--new-window") { UseShellExecute = true }); break;
            case "open":
                var dlg = new OpenFileDialog
                {
                    Filter = Strings.Get("MdFilter"),
                    InitialDirectory = _path != null ? Path.GetDirectoryName(_path) : null,
                };
                if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName);
                break;
            case "save": await SaveAsync(false); break;
            case "saveas": await SaveAsync(true); break;
            case "export": await ExportHtmlAsync(); break;
            case "print": await Js("mdApp.preparePrint()"); break;   // continues on "printReady"
            case "find": ShowFind(false); break;
            case "replace": ShowFind(true); break;
            case "findnext": await FindStep(1); break;
            case "findprev": await FindStep(-1); break;
            case "findclose": await HideFind(); break;
            case "outline": ToggleOutline(); break;
            case "files": ToggleFiles(); break;
            case "readonly": await SetReadOnly(!_settings.ReadOnly); break;
            case "opendir": PickFolder(); break;
            case "manual": OpenManual(); break;
            case "folder":
                if (_path != null) Process.Start("explorer.exe", $"/select,\"{_path}\"");
                break;
            case "close": case "exit": Close(); break;
            case "mode1": await SetMode("visual"); break;
            case "mode2": await SetMode("source"); break;
            case "mode3": await SetMode("preview"); break;
            case "zoom+": Web.ZoomFactor = Math.Min(Web.ZoomFactor + 0.1, 3); break;
            case "zoom-": Web.ZoomFactor = Math.Max(Web.ZoomFactor - 0.1, 0.5); break;
            case "zoom0": Web.ZoomFactor = 1; break;
        }
    }

    private async Task SetMode(string mode)
    {
        _settings.Mode = mode; _settings.Save();
        SyncSegments();
        if (_ready) await Js($"mdApp.setMode({Q(mode)})");
        BuildMenu(); UpdateStatus();
    }

    // ------------------------------------------------------------------ theme (View → Theme…)
    private bool IsDark => _settings.Theme == "dark" || (_settings.Theme == "auto" && ThemeManager.SystemDark());
    internal string AccentKey => _settings.Accent;
    private bool _appliedDark;

    private async Task ApplyThemeAsync()
    {
        bool dark = IsDark;
        ThemeManager.Apply(this, dark, _settings.Accent);
        Icon = Accents.WindowIcon(_settings.Accent) ?? Icon;
        Web.DefaultBackgroundColor = dark ? System.Drawing.Color.FromArgb(13, 17, 23) : System.Drawing.Color.White;
        if (_ready && dark != _appliedDark) await Js($"mdApp.setTheme({Q(dark ? "dark" : "light")})");
        _appliedDark = dark;
        if (_folderInit) RefreshTree();   // folder colours follow the theme
    }

    private void ShowThemeDialog()
    {
        var dlg = new ThemeDialog(this, _settings.Theme, _settings.Accent,
            async mode => { _settings.Theme = mode; _settings.Save(); await ApplyThemeAsync(); },
            async key => { _settings.Accent = key; _settings.AccentChosen = true; _settings.Save(); await ApplyThemeAsync(); Accents.ApplyToSystem(key); });
        dlg.ShowDialog();
    }

    private void OnSystemPreference(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == "auto") Dispatcher.BeginInvoke(async () => await ApplyThemeAsync());
    }

    private async Task SetReadOnly(bool on)
    {
        _settings.ReadOnly = on; _settings.Save();
        SyncSegments();
        if (_ready) await Js($"mdApp.setLocked({(on ? "true" : "false")})");
        BuildMenu(); UpdateStatus();
        if (_ready && !on) await Js("mdApp.focus()");
    }

    private async Task Exec(string cmd, string payload = "undefined")
    {
        if (_ready) await Js($"mdApp.exec({Q(cmd)},{payload})");
    }

    private async void OnKey(object sender, KeyEventArgs e)
    {
        // Shortcuts when the focus is on the menu or the window frame (the page handles its own)
        if (Keyboard.Modifiers != ModifierKeys.Control && Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
        if (Web.IsKeyboardFocusWithin) return;
        string? cmd = e.Key switch
        {
            Key.N => "new", Key.O => "open", Key.P => "print", Key.W => "close",
            Key.S => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "saveas" : "save",
            Key.D1 => "mode1", Key.D2 => "mode2", Key.D3 => "mode3",
            Key.E when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "files",
            Key.L when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "readonly",
            _ => null,
        };
        if (cmd != null) { e.Handled = true; await RunCommand(cmd); }
    }

    // ------------------------------------------------------------------ Menu
    // Segoe Fluent Icons glyph per command (empty = no icon)
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["New"] = "", ["NewWindow"] = "", ["Open"] = "", ["Recent"] = "",
        ["Save"] = "", ["SaveAs"] = "", ["ExportHtml"] = "", ["Print"] = "",
        ["OpenFolder"] = "", ["Exit"] = "", ["Undo"] = "", ["Redo"] = "",
        ["Bold"] = "", ["Italic"] = "", ["Strike"] = "", ["Code"] = "",
        ["Heading"] = "", ["Quote"] = "", ["Ul"] = "", ["Ol"] = "",
        ["Task"] = "", ["CodeBlock"] = "", ["Hr"] = "",
        ["ModeVisual"] = "", ["ModeSource"] = "", ["ModePreview"] = "",
        ["Dark"] = "", ["ZoomIn"] = "", ["ZoomOut"] = "", ["ZoomReset"] = "",
        ["AutoSave"] = "\uE895",
        ["OpenDir"] = "\uE838", ["Files"] = "\uE8B7", ["CloseDir"] = "\uE711", ["Sample"] = "\uE736", ["Manual"] = "", ["Guide"] = "", ["About"] = "",
        ["Find"] = "", ["Replace"] = "", ["FindNext"] = "", ["Outline"] = "", ["CheckUpdates"] = "", ["RecentClear"] = "",
    };

    private static object? Glyph(string key) => Glyphs.TryGetValue(key, out var g)
        ? new TextBlock { Text = g, FontFamily = (FontFamily)Application.Current.Resources["Gm.Icons"], FontSize = 14 }
        : null;

    private void SetSampleHidden(bool hidden)
    {
        _settings.SampleHidden = hidden;
        _settings.Save();
        BuildMenu();
    }

    private MenuItem Item(string key, string? gesture, Func<Task> action, bool? check = null, bool enabled = true)
    {
        var mi = new MenuItem { Header = Strings.Get(key), InputGestureText = gesture ?? "", IsEnabled = enabled, Icon = Glyph(key) };
        if (check.HasValue) { mi.IsCheckable = true; mi.IsChecked = check.Value; }
        mi.Click += async (_, _) => await action();
        return mi;
    }

    private void BuildMenu()
    {
        string ctrl = "Ctrl+";
        MainMenu.Items.Clear();

        var file = new MenuItem { Header = Strings.Get("File") };
        file.Items.Add(Item("New", ctrl + "N", () => RunCommand("new")));
        file.Items.Add(Item("NewWindow", null, () => RunCommand("newwindow")));
        file.Items.Add(Item("Open", ctrl + "O", () => RunCommand("open")));
        file.Items.Add(Item("OpenDir", null, () => RunCommand("opendir")));
        var recent = new MenuItem { Header = Strings.Get("Recent"), Icon = Glyph("Recent") };
        if (SamplePath != null && !_settings.SampleHidden)
        {
            var smi = new MenuItem { Header = Strings.Get("SampleItem"), Icon = Glyph("Sample"), ToolTip = Strings.Get("SampleTip") };
            smi.Click += async (_, _) => await OpenSampleAsync();
            var hide = new MenuItem { Header = Strings.Get("SampleHide") };
            hide.Click += (_, _) => SetSampleHidden(true);
            smi.ContextMenu = new ContextMenu { Items = { hide } };
            recent.Items.Add(smi);
            var hideVisible = new MenuItem { Header = Strings.Get("SampleHide"), Icon = Glyph("CloseDir") };
            hideVisible.Click += (_, _) => SetSampleHidden(true);
            recent.Items.Add(hideVisible);
            recent.Items.Add(new Separator());
        }
        if (_settings.Recent.Count == 0 && _settings.RecentFolders.Count == 0) recent.Items.Add(new MenuItem { Header = Strings.Get("RecentEmpty"), IsEnabled = false });
        else
        {
            int i = 1;
            foreach (var p in _settings.Recent.ToList())
            {
                var mi = new MenuItem { Header = $"_{(i <= 9 ? i.ToString() : "")}  {Path.GetFileName(p).Replace("_", "__")}", InputGestureText = ShortDir(p), ToolTip = p };
                var path = p;
                mi.Click += (_, _) => OpenPath(path);
                recent.Items.Add(mi); i++;
            }
        }
        if (_settings.RecentFolders.Count > 0)
        {
            recent.Items.Add(new Separator());
            recent.Items.Add(new MenuItem { Header = Strings.Get("RecentFolders"), IsEnabled = false, FontSize = 11 });
            foreach (var d in _settings.RecentFolders.ToList())
            {
                var mi = new MenuItem { Header = FolderLabel(d).Replace("_", "__"), InputGestureText = ShortDir(d), ToolTip = d, Icon = Glyph("Files") };
                var dir = d;
                mi.Click += (_, _) => OpenFolder(dir);
                recent.Items.Add(mi);
            }
        }
        if (_settings.Recent.Count > 0 || _settings.RecentFolders.Count > 0)
        {
            recent.Items.Add(new Separator());
            recent.Items.Add(Item("RecentClear", null, () => { _settings.Recent.Clear(); _settings.RecentFolders.Clear(); _settings.Save(); BuildMenu(); return Task.CompletedTask; }));
        }
        file.Items.Add(recent);
        file.Items.Add(Item("CloseDir", null, () => { CloseAllFolders(); return Task.CompletedTask; }, enabled: _roots.Count > 0));
        var startup = new MenuItem { Header = Strings.Get("StartupMenu") };
        foreach (var mi in StartupItems()) startup.Items.Add(mi);
        file.Items.Add(startup);
        file.Items.Add(new Separator());
        file.Items.Add(Item("Save", ctrl + "S", () => RunCommand("save")));
        file.Items.Add(Item("SaveAs", ctrl + "Shift+S", () => RunCommand("saveas")));
        file.Items.Add(Item("AutoSave", null, () => SetAutoSave(!_settings.AutoSave), _settings.AutoSave));
        file.Items.Add(new Separator());
        file.Items.Add(Item("ExportHtml", null, () => RunCommand("export")));
        file.Items.Add(Item("Print", ctrl + "P", () => RunCommand("print")));
        file.Items.Add(Item("OpenFolder", null, () => RunCommand("folder"), enabled: _path != null));
        file.Items.Add(new Separator());
        file.Items.Add(Item("Exit", "Alt+F4", () => RunCommand("exit")));
        MainMenu.Items.Add(file);

        var edit = new MenuItem { Header = Strings.Get("Edit") };
        edit.Items.Add(Item("Undo", ctrl + "Z", () => Exec("undo")));
        edit.Items.Add(Item("Redo", ctrl + "Y", () => Exec("redo")));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("Find", ctrl + "F", () => RunCommand("find")));
        edit.Items.Add(Item("Replace", ctrl + "H", () => RunCommand("replace")));
        edit.Items.Add(Item("FindNext", "F3", () => RunCommand("findnext")));
        MainMenu.Items.Add(edit);

        var fmt = new MenuItem { Header = Strings.Get("Format") };
        fmt.Items.Add(Item("Bold", ctrl + "B", () => Exec("bold")));
        fmt.Items.Add(Item("Italic", ctrl + "I", () => Exec("italic")));
        fmt.Items.Add(Item("Strike", null, () => Exec("strike")));
        fmt.Items.Add(Item("Code", null, () => Exec("code")));
        fmt.Items.Add(new Separator());
        var heading = new MenuItem { Header = Strings.Get("Heading"), Icon = Glyph("Heading") };
        for (int lv = 1; lv <= 6; lv++)
        {
            int level = lv;
            var mi = new MenuItem { Header = $"H{level}", FontSize = 13 + (6 - level) * 1.5, FontWeight = FontWeights.SemiBold };
            mi.Click += async (_, _) => await Exec("heading", $"{{level:{level}}}");
            heading.Items.Add(mi);
        }
        heading.Items.Add(new Separator());
        heading.Items.Add(Item("Paragraph", null, () => Exec("heading", "{level:0}")));
        fmt.Items.Add(heading);
        fmt.Items.Add(Item("Quote", null, () => Exec("blockQuote")));
        fmt.Items.Add(Item("Ul", null, () => Exec("bulletList")));
        fmt.Items.Add(Item("Ol", null, () => Exec("orderedList")));
        fmt.Items.Add(Item("Task", null, () => Exec("taskList")));
        fmt.Items.Add(Item("CodeBlock", null, () => Exec("codeBlock")));
        fmt.Items.Add(Item("Hr", null, () => Exec("hr")));
        MainMenu.Items.Add(fmt);

        var view = new MenuItem { Header = Strings.Get("View") };
        view.Items.Add(Item("ModeVisual", ctrl + "1", () => SetMode("visual"), _settings.Mode == "visual"));
        view.Items.Add(Item("ModeSource", ctrl + "2", () => SetMode("source"), _settings.Mode == "source"));
        view.Items.Add(Item("ModePreview", ctrl + "3", () => SetMode("preview"), _settings.Mode == "preview"));
        view.Items.Add(new Separator());
        view.Items.Add(Item("ReadOnly", ctrl + "Shift+L", () => SetReadOnly(!_settings.ReadOnly), _settings.ReadOnly));
        view.Items.Add(new Separator());
        view.Items.Add(Item("ThemeMenu", null, () => { ShowThemeDialog(); return Task.CompletedTask; }));
        view.Items.Add(new Separator());
        view.Items.Add(Item("ZoomIn", ctrl + "+", () => RunCommand("zoom+")));
        view.Items.Add(Item("ZoomOut", ctrl + "-", () => RunCommand("zoom-")));
        view.Items.Add(Item("ZoomReset", ctrl + "0", () => RunCommand("zoom0")));
        view.Items.Add(new Separator());
        view.Items.Add(Item("Files", ctrl + "Shift+E", () => { ToggleFiles(); return Task.CompletedTask; }, _settings.FilesPanel));
        view.Items.Add(Item("Outline", ctrl + "Shift+O", () => { ToggleOutline(); return Task.CompletedTask; }, _settings.Outline));
        view.Items.Add(Item("Spell", null, async () =>
        {
            _settings.Spell = !_settings.Spell; _settings.Save();
            if (_ready) await Js($"mdApp.setSpell({(_settings.Spell ? "true" : "false")})");
            BuildMenu();
        }, _settings.Spell));
        MainMenu.Items.Add(view);

        var lang = new MenuItem { Header = Strings.Get("Language") };
        foreach (var (code, name) in new[] { ("en", "English"), ("es", "Español") })
        {
            var mi = new MenuItem { Header = name, IsCheckable = true, IsChecked = _settings.Lang == code };
            mi.Click += async (_, _) => await SetLanguage(code);
            lang.Items.Add(mi);
        }
        MainMenu.Items.Add(lang);

        var help = new MenuItem { Header = Strings.Get("Help") };
        help.Items.Add(Item("Manual", "F1", () => { OpenManual(); return Task.CompletedTask; }));
        help.Items.Add(Item("Sample", null, OpenSampleAsync, enabled: SamplePath != null));
        help.Items.Add(Item("SampleInRecent", null, () => { SetSampleHidden(!_settings.SampleHidden); return Task.CompletedTask; },
            check: !_settings.SampleHidden, enabled: SamplePath != null));
        help.Items.Add(Item("Guide", null, () =>
        {
            OpenLink(Strings.Lang == "es"
                ? "https://docs.github.com/es/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax"
                : "https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax");
            return Task.CompletedTask;
        }));
        help.Items.Add(Item("CheckUpdates", null, () => CheckUpdatesAsync(true)));
        help.Items.Add(new Separator());
        help.Items.Add(Item("About", null, () =>
        {
            new AboutDialog(this, Version).ShowDialog();
            return Task.CompletedTask;
        }));
        MainMenu.Items.Add(help);

        BuildToolbar();
        InputBindings.Clear();
        var f1 = new KeyBinding(new RelayCommand(OpenManual), Key.F1, ModifierKeys.None);
        InputBindings.Add(f1);
    }

    // ------------------------------------------------------------------ Native toolbar
    private bool _segWired;

    private Button Tool(string glyph, string tipKey, string? gesture, Func<Task> action)
    {
        var b = new Button
        {
            Content = glyph,
            Style = (Style)FindResource("Gm.ToolButton"),
            ToolTip = Strings.Plain(tipKey) + (gesture != null ? "   " + gesture : ""),
        };
        b.Click += async (_, _) => { await action(); if (_ready) await Js("mdApp.focus()"); };
        return b;
    }

    private void BuildToolbar()
    {
        Tools.Children.Clear();
        void Sep() => Tools.Children.Add(new Border { Style = (Style)FindResource("Gm.ToolSep") });
        Tools.Children.Add(Tool("\uE8B7", "Files", "Ctrl+Shift+E", () => { ToggleFiles(); return Task.CompletedTask; }));
        Tools.Children.Add(Tool("", "Outline", "Ctrl+Shift+O", () => { ToggleOutline(); return Task.CompletedTask; }));
        Sep();

        var h = Tool("", "Heading", null, () => Task.CompletedTask);
        h.Click += (_, _) =>
        {
            // from the ⋯ menu the button is hidden: open at the mouse instead
            var cm = new ContextMenu { PlacementTarget = h, Placement = h.IsVisible ? System.Windows.Controls.Primitives.PlacementMode.Bottom : System.Windows.Controls.Primitives.PlacementMode.MousePoint };
            for (int lv = 1; lv <= 6; lv++)
            {
                int level = lv;
                var mi = new MenuItem { Header = $"H{level}  " + Strings.Plain("Heading") + $" {level}", FontSize = 15 - level * 0.5, FontWeight = level <= 3 ? FontWeights.SemiBold : FontWeights.Normal };
                mi.Click += async (_, _) => { await Exec("heading", $"{{level:{level}}}"); await Js("mdApp.focus()"); };
                cm.Items.Add(mi);
            }
            cm.Items.Add(new Separator());
            var p = new MenuItem { Header = Strings.Plain("Paragraph") };
            p.Click += async (_, _) => { await Exec("heading", "{level:0}"); await Js("mdApp.focus()"); };
            cm.Items.Add(p);
            cm.IsOpen = true;
        };
        Tools.Children.Add(h);
        Tools.Children.Add(Tool("", "Bold", "Ctrl+B", () => Exec("bold")));
        Tools.Children.Add(Tool("", "Italic", "Ctrl+I", () => Exec("italic")));
        Tools.Children.Add(Tool("", "Strike", null, () => Exec("strike")));
        Sep();
        Tools.Children.Add(Tool("", "Quote", null, () => Exec("blockQuote")));
        Tools.Children.Add(Tool("", "Hr", null, () => Exec("hr")));
        Sep();
        Tools.Children.Add(Tool("", "Ul", null, () => Exec("bulletList")));
        Tools.Children.Add(Tool("", "Ol", null, () => Exec("orderedList")));
        Tools.Children.Add(Tool("", "Task", null, () => Exec("taskList")));
        Tools.Children.Add(Tool("", "Outdent", null, () => Exec("outdent")));
        Tools.Children.Add(Tool("", "Indent", null, () => Exec("indent")));
        Sep();
        Tools.Children.Add(Tool("", "Table", null, InsertTableAsync));
        Tools.Children.Add(Tool("", "Image", null, InsertImageAsync));
        Tools.Children.Add(Tool("", "Link", null, InsertLinkAsync));
        Sep();
        Tools.Children.Add(Tool("", "Code", null, () => Exec("code")));
        Tools.Children.Add(Tool("", "CodeBlock", null, () => Exec("codeBlock")));

        SegVisual.Content = Strings.Get("SegVisual");
        SegSource.Content = Strings.Get("SegSource");
        SegPreview.Content = Strings.Get("SegPreview");
        SegVisual.ToolTip = SegTip("ModeVisual", "TipVisual", "Ctrl+1");
        SegSource.ToolTip = SegTip("ModeSource", "TipSource", "Ctrl+2");
        SegPreview.ToolTip = SegTip("ModePreview", "TipPreview", "Ctrl+3");
        if (ZoomSlider != null && ToolsMore.ToolTip != null) LocalizeFit();
        Dispatcher.BeginInvoke(FitToolbar);
        AutoSaveBtn.Content = Strings.Plain("AutoSave");
        AutoSaveBtn.ToolTip = SegTip("AutoSave", "TipAutoSave", "");
        AutoSaveBtn.IsChecked = _settings.AutoSave;
        SegEdit.Content = SegLabel("\uE70F", "SegEdit");
        SegRead.Content = SegLabel("\uE72E", "SegRead");
        SegEdit.ToolTip = SegTip("SegEdit", "TipEdit", "Ctrl+Shift+L");
        SegRead.ToolTip = SegTip("SegRead", "TipRead", "Ctrl+Shift+L");
        SyncSegments();
        if (!_segWired)
        {
            _segWired = true;
            SegVisual.Checked += async (_, _) => { if (_settings.Mode != "visual") await SetMode("visual"); };
            SegSource.Checked += async (_, _) => { if (_settings.Mode != "source") await SetMode("source"); };
            SegPreview.Checked += async (_, _) => { if (_settings.Mode != "preview") await SetMode("preview"); };
            SegEdit.Checked += async (_, _) => { if (_settings.ReadOnly) await SetReadOnly(false); };
            SegRead.Checked += async (_, _) => { if (!_settings.ReadOnly) await SetReadOnly(true); };
            // Checked/Unchecked, not Click: the switch also turns by keyboard and by accessibility tools
            AutoSaveBtn.Checked += async (_, _) => { if (!_settings.AutoSave) await SetAutoSave(true); };
            AutoSaveBtn.Unchecked += async (_, _) => { if (_settings.AutoSave) await SetAutoSave(false); };
        }
    }

    private static object SegLabel(string glyph, string key)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(new TextBlock { Text = Strings.Get(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        return p;
    }

    /// <summary>Two-line tooltip for the mode buttons: name + shortcut in bold, then what the mode is for.</summary>
    internal static object SegTip(string nameKey, string tipKey, string gesture)
    {
        var p = new StackPanel { MaxWidth = 380 };
        p.Children.Add(new TextBlock { Text = (Strings.Plain(nameKey) + "   " + gesture).TrimEnd(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        p.Children.Add(new TextBlock { Text = Strings.Get(tipKey), TextWrapping = TextWrapping.Wrap });
        return p;
    }

    private void SyncSegments()
    {
        SegVisual.IsChecked = _settings.Mode == "visual";
        SegSource.IsChecked = _settings.Mode == "source";
        SegPreview.IsChecked = _settings.Mode == "preview";
        SegEdit.IsChecked = !_settings.ReadOnly;
        SegRead.IsChecked = _settings.ReadOnly;
        foreach (var (seg, on) in new[] { (SegEdit, !_settings.ReadOnly), (SegRead, _settings.ReadOnly) })
            if (seg.Content is StackPanel sp && sp.Children.Count == 2) sp.Children[1].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Tools.IsEnabled = _settings.Mode != "preview" && !_settings.ReadOnly;
    }

    private async Task<string> SelectedTextAsync() =>
        _ready ? JsonSerializer.Deserialize<string>(await Js("mdApp.selectedText()")) ?? "" : "";

    private async Task InsertLinkAsync()
    {
        if (!_ready) return;
        var sel = await SelectedTextAsync();
        var dlg = new InputDialog(this, Strings.Plain("Link"), new[] { (Strings.Get("LinkText"), sel), (Strings.Get("LinkUrl"), "https://") });
        if (dlg.ShowDialog() != true) return;
        var v = dlg.Values;
        if (v[1].Length == 0) return;
        await Exec("addLink", JsonSerializer.Serialize(new { linkUrl = v[1], linkText = v[0].Length > 0 ? v[0] : v[1] }));
    }

    private async Task InsertImageAsync()
    {
        if (!_ready) return;
        var dlg = new InputDialog(this, Strings.Plain("Image"), new[] { (Strings.Get("ImageUrl"), ""), (Strings.Get("ImageAlt"), "") },
            (d, i) =>
            {
                if (i != 0) return null;
                var b = new Button { Content = Strings.Get("Browse"), Style = (Style)FindResource("Gm.DialogButton") };
                b.Click += (_, _) =>
                {
                    var ofd = new OpenFileDialog { Filter = Strings.Get("ImgFilter"), InitialDirectory = _path != null ? Path.GetDirectoryName(_path) : null };
                    if (ofd.ShowDialog(d) != true) return;
                    var f = ofd.FileName;
                    if (_path != null)
                    {
                        var rel = Path.GetRelativePath(Path.GetDirectoryName(_path)!, f);
                        if (!rel.StartsWith("..") && !Path.IsPathRooted(rel)) f = rel;
                    }
                    d.SetField(0, f.Replace('\\', '/').Replace(" ", "%20"));
                    d.SetField(1, Path.GetFileNameWithoutExtension(ofd.FileName));
                };
                return b;
            });
        if (dlg.ShowDialog() != true) return;
        var v = dlg.Values;
        if (v[0].Length == 0) return;
        await Exec("addImage", JsonSerializer.Serialize(new { imageUrl = v[0], altText = v[1] }));
    }

    private async Task InsertTableAsync()
    {
        if (!_ready) return;
        var dlg = new InputDialog(this, Strings.Plain("Table"), new[] { (Strings.Get("Columns"), "3"), (Strings.Get("Rows"), "3") });
        if (dlg.ShowDialog() != true) return;
        var v = dlg.Values;
        int cols = int.TryParse(v[0], out var c) ? Math.Clamp(c, 1, 20) : 3;
        int rows = int.TryParse(v[1], out var r) ? Math.Clamp(r, 1, 100) : 3;
        await Exec("addTable", JsonSerializer.Serialize(new { columnCount = cols, rowCount = rows + 1 }));
    }

    private static string ShortDir(string p)
    {
        var d = Path.GetDirectoryName(p) ?? "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (d.StartsWith(home, StringComparison.OrdinalIgnoreCase)) d = "~" + d[home.Length..];
        return d.Length > 38 ? "…" + d[^37..] : d;
    }

    private void OpenManual()
    {
        var pdf = Path.Combine(AppDir, "docs", Strings.Lang == "es" ? "Gryphmark_Manual_ES.pdf" : "Gryphmark_Manual_EN.pdf");
        if (File.Exists(pdf)) Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true });
    }

    private async Task SetLanguage(string code)
    {
        _settings.Lang = code; Strings.Lang = code; _settings.Save();
        BuildMenu(); UpdateTitle(); UpdateStatus(); LocalizeFeatures();
        // a new spell check language needs a new browser; the document is carried over
        if (_ready && SpellLang(code) != _webSpell) { await RestartWebAsync(); return; }
        if (_ready) await Js($"mdApp.setLang({Q(code)})");
        // the sample follows the interface language (unless it was edited: those changes are kept)
        if (_ready && _isSample && _path == null && !_dirty) await LoadSampleAsync();
    }

    private async Task RestartWebAsync()
    {
        var text = JsonSerializer.Deserialize<string>(await Js("mdApp.getMarkdownPreserving()")) ?? "";
        _restore = (text, _dirty);
        _settings.Zoom = Web.ZoomFactor;
        _ready = false;
        var host = (Panel)Web.Parent;
        int at = host.Children.IndexOf(Web);
        var old = Web;
        host.Children.Remove(old);
        old.Dispose();
        Web = new Microsoft.Web.WebView2.Wpf.WebView2 { DefaultBackgroundColor = old.DefaultBackgroundColor };
        Grid.SetRow(Web, 1);
        host.Children.Insert(at, Web);
        Web.ZoomFactorChanged += (_, _) => SyncZoom();
        await InitWebAsync();
    }

    private async Task RestoreDocAsync(string text, bool dirty)
    {
        if (_isSample && _path == null && !dirty) { await LoadSampleAsync(); return; }
        if (_path != null)
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping("doc.local", Path.GetDirectoryName(_path)!, CoreWebView2HostResourceAccessKind.Allow);
        await Js($"mdApp.load({Q(text)},{(_path != null ? "'https://doc.local/'" : "null")})");
        _dirty = dirty;
        _words = int.TryParse(await Js("mdApp.words()"), out var n) ? n : 0;
        UpdateTitle(); UpdateStatus();
    }

    // ------------------------------------------------------------------ Chrome
    private void UpdateTitle()
    {
        var name = _path != null ? Path.GetFileName(_path) : Strings.Get(_isSample ? "SampleTitle" : "Untitled");
        Title = $"{(_dirty ? "● " : "")}{name} — Gryphmark" + (Settings.Profile != null ? $"  [{Settings.Profile}]" : "");
        RevealCurrent();   // the Files panel follows the open file
    }

    private void UpdateStatus()
    {
        StatusPath.Text = _path ?? Strings.Get(_isSample ? "SampleTitle" : "Untitled");
        StatusWords.Text = string.Format(Strings.Get("Words"), _words);
        StatusMode.Text = Strings.Plain(_settings.Mode switch { "source" => "ModeSource", "preview" => "ModePreview", _ => "ModeVisual" })
            + (_settings.ReadOnly ? " · \U0001F512 " + Strings.Get("SegRead") : "");
        StatusState.Text = _dirty ? Strings.Get("Modified") : (_path != null ? Strings.Get("Saved") : "");
    }
}

public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
