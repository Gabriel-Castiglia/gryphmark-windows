using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gryphmark;

/// <summary>Files panel: one tree per open folder (subfolders and Markdown files), like the explorer of
/// VS Code or Obsidian. Each folder is a block with its own pastel colour; a click opens the file beside it.
/// The trees follow the disk: files and folders that appear, are renamed or are deleted (also a whole open
/// folder, or a network drive that goes away and comes back) are reflected without breaking anything.</summary>
public partial class MainWindow
{
    private static readonly HashSet<string> MdExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".mdown", ".mkd" };
    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".vscode", ".idea", "__pycache__", "$RECYCLE.BIN", "System Volume Information" };
    private const string Placeholder = "…";
    private static readonly StringComparer PathCmp = StringComparer.OrdinalIgnoreCase;

    private readonly List<string> _roots = new();                    // open folders, in the order they were opened
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);   // open folders not reachable now
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private string? _revealedPath;
    private string? _expandNext;                                     // folder just added: open it after the rebuild
    private bool _folderInit;
    private readonly DispatcherTimer _folderRefresh = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _folderProbe = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _probing;

    /// <summary>Called from the constructor: wires the panel and reopens the folders of the last session
    /// (plus the one passed on the command line).</summary>
    private void InitFolder(string? startFolder)
    {
        _folderRefresh.Tick += (_, _) => { _folderRefresh.Stop(); RefreshTree(); };
        _folderProbe.Tick += (_, _) => ProbeRoots();
        FilesTree.PreviewMouseLeftButtonUp += OnTreeClick;
        FilesTree.PreviewKeyDown += OnTreeKey;
        FilesOpenBtn.Click += (_, _) => PickFolder();
        FilesAdd.Click += (_, _) => PickFolder();
        FilesRefresh.Click += (_, _) => RefreshTree();
        FilesCollapse.Click += (_, _) => CollapseAllTrees();
        FilesOptions.Click += (_, _) => { var m = OptionsMenu(); m.PlacementTarget = FilesOptions; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
        FilesSplitter.DragCompleted += (_, _) =>
        {
            if (FilesColumn.ActualWidth > 0) { _settings.FilesWidth = Math.Round(FilesColumn.ActualWidth); _settings.Save(); }
        };
        // 1.4 and earlier kept a single folder
        if (_settings.Folders.Count == 0 && _settings.Folder != null) _settings.Folders.Add(_settings.Folder);
        _settings.Folder = null;
        // what opens at start: the folders of the last session, nothing, or only the pinned ones
        _settings.PinnedFolders.RemoveAll(string.IsNullOrWhiteSpace);
        var start = _settings.StartupFolders switch
        {
            "clean" => new List<string>(),
            "pinned" => _settings.PinnedFolders.ToList(),
            _ => _settings.Folders.Concat(_settings.PinnedFolders).ToList(),
        };
        foreach (var d in start)
            if (!_roots.Contains(d, PathCmp)) _roots.Add(d);
        if (_settings.StartupFolders != "restore") SaveRoots();
        LocalizeFolder();
        if (startFolder != null) OpenFolder(startFolder, showPanel: true);
        else { RefreshTree(); ApplyFilesVisibility(); }
        _folderProbe.Start();
        _folderInit = true;
    }

    /// <summary>Self-test (--render-chrome): show the panel empty (no dirs) or with folders and one file selected.</summary>
    internal void TestFolder(string? select, params string[] dirs)
    {
        _settings.FilesPanel = true;
        LocalizeFolder();
        CloseAllFolders();
        foreach (var d in dirs) OpenFolder(d);
        if (select != null) { _path = select; _revealedPath = null; RevealCurrent(); _path = null; }
        ApplyFilesVisibility();
    }

    private void LocalizeFolder()
    {
        FilesEmpty.Text = Strings.Get("FilesEmpty");
        FilesOpenBtn.Content = Strings.Get("FilesOpenBtn");
        FilesAdd.ToolTip = Strings.Get("FolderAdd");
        FilesOptions.ToolTip = Strings.Get("FolderOptions");
        FilesRefresh.ToolTip = Strings.Get("Refresh");
        FilesCollapse.ToolTip = Strings.Get("CollapseAll");
        FilesTitle.Text = Strings.Get(_roots.Count > 0 ? "RecentFolders" : "FilesTitle");
        if (_roots.Count > 0) RefreshTree();   // context menus are built with the interface language
    }

    private static string FolderLabel(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? dir : name;
    }

    private void SaveRoots() { _settings.Folders = _roots.ToList(); _settings.Save(); }

    // ------------------------------------------------------------------ open / close
    private void PickFolder()
    {
        string? start = _roots.LastOrDefault(d => !_missing.Contains(d)) ?? (_path != null ? Path.GetDirectoryName(_path) : null);
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = start };
        try { if (dlg.ShowDialog(this) == true) OpenFolder(dlg.FolderName); } catch { }
    }

    /// <summary>Adds a folder as one more tree (at the end, in opening order). Opening one that is already
    /// there just shows it.</summary>
    private void OpenFolder(string dir, bool showPanel = true)
    {
        try { dir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar); if (dir.EndsWith(':')) dir += Path.DirectorySeparatorChar; }
        catch { return; }
        if (!SafeDirExists(dir))
        {
            MessageBox.Show(this, string.Format(Strings.Get("FolderMissing"), dir), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Warning);
            _settings.RecentFolders.RemoveAll(p => PathCmp.Equals(p, dir)); _settings.Save(); BuildMenu();
            return;
        }
        if (!_roots.Contains(dir, PathCmp)) { _roots.Add(dir); SaveRoots(); }
        _settings.AddRecentFolder(dir);
        if (showPanel) _settings.FilesPanel = true;
        _settings.Save();
        _expandNext = _roots.First(r => PathCmp.Equals(r, dir));
        RefreshTree();
        ApplyFilesVisibility();
        BuildMenu();
    }

    private bool IsPinned(string dir) => _settings.PinnedFolders.Contains(dir, PathCmp);

    private void TogglePin(string dir)
    {
        if (IsPinned(dir)) _settings.PinnedFolders.RemoveAll(p => PathCmp.Equals(p, dir));
        else _settings.PinnedFolders.Add(dir);
        _settings.Save();
        RefreshTree();
    }

    private void CloseRoot(string dir)
    {
        _roots.RemoveAll(r => PathCmp.Equals(r, dir));
        _settings.PinnedFolders.RemoveAll(p => PathCmp.Equals(p, dir));   // closing a pinned folder unpins it
        _settings.FolderColorMap.Remove(dir);
        StopWatching(dir);
        _missing.Remove(dir);
        SaveRoots();
        RefreshTree();
        ApplyFilesVisibility();
        BuildMenu();
    }

    private void CloseAllFolders()
    {
        foreach (var d in _roots.ToList()) StopWatching(d);
        _roots.Clear(); _missing.Clear();
        SaveRoots();
        FilesTree.Items.Clear();
        FilesTitle.Text = Strings.Get("FilesTitle");
        ApplyFilesVisibility();
        BuildMenu();
    }

    private void MoveRoot(string dir, int delta)
    {
        int i = _roots.FindIndex(r => PathCmp.Equals(r, dir)), j = i + delta;
        if (i < 0 || j < 0 || j >= _roots.Count) return;
        (_roots[i], _roots[j]) = (_roots[j], _roots[i]);
        _settings.FolderSort = "opened";
        SaveRoots();
        RefreshTree();
    }

    private void ToggleFiles()
    {
        _settings.FilesPanel = !_settings.FilesPanel; _settings.Save();
        ApplyFilesVisibility(); BuildMenu();
    }

    private void ApplyFilesVisibility()
    {
        bool show = _settings.FilesPanel;
        FilesColumn.MinWidth = show ? 160 : 0;
        FilesColumn.Width = new GridLength(show ? Math.Clamp(_settings.FilesWidth, 160, 600) : 0);
        SplitterColumn.Width = new GridLength(show ? 5 : 0);
        FilesPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FilesSplitter.Visibility = FilesPane.Visibility;
        bool open = _roots.Count > 0;
        FilesEmptyPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        FilesTree.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FilesHeaderButtons.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FilesTitle.Text = Strings.Get(open ? "RecentFolders" : "FilesTitle");
    }

    // ------------------------------------------------------------------ order and colours
    /// <summary>Pinned folders first (in the order they were pinned), then the rest by the chosen order.</summary>
    private IEnumerable<string> OrderedRoots()
    {
        var pinned = _settings.PinnedFolders.Where(p => _roots.Contains(p, PathCmp)).Select(p => _roots.First(r => PathCmp.Equals(r, p))).ToList();
        return pinned.Concat(SortedRoots(_roots.Where(r => !pinned.Contains(r, PathCmp)).ToList()));
    }

    private IEnumerable<string> SortedRoots(List<string> roots) => _settings.FolderSort switch
    {
        "az" => roots.OrderBy(FolderLabel, StringComparer.CurrentCultureIgnoreCase),
        "za" => roots.OrderByDescending(FolderLabel, StringComparer.CurrentCultureIgnoreCase),
        "modified" => roots.OrderByDescending(d => { try { return _missing.Contains(d) ? DateTime.MinValue : Directory.GetLastWriteTime(d); } catch { return DateTime.MinValue; } }),
        _ => roots,
    };

    /// <summary>Hue (degrees) of an open folder: chosen by hand, or tones of the theme colour, or spread
    /// around the colour wheel (multicolour). The position in opening order keeps it stable when sorting.</summary>
    private double RootHue(string dir, out bool grey)
    {
        grey = false;
        if (_settings.FolderColorMap.TryGetValue(dir, out var h) && double.TryParse(h, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var manual)) return manual;
        int i = Math.Max(0, _roots.FindIndex(r => PathCmp.Equals(r, dir)));
        var (ah, asat, _) = ToHsl((Color)ColorConverter.ConvertFromString(Accents.Get(_settings.Accent).Light));
        if (_settings.FolderColors == "multi") return (ah + i * 137.508) % 360;
        if (asat < 0.15) grey = true;   // black / white themes: shades of grey
        double[] steps = { 0, 22, -22, 44, -44, 66, -66, 11, -11, 33, -33 };
        return (ah + steps[i % steps.Length] + 360) % 360;
    }

    private (Brush Fill, Brush Edge) RootBrushes(string dir)
    {
        if (_settings.FolderColors == "none") return (Brushes.Transparent, Brushes.Transparent);
        bool dark = ChromeIsDark();
        double hue = RootHue(dir, out bool grey);
        int i = Math.Max(0, _roots.FindIndex(r => PathCmp.Equals(r, dir)));
        double s = grey ? 0 : 1;
        Color fill = dark ? FromHsl(hue, 0.30 * s, grey ? 0.17 + (i % 4) * 0.025 : 0.20) : FromHsl(hue, 0.65 * s, grey ? 0.95 - (i % 4) * 0.02 : 0.94);
        Color edge = dark ? FromHsl(hue, 0.55 * s, grey ? 0.55 : 0.62) : FromHsl(hue, 0.55 * s, grey ? 0.55 : 0.58);
        return (new SolidColorBrush(fill), new SolidColorBrush(edge));
    }

    /// <summary>The chrome as it is painted now (the theme applied, which "Automatic" resolves).</summary>
    private static bool ChromeIsDark() =>
        Application.Current.Resources["Gm.Chrome"] is SolidColorBrush b && ToHsl(b.Color).L < 0.5;

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    // ------------------------------------------------------------------ tree
    /// <summary>Rebuilds every tree keeping the open folders open and the current file selected.
    /// Never throws: an unreachable folder is shown as such and checked again later.</summary>
    private void RefreshTree()
    {
        try
        {
            var expanded = new HashSet<string>(PathCmp);
            var known = new HashSet<string>(PathCmp);
            void Collect(ItemCollection items)
            {
                foreach (var i in items.OfType<TreeViewItem>())
                {
                    if (i.Tag is not string p) continue;
                    known.Add(p);
                    if (i.IsExpanded) { expanded.Add(p); Collect(i.Items); }
                }
            }
            Collect(FilesTree.Items);
            FilesTree.Items.Clear();
            foreach (var root in OrderedRoots().ToList())
            {
                bool ok = SafeDirExists(root);
                if (ok) { _missing.Remove(root); WatchRoot(root); } else { _missing.Add(root); StopWatching(root); }
                var node = MakeRoot(root, ok);
                FilesTree.Items.Add(node);
                bool open = ok && (expanded.Contains(root) || !known.Contains(root) || PathCmp.Equals(root, _expandNext));
                if (open) { node.IsExpanded = true; Restore(node.Items, expanded); }
            }
            _expandNext = null;
            _revealedPath = null;
            RevealCurrent();
        }
        catch { }
    }

    private static void Restore(ItemCollection items, HashSet<string> expanded)
    {
        foreach (var i in items.OfType<TreeViewItem>())
            if (i.Tag is string p && expanded.Contains(p)) { i.IsExpanded = true; Restore(i.Items, expanded); }
    }

    private TreeViewItem MakeRoot(string dir, bool available)
    {
        var (fill, edge) = RootBrushes(dir);
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        if (_settings.FolderColors == "none") icon.SetResourceReference(TextBlock.ForegroundProperty, "Gm.FolderIcon"); else icon.Foreground = edge;
        var name = new TextBlock { Text = FolderLabel(dir).ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        if (IsPinned(dir))
        {
            var pin = new TextBlock { Text = "\uE718", FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 11, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Strings.Get("FolderPinnedTip") };
            pin.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
            DockPanel.SetDock(pin, Dock.Right);
            header.Children.Add(pin);
        }
        if (!available)
        {
            var note = new TextBlock { Text = "  " + Strings.Get("FolderUnavailable"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
            DockPanel.SetDock(note, Dock.Right);
            header.Children.Add(note);
            name.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
        }
        header.Children.Add(name);
        var item = new TreeViewItem { Header = header, Tag = dir, ToolTip = dir, Style = (Style)FindResource("Gm.FilesRoot"), Background = fill, BorderBrush = edge };
        ToolTipService.SetInitialShowDelay(item, 900);
        if (available) { item.Items.Add(Placeholder); item.Expanded += OnNodeExpanded; }
        item.ContextMenu = RootMenu(dir);
        return item;
    }

    /// <summary>One level: subfolders first, then Markdown files, each sorted by name. Hidden and
    /// system entries and tool folders (.git, node_modules, bin, obj…) are left out.</summary>
    private void Populate(ItemCollection items, string dir)
    {
        List<string> dirs, files;
        try
        {
            var info = new DirectoryInfo(dir);
            dirs = info.EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !SkippedDirs.Contains(d.Name))
                .Select(d => d.FullName).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
            files = info.EnumerateFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && MdExtensions.Contains(f.Extension))
                .Select(f => f.FullName).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch { return; }   // gone or no access: show it empty
        foreach (var d in dirs) items.Add(MakeNode(d, true));
        foreach (var f in files) items.Add(MakeNode(f, false));
    }

    private TreeViewItem MakeNode(string path, bool isDir)
    {
        var icon = new TextBlock
        {
            Text = isDir ? "" : "", FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 13,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, isDir ? "Gm.FolderIcon" : "Gm.TextMuted");
        var name = new TextBlock { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon); header.Children.Add(name);

        // the style is set here: ItemContainerStyle only reaches items generated by the TreeView itself
        var item = new TreeViewItem { Header = header, Tag = path, ToolTip = path, Style = (Style)FindResource("Gm.FilesItem") };
        ToolTipService.SetInitialShowDelay(item, 900);
        if (isDir)
        {
            item.Items.Add(Placeholder);   // filled when first expanded
            item.Expanded += OnNodeExpanded;
        }
        item.ContextMenu = isDir ? FolderMenu(path) : FileMenu(path);
        return item;
    }

    private void OnNodeExpanded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender) || sender is not TreeViewItem item) return;
        if (item.Items.Count == 1 && item.Items[0] is string)
        {
            item.Items.Clear();
            Populate(item.Items, (string)item.Tag);
        }
    }

    private void CollapseAllTrees() { foreach (var i in FilesTree.Items.OfType<TreeViewItem>()) CollapseAll(i); }

    private static void CollapseAll(TreeViewItem item)
    {
        foreach (var c in item.Items.OfType<TreeViewItem>()) CollapseAll(c);
        item.IsExpanded = false;
    }

    private static T? Ancestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>A click on a folder row opens or closes it; on a file row it opens the file.</summary>
    private void OnTreeClick(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (Ancestor<ToggleButton>(src) != null) return;   // the chevron toggles by itself
        if (Ancestor<TreeViewItem>(src) is not { Tag: string path } item) return;
        Activate(item, path);
    }

    private void OnTreeKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || FilesTree.SelectedItem is not TreeViewItem { Tag: string path } item) return;
        e.Handled = true;
        Activate(item, path);
    }

    private void Activate(TreeViewItem item, string path)
    {
        if (_roots.Contains(path, PathCmp) && _missing.Contains(path)) { Flash(string.Format(Strings.Get("FolderMissing"), path).Replace('\n', ' ')); return; }
        if (SafeDirExists(path)) { item.IsExpanded = !item.IsExpanded; return; }
        if (!File.Exists(path)) { Flash(Strings.Get("ItemGone")); RefreshTree(); return; }   // deleted meanwhile
        if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
        OpenPath(path);
    }

    // ------------------------------------------------------------------ menus
    private MenuItem MenuEntry(string text, Action run, bool? check = null, bool enabled = true)
    {
        var mi = new MenuItem { Header = text, IsEnabled = enabled };
        if (check.HasValue) { mi.IsCheckable = true; mi.IsChecked = check.Value; }
        mi.Click += (_, _) => { try { run(); } catch { } };
        return mi;
    }

    private ContextMenu FileMenu(string path) => new()
    {
        Items =
        {
            MenuEntry(Strings.Plain("FileOpen"), () => { if (!File.Exists(path)) { Flash(Strings.Get("ItemGone")); RefreshTree(); } else if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) OpenPath(path); }),
            MenuEntry(Strings.Get("OpenNewWindow"), () => StartSelf("--new-window", path)),
            new Separator(),
            MenuEntry(Strings.Plain("OpenFolder"), () => ShowInExplorer(path, true)),
            MenuEntry(Strings.Get("CopyPath"), () => Clipboard.SetText(path)),
        },
    };

    private ContextMenu FolderMenu(string path) => new()
    {
        Items =
        {
            MenuEntry(Strings.Plain("OpenFolder"), () => ShowInExplorer(path, false)),
            MenuEntry(Strings.Get("CopyPath"), () => Clipboard.SetText(path)),
            new Separator(),
            MenuEntry(Strings.Get("Refresh"), RefreshTree),
        },
    };

    private ContextMenu RootMenu(string dir)
    {
        bool manual = _settings.FolderSort == "opened";
        int i = _roots.FindIndex(r => PathCmp.Equals(r, dir));
        var color = new MenuItem { Header = Strings.Get("FolderColor"), IsEnabled = _settings.FolderColors != "none" };
        color.Items.Add(MenuEntry(Strings.Get("FolderColorAuto"), () => { _settings.FolderColorMap.Remove(dir); _settings.Save(); RefreshTree(); }, !_settings.FolderColorMap.ContainsKey(dir)));
        color.Items.Add(new Separator());
        string[] en = { "Red", "Orange", "Yellow", "Lime", "Green", "Mint", "Cyan", "Sky", "Blue", "Violet", "Purple", "Pink" };
        string[] es = { "Rojo", "Naranja", "Amarillo", "Lima", "Verde", "Menta", "Cian", "Celeste", "Azul", "Violeta", "Morado", "Rosa" };
        for (int k = 0; k < 12; k++)
        {
            double hue = k * 30;
            string key = hue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var mi = MenuEntry(Strings.Lang == "es" ? es[k] : en[k], () => { _settings.FolderColorMap[dir] = key; _settings.Save(); RefreshTree(); },
                               _settings.FolderColorMap.TryGetValue(dir, out var cur) && cur == key);
            mi.Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(FromHsl(hue, 0.6, ChromeIsDark() ? 0.45 : 0.75)) };
            color.Items.Add(mi);
        }
        return new ContextMenu
        {
            Items =
            {
                MenuEntry(Strings.Get(IsPinned(dir) ? "FolderUnpin" : "FolderPin"), () => TogglePin(dir)),
                MenuEntry(Strings.Get("FolderCloseOne"), () => CloseRoot(dir)),
                color,
                MenuEntry(Strings.Get("FolderUp"), () => MoveRoot(dir, -1), enabled: manual && i > 0),
                MenuEntry(Strings.Get("FolderDown"), () => MoveRoot(dir, +1), enabled: manual && i >= 0 && i < _roots.Count - 1),
                new Separator(),
                MenuEntry(Strings.Plain("OpenFolder"), () => ShowInExplorer(dir, false), enabled: !_missing.Contains(dir)),
                MenuEntry(Strings.Get("CopyPath"), () => Clipboard.SetText(dir)),
                MenuEntry(Strings.Get("Refresh"), RefreshTree),
            },
        };
    }

    /// <summary>The ⋯ button of the panel: order of the folders, colours, and folder-wide actions.</summary>
    private ContextMenu OptionsMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(new MenuItem { Header = Strings.Get("FolderSortTitle"), IsEnabled = false, FontSize = 11 });
        foreach (var (key, label) in new[] { ("opened", "FolderSortOpened"), ("az", "FolderSortAz"), ("za", "FolderSortZa"), ("modified", "FolderSortModified") })
            m.Items.Add(MenuEntry(Strings.Get(label), () => { _settings.FolderSort = key; _settings.Save(); RefreshTree(); }, _settings.FolderSort == key));
        m.Items.Add(new Separator());
        m.Items.Add(new MenuItem { Header = Strings.Get("FolderColorsTitle"), IsEnabled = false, FontSize = 11 });
        foreach (var (key, label) in new[] { ("theme", "FolderColorsTheme"), ("multi", "FolderColorsMulti"), ("none", "FolderColorsNone") })
            m.Items.Add(MenuEntry(Strings.Get(label), () => { _settings.FolderColors = key; _settings.Save(); RefreshTree(); }, _settings.FolderColors == key));
        m.Items.Add(new Separator());
        m.Items.Add(new MenuItem { Header = Strings.Get("StartupTitle"), IsEnabled = false, FontSize = 11 });
        foreach (var mi in StartupItems()) m.Items.Add(mi);
        m.Items.Add(new Separator());
        m.Items.Add(MenuEntry(Strings.Get("FolderAdd"), PickFolder));
        m.Items.Add(MenuEntry(Strings.Get("CollapseAll"), CollapseAllTrees));
        m.Items.Add(MenuEntry(Strings.Get("FolderCloseAll"), CloseAllFolders));
        return m;
    }

    /// <summary>"When Gryphmark starts" choices (panel ⋯ menu and File → Folders at start).</summary>
    internal IEnumerable<MenuItem> StartupItems()
    {
        foreach (var (key, label, tip) in new[] { ("restore", "StartupRestore", "StartupRestoreTip"), ("clean", "StartupClean", "StartupCleanTip"), ("pinned", "StartupPinned", "StartupPinnedTip") })
        {
            var mi = MenuEntry(Strings.Get(label), () => { _settings.StartupFolders = key; _settings.Save(); BuildMenu(); }, _settings.StartupFolders == key);
            mi.ToolTip = Strings.Get(tip);
            yield return mi;
        }
    }

    private static void StartSelf(params string[] args)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try { Process.Start(psi); } catch { }
    }

    private static void ShowInExplorer(string path, bool select)
    {
        try
        {
            if (select) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    // ------------------------------------------------------------------ follow the disk
    private static bool SafeDirExists(string dir) { try { return Directory.Exists(dir); } catch { return false; } }

    private void ScheduleRefresh() => Dispatcher.BeginInvoke(() => { _folderRefresh.Stop(); _folderRefresh.Start(); });

    private void WatchRoot(string dir)
    {
        if (_watchers.ContainsKey(dir)) return;
        try
        {
            var w = new FileSystemWatcher(dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, InternalBufferSize = 64 * 1024 };
            w.Created += (_, _) => ScheduleRefresh();
            w.Deleted += (_, _) => ScheduleRefresh();
            w.Renamed += (_, _) => ScheduleRefresh();
            // the folder itself was deleted, or a network drive went away, or too many changes at once
            w.Error += (_, _) => Dispatcher.BeginInvoke(() => { StopWatching(dir); ScheduleRefresh(); });
            w.EnableRaisingEvents = true;
            _watchers[dir] = w;
        }
        catch { }
    }

    private void StopWatching(string dir)
    {
        if (_watchers.Remove(dir, out var w)) try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
    }

    /// <summary>Every few seconds, off the UI thread (a lost network drive can take long to answer): has an
    /// open folder disappeared or come back? The watchers do not always report the folder itself.</summary>
    private void ProbeRoots()
    {
        if (_probing || _roots.Count == 0) return;
        _probing = true;
        var roots = _roots.ToList();
        var missingNow = _missing.ToHashSet(PathCmp);
        Task.Run(() =>
        {
            bool changed = roots.Any(r => SafeDirExists(r) == missingNow.Contains(r));
            Dispatcher.BeginInvoke(() => { _probing = false; if (changed) RefreshTree(); });
        });
    }

    /// <summary>Opens the folders down to the current file and selects it (called when the file changes).</summary>
    private void RevealCurrent()
    {
        if (_roots.Count == 0 || _path == null || _revealedPath == _path) return;
        _revealedPath = _path;
        var root = _roots.Where(r => _path.StartsWith(r.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(r => r.Length).FirstOrDefault();
        if (root == null || _missing.Contains(root))
        {
            if (FilesTree.SelectedItem is TreeViewItem s) s.IsSelected = false;
            return;
        }
        var node = FilesTree.Items.OfType<TreeViewItem>().FirstOrDefault(i => PathCmp.Equals(i.Tag as string, root));
        if (node == null) return;
        node.IsExpanded = true;
        var items = node.Items;
        var current = root;
        foreach (var part in _path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            var next = items.OfType<TreeViewItem>().FirstOrDefault(i => PathCmp.Equals(i.Tag as string, current));
            if (next == null) return;
            if (SafeDirExists(current)) { next.IsExpanded = true; items = next.Items; continue; }
            next.IsSelected = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => next.BringIntoView());
        }
    }
}
