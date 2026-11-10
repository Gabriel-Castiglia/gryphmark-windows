using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gryphmark;

/// <summary>Files panel: the tree of an open folder (subfolders and Markdown files), like the
/// explorer of VS Code or Obsidian. A click opens the file beside it; the tree follows changes on disk.</summary>
public partial class MainWindow
{
    private static readonly HashSet<string> MdExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".mdown", ".mkd" };
    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".vscode", ".idea", "__pycache__", "$RECYCLE.BIN", "System Volume Information" };
    private const string Placeholder = "…";

    private string? _folder;
    private string? _revealedPath;
    private FileSystemWatcher? _folderWatcher;
    private readonly DispatcherTimer _folderRefresh = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>Called from the constructor: wires the panel and reopens the folder of the last session
    /// (or the one passed on the command line).</summary>
    private void InitFolder(string? startFolder)
    {
        _folderRefresh.Tick += (_, _) => { _folderRefresh.Stop(); RefreshTree(); };
        FilesTree.PreviewMouseLeftButtonUp += OnTreeClick;
        FilesTree.PreviewKeyDown += OnTreeKey;
        FilesOpenBtn.Click += (_, _) => PickFolder();
        FilesRefresh.Click += (_, _) => RefreshTree();
        FilesCollapse.Click += (_, _) => { foreach (var i in FilesTree.Items.OfType<TreeViewItem>()) CollapseAll(i); };
        FilesClose.Click += (_, _) => CloseFolder();
        FilesSplitter.DragCompleted += (_, _) =>
        {
            if (FilesColumn.ActualWidth > 0) { _settings.FilesWidth = Math.Round(FilesColumn.ActualWidth); _settings.Save(); }
        };
        LocalizeFolder();
        var dir = startFolder ?? _settings.Folder;
        if (dir != null && Directory.Exists(dir)) OpenFolder(dir, showPanel: startFolder != null);
        else ApplyFilesVisibility();
        _folderInit = true;
    }

    private bool _folderInit;

    /// <summary>Self-test (--render-chrome): show the panel empty (dir = null) or with a folder and one file selected.</summary>
    internal void TestFolder(string? dir, string? select)
    {
        _settings.FilesPanel = true;
        LocalizeFolder();
        if (dir == null) CloseFolder();
        else { OpenFolder(dir); _path = select; _revealedPath = null; RevealCurrent(); _path = null; }
        ApplyFilesVisibility();
    }

    private void LocalizeFolder()
    {
        FilesEmpty.Text = Strings.Get("FilesEmpty");
        FilesOpenBtn.Content = Strings.Get("FilesOpenBtn");
        FilesRefresh.ToolTip = Strings.Get("Refresh");
        FilesCollapse.ToolTip = Strings.Get("CollapseAll");
        FilesClose.ToolTip = Strings.Plain("CloseDir");
        FilesTitle.Text = _folder != null ? FolderLabel(_folder) : Strings.Get("FilesTitle");
        if (_folder != null) RefreshTree();   // context menus are built with the interface language
    }

    private static string FolderLabel(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return (string.IsNullOrEmpty(name) ? dir : name).ToUpperInvariant();
    }

    // ------------------------------------------------------------------ open / close
    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = _folder ?? (_path != null ? Path.GetDirectoryName(_path) : null),
        };
        if (dlg.ShowDialog(this) == true) OpenFolder(dlg.FolderName);
    }

    private void OpenFolder(string dir, bool showPanel = true)
    {
        try { dir = Path.GetFullPath(dir); } catch { return; }
        if (!Directory.Exists(dir))
        {
            MessageBox.Show(this, string.Format(Strings.Get("FolderMissing"), dir), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Warning);
            _settings.RecentFolders.Remove(dir); _settings.Save(); BuildMenu();
            return;
        }
        _folder = dir;
        _settings.Folder = dir;
        _settings.AddRecentFolder(dir);
        if (showPanel) _settings.FilesPanel = true;
        _settings.Save();
        WatchFolder(dir);
        _revealedPath = null;
        BuildTree();
        ApplyFilesVisibility();
        BuildMenu();
        RevealCurrent();
    }

    private void CloseFolder()
    {
        _folder = null;
        _settings.Folder = null; _settings.Save();
        _folderWatcher?.Dispose(); _folderWatcher = null;
        FilesTree.Items.Clear();
        FilesTitle.Text = Strings.Get("FilesTitle");
        ApplyFilesVisibility();
        BuildMenu();
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
        bool open = _folder != null;
        FilesEmptyPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        FilesTree.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FilesHeaderButtons.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ tree
    private void BuildTree()
    {
        FilesTree.Items.Clear();
        if (_folder == null) return;
        FilesTitle.Text = FolderLabel(_folder);
        Populate(FilesTree.Items, _folder);
    }

    /// <summary>One level: subfolders first, then Markdown files, each sorted by name. Hidden and
    /// system entries and tool folders (.git, node_modules, bin, obj…) are left out.</summary>
    private void Populate(ItemCollection items, string dir)
    {
        List<string> dirs, files;
        try
        {
            dirs = new DirectoryInfo(dir).EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !SkippedDirs.Contains(d.Name))
                .Select(d => d.FullName).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
            files = new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && MdExtensions.Contains(f.Extension))
                .Select(f => f.FullName).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch { return; }   // no access: show the folder empty
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
        if (Directory.Exists(path)) { item.IsExpanded = !item.IsExpanded; return; }
        if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
        OpenPath(path);
    }

    // ------------------------------------------------------------------ context menus
    private MenuItem MenuEntry(string text, Action run)
    {
        var mi = new MenuItem { Header = text };
        mi.Click += (_, _) => run();
        return mi;
    }

    private ContextMenu FileMenu(string path) => new()
    {
        Items =
        {
            MenuEntry(Strings.Plain("FileOpen"), () => { if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) OpenPath(path); }),
            MenuEntry(Strings.Get("OpenNewWindow"), () => StartSelf("--new-window", path)),
            new Separator(),
            MenuEntry(Strings.Plain("OpenFolder"), () => ShowInExplorer(path, true)),
            MenuEntry(Strings.Get("CopyPath"), () => { try { Clipboard.SetText(path); } catch { } }),
        },
    };

    private ContextMenu FolderMenu(string path) => new()
    {
        Items =
        {
            MenuEntry(Strings.Plain("OpenFolder"), () => ShowInExplorer(path, false)),
            MenuEntry(Strings.Get("CopyPath"), () => { try { Clipboard.SetText(path); } catch { } }),
            new Separator(),
            MenuEntry(Strings.Get("Refresh"), RefreshTree),
        },
    };

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
    private void WatchFolder(string dir)
    {
        _folderWatcher?.Dispose();
        try
        {
            _folderWatcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() => { _folderRefresh.Stop(); _folderRefresh.Start(); });
            _folderWatcher.Created += changed;
            _folderWatcher.Deleted += changed;
            _folderWatcher.Renamed += (s, e) => changed(s, e);
            _folderWatcher.EnableRaisingEvents = true;
        }
        catch { _folderWatcher = null; }
    }

    /// <summary>Rebuilds the tree keeping the open folders open and the current file selected.</summary>
    private void RefreshTree()
    {
        if (_folder == null) return;
        if (!Directory.Exists(_folder)) { CloseFolder(); return; }
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(ItemCollection items)
        {
            foreach (var i in items.OfType<TreeViewItem>())
                if (i.IsExpanded && i.Tag is string p) { expanded.Add(p); Collect(i.Items); }
        }
        Collect(FilesTree.Items);
        BuildTree();
        void Restore(ItemCollection items)
        {
            foreach (var i in items.OfType<TreeViewItem>())
                if (i.Tag is string p && expanded.Contains(p)) { i.IsExpanded = true; Restore(i.Items); }
        }
        Restore(FilesTree.Items);
        _revealedPath = null;
        RevealCurrent();
    }

    /// <summary>Opens the folders down to the current file and selects it (called when the file changes).</summary>
    private void RevealCurrent()
    {
        if (_folder == null || _path == null || _revealedPath == _path) return;
        _revealedPath = _path;
        var root = _folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!_path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (FilesTree.SelectedItem is TreeViewItem s) s.IsSelected = false;
            return;
        }
        var items = FilesTree.Items;
        var current = _folder;
        foreach (var part in _path.Substring(root.Length).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            var node = items.OfType<TreeViewItem>().FirstOrDefault(i => string.Equals(i.Tag as string, current, StringComparison.OrdinalIgnoreCase));
            if (node == null) return;
            if (Directory.Exists(current)) { node.IsExpanded = true; items = node.Items; continue; }
            node.IsSelected = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => node.BringIntoView());
        }
    }
}
