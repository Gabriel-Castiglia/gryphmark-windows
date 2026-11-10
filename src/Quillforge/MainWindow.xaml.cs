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

namespace Quillforge;

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
        _pendingOpen = startPath;
        Width = _settings.Width; Height = _settings.Height;
        if (_settings.Maximized) WindowState = WindowState.Maximized;
        ThemeManager.Apply(this, _settings.Theme == "dark");
        // page colour from the first frame, so there is no white/black flash while the editor loads
        Web.DefaultBackgroundColor = _settings.Theme == "dark" ? System.Drawing.Color.FromArgb(13, 17, 23) : System.Drawing.Color.White;
        BuildMenu();
        UpdateTitle();
        SourceInitialized += (_, _) => ThemeManager.Apply(this, _settings.Theme == "dark");
        Loaded += async (_, _) => await InitWebAsync();
        Closing += OnClosing;
        PreviewKeyDown += OnKey;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) OpenPath(f[0]);
        };
    }

    // ------------------------------------------------------------------ WebView
    private async Task InitWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Dir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception)
        {
            MessageBox.Show(this, Strings.Get("NoWebView"), "Quillforge", MessageBoxButton.OK, MessageBoxImage.Error);
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
                await Js($"mdApp.init({Q(_settings.Lang)},{Q(_settings.Theme)},{Q(_settings.Mode)})");
                _ready = true;
                if (_pendingOpen != null) { var p = _pendingOpen; _pendingOpen = null; await LoadFileAsync(p); }
                else UpdateStatus();
                break;
            case "dirty":
                if (root.TryGetProperty("words", out var w)) _words = w.GetInt32();
                if (!_dirty) { _dirty = true; UpdateTitle(); }
                UpdateStatus();
                break;
            case "cmd":
                await RunCommand(root.GetProperty("cmd").GetString() ?? "");
                break;
            case "link":
                OpenLink(root.GetProperty("href").GetString() ?? "");
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
                if (File.Exists(local)) { Process.Start(new ProcessStartInfo(local) { UseShellExecute = true }); return; }
            }
            if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch { /* nothing sensible to do with a broken link */ }
    }

    private static bool IsMarkdown(string p) =>
        new[] { ".md", ".markdown", ".mdown", ".mkd", ".txt" }.Contains(Path.GetExtension(p).ToLowerInvariant());

    // ------------------------------------------------------------------ Files
    private async void OpenPath(string path)
    {
        if (!_ready) { _pendingOpen = path; return; }
        if (!await ConfirmDiscardAsync()) return;
        await LoadFileAsync(path);
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, string.Format(Strings.Get("NotFound"), path), "Quillforge", MessageBoxButton.OK, MessageBoxImage.Warning);
                _settings.Recent.Remove(path); BuildMenu();
                return;
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            var dir = Path.GetDirectoryName(path)!;
            Web.CoreWebView2.ClearVirtualHostNameToFolderMapping("doc.local");
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping("doc.local", dir, CoreWebView2HostResourceAccessKind.Allow);
            await Js($"mdApp.load({Q(text)},'https://doc.local/')");
            _path = path; _dirty = false;
            _words = int.TryParse(await Js("mdApp.words()"), out var n) ? n : 0;
            _settings.AddRecent(path); _settings.Save();
            BuildMenu(); UpdateTitle(); UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Get("ErrOpen"), ex.Message), "Quillforge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> SaveAsync(bool forceDialog)
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
        try
        {
            var md = JsonSerializer.Deserialize<string>(await Js("mdApp.getMarkdown()")) ?? "";
            if (!md.EndsWith('\n')) md += "\n";
            File.WriteAllText(target!, md, new UTF8Encoding(false));
            bool moved = !string.Equals(target, _path, StringComparison.OrdinalIgnoreCase);
            _path = target; _dirty = false;
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
            MessageBox.Show(this, string.Format(Strings.Get("ErrSave"), ex.Message), "Quillforge", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        var name = _path != null ? Path.GetFileName(_path) : Strings.Get("Untitled");
        var r = MessageBox.Show(this, string.Format(Strings.Get("AskSave"), name), "Quillforge",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return await SaveAsync(false);
        return true;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { _settings.Width = Width; _settings.Height = Height; }
        if (_ready) _settings.Zoom = Web.ZoomFactor;
        _settings.Save();
        if (_closingConfirmed || !_dirty) return;
        e.Cancel = true;
        if (await ConfirmDiscardAsync()) { _closingConfirmed = true; Close(); }
    }

    private async Task NewDocAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        Web.CoreWebView2.ClearVirtualHostNameToFolderMapping("doc.local");
        await Js("mdApp.load('', null)");
        _path = null; _dirty = false; _words = 0;
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
        var title = Path.GetFileNameWithoutExtension(dlg.FileName);
        using var doc = JsonDocument.Parse(await Js($"mdApp.exportHtml({Q(title)})"));
        var r = doc.RootElement;
        bool dark = r.GetProperty("theme").GetString() == "dark";
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
            <meta name="generator" content="Quillforge {{Version}}">
            <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
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
        try { File.WriteAllText(dlg.FileName, html, new UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, string.Format(Strings.Get("ErrSave"), ex.Message), "Quillforge", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // ------------------------------------------------------------------ Commands
    private async Task RunCommand(string cmd)
    {
        if (!_ready && cmd != "exit") return;
        switch (cmd)
        {
            case "new": await NewDocAsync(); break;
            case "newwindow": Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); break;
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
            case "print":
                await Js("mdApp.preparePrint()");
                Web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                break;
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
        ["Manual"] = "", ["Guide"] = "", ["About"] = "", ["RecentClear"] = "",
    };

    private static object? Glyph(string key) => Glyphs.TryGetValue(key, out var g)
        ? new TextBlock { Text = g, FontFamily = (FontFamily)Application.Current.Resources["Qf.Icons"], FontSize = 14 }
        : null;

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
        var recent = new MenuItem { Header = Strings.Get("Recent"), Icon = Glyph("Recent") };
        if (_settings.Recent.Count == 0) recent.Items.Add(new MenuItem { Header = Strings.Get("RecentEmpty"), IsEnabled = false });
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
            recent.Items.Add(new Separator());
            recent.Items.Add(Item("RecentClear", null, () => { _settings.Recent.Clear(); _settings.Save(); BuildMenu(); return Task.CompletedTask; }));
        }
        file.Items.Add(recent);
        file.Items.Add(new Separator());
        file.Items.Add(Item("Save", ctrl + "S", () => RunCommand("save")));
        file.Items.Add(Item("SaveAs", ctrl + "Shift+S", () => RunCommand("saveas")));
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
        view.Items.Add(Item("Dark", null, async () =>
        {
            _settings.Theme = _settings.Theme == "dark" ? "light" : "dark"; _settings.Save();
            ThemeManager.Apply(this, _settings.Theme == "dark");
            if (_ready) await Js($"mdApp.setTheme({Q(_settings.Theme)})");
            BuildMenu();
        }, _settings.Theme == "dark"));
        view.Items.Add(new Separator());
        view.Items.Add(Item("ZoomIn", ctrl + "+", () => RunCommand("zoom+")));
        view.Items.Add(Item("ZoomOut", ctrl + "-", () => RunCommand("zoom-")));
        view.Items.Add(Item("ZoomReset", ctrl + "0", () => RunCommand("zoom0")));
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
        help.Items.Add(Item("Guide", null, () =>
        {
            OpenLink(Strings.Lang == "es"
                ? "https://docs.github.com/es/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax"
                : "https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax");
            return Task.CompletedTask;
        }));
        help.Items.Add(new Separator());
        help.Items.Add(Item("About", null, () =>
        {
            MessageBox.Show(this, string.Format(Strings.Get("AboutText"), Version), Strings.Plain("About"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            Style = (Style)FindResource("Qf.ToolButton"),
            ToolTip = Strings.Plain(tipKey) + (gesture != null ? "   " + gesture : ""),
        };
        b.Click += async (_, _) => { await action(); if (_ready) await Js("mdApp.focus()"); };
        return b;
    }

    private void BuildToolbar()
    {
        Tools.Children.Clear();
        void Sep() => Tools.Children.Add(new Border { Style = (Style)FindResource("Qf.ToolSep") });

        var h = Tool("", "Heading", null, () => Task.CompletedTask);
        h.Click += (_, _) =>
        {
            var cm = new ContextMenu { PlacementTarget = h, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
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
        SegVisual.ToolTip = Strings.Plain("ModeVisual") + "   Ctrl+1";
        SegSource.ToolTip = Strings.Plain("ModeSource") + "   Ctrl+2";
        SegPreview.ToolTip = Strings.Plain("ModePreview") + "   Ctrl+3";
        SyncSegments();
        if (!_segWired)
        {
            _segWired = true;
            SegVisual.Checked += async (_, _) => { if (_settings.Mode != "visual") await SetMode("visual"); };
            SegSource.Checked += async (_, _) => { if (_settings.Mode != "source") await SetMode("source"); };
            SegPreview.Checked += async (_, _) => { if (_settings.Mode != "preview") await SetMode("preview"); };
        }
    }

    private void SyncSegments()
    {
        SegVisual.IsChecked = _settings.Mode == "visual";
        SegSource.IsChecked = _settings.Mode == "source";
        SegPreview.IsChecked = _settings.Mode == "preview";
        Tools.IsEnabled = _settings.Mode != "preview";
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
                var b = new Button { Content = Strings.Get("Browse"), Style = (Style)FindResource("Qf.DialogButton") };
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
        var pdf = Path.Combine(AppDir, "docs", Strings.Lang == "es" ? "Quillforge_Manual_ES.pdf" : "Quillforge_Manual_EN.pdf");
        if (File.Exists(pdf)) Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true });
    }

    private async Task SetLanguage(string code)
    {
        _settings.Lang = code; Strings.Lang = code; _settings.Save();
        if (_ready) await Js($"mdApp.setLang({Q(code)})");
        BuildMenu(); UpdateTitle(); UpdateStatus();
    }

    // ------------------------------------------------------------------ Chrome
    private void UpdateTitle()
    {
        var name = _path != null ? Path.GetFileName(_path) : Strings.Get("Untitled");
        Title = $"{(_dirty ? "● " : "")}{name} — Quillforge";
    }

    private void UpdateStatus()
    {
        StatusPath.Text = _path ?? Strings.Get("Untitled");
        StatusWords.Text = string.Format(Strings.Get("Words"), _words);
        StatusMode.Text = Strings.Plain(_settings.Mode switch { "source" => "ModeSource", "preview" => "ModePreview", _ => "ModeVisual" });
        StatusState.Text = _dirty ? Strings.Get("Modified") : (_path != null ? Strings.Get("Saved") : "");
    }
}

public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
