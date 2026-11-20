// Vigia -- main window: favourite files.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 20, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Vigia;

/// <summary>Favourites: files marked with a star, from any of the open folders (or from none: a file opened by
/// itself can be one too). They make one list, the same for every tree, shown as the first block of the Files
/// panel. The star sits at the right of each file row: filled and golden for a favourite, faint on hover for the
/// rest; a click on it, the row's menu or File → Favourite adds or takes the file out. A favourite that was
/// renamed or moved from the panel follows its file; one deleted outside stays, greyed, until it is taken out.</summary>
public partial class MainWindow
{
    private const string FavTag = "::favorites";   // Tag of the Favourites block (never a path)
    private const string StarTag = "gm-star";        // Tag of the star of a row
    private static readonly SolidColorBrush StarBrush = Frozen(Color.FromRgb(0xE3, 0xA0, 0x08));

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void InitFavorites()
    {
        var seen = new HashSet<string>(PathCmp);
        _settings.FavoriteFiles.RemoveAll(p => string.IsNullOrWhiteSpace(p) || !seen.Add(p));
    }

    internal void SetFavoritesForTest(params string[] files)
    {
        _settings.FavoriteFiles = files.ToList();
        RefreshTree(); ApplyFilesVisibility();
    }

    private bool IsFavorite(string path) => _settings.FavoriteFiles.Contains(path, PathCmp);

    private void ToggleFavorite(string path)
    {
        bool was = IsFavorite(path);
        if (was) _settings.FavoriteFiles.RemoveAll(p => PathCmp.Equals(p, path));
        else _settings.FavoriteFiles.Add(path);
        _settings.Save();
        RefreshTree();
        ApplyFilesVisibility();
        BuildMenu();
        Flash(string.Format(Strings.Get(was ? "FavRemoved" : "FavAdded"), Path.GetFileName(path)));
    }

    /// <summary>A favourite is a folder (shown first, and opens like a tree) when it is one on disk, or when it is gone
    /// and its name has no Markdown extension.</summary>
    private static bool FavIsDir(string path) => SafeDirExists(path) || (!File.Exists(path) && !MdExtensions.Contains(Path.GetExtension(path)));

    /// <summary>The favourites as shown: folders first, then files, each in the list's order.</summary>
    private List<string> FavoritesShown() =>
        _settings.FavoriteFiles.Where(FavIsDir).Concat(_settings.FavoriteFiles.Where(p => !FavIsDir(p))).ToList();

    /// <summary>Up or down among the favourites of the same kind (folders among folders, files among files).</summary>
    private void MoveFavorite(string path, int delta)
    {
        var shown = FavoritesShown();
        int i = shown.FindIndex(p => PathCmp.Equals(p, path)), j = i + delta;
        if (i < 0 || j < 0 || j >= shown.Count || FavIsDir(shown[i]) != FavIsDir(shown[j])) return;
        (shown[i], shown[j]) = (shown[j], shown[i]);
        _settings.FavoriteFiles = shown;
        _settings.Save();
        RefreshTree();
    }

    /// <summary>A favourite dragged onto another one takes its place (the others move down or up).</summary>
    internal void MoveFavoriteTo(string path, string target)
    {
        var shown = FavoritesShown();
        int from = shown.FindIndex(p => PathCmp.Equals(p, path)), to = shown.FindIndex(p => PathCmp.Equals(p, target));
        if (from < 0 || to < 0 || from == to) return;
        var item = shown[from];
        shown.RemoveAt(from);
        shown.Insert(to, item);
        _settings.FavoriteFiles = shown;
        _settings.Save();
        RefreshTree();
    }

    /// <summary>A row right under the Favourites title (not a file inside a favourite folder).</summary>
    private static bool IsFavRow(TreeViewItem? row) =>
        row != null && ItemsControl.ItemsControlFromItemContainer(row) is TreeViewItem { Tag: FavTag };

    // ------------------------------------------------------------------ recent files (off by default)
    private const string RecentTag = "::recent";

    private void ToggleRecentPanel()
    {
        _settings.RecentPanel = !_settings.RecentPanel; _settings.Save();
        UpdateRecentBlock(); ApplyFilesVisibility(); BuildMenu();
    }

    /// <summary>RECENT: the last files opened, under the favourites. Off unless turned on (View → Recent files, or
    /// the panel's ⋯ menu).</summary>
    private TreeViewItem? MakeRecentRoot()
    {
        if (!_settings.RecentPanel) return null;
        var files = _settings.Recent.Where(File.Exists).Take(8).ToList();
        if (files.Count == 0) return null;
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
        var name = new TextBlock { Text = Strings.Get("RecentTitle"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon); header.Children.Add(name);
        var item = new TreeViewItem { Header = header, Tag = RecentTag, Style = (Style)FindResource("Gm.FilesRoot"), Background = Brushes.Transparent };
        item.SetResourceReference(Control.BorderBrushProperty, "Gm.ChromeBorder");
        foreach (var f in files)
        {
            var node = MakeNode(f, false);
            if (node.Header is DockPanel h && h.Children.OfType<TextBlock>().LastOrDefault() is { } t)
            {
                t.Inlines.Clear();
                t.Inlines.Add(new Run(Path.GetFileName(f)));
                var where = new Run("   " + FolderLabel(Path.GetDirectoryName(f) ?? f)) { FontSize = 11 };
                where.SetResourceReference(TextElement.ForegroundProperty, "Gm.TextMuted");
                t.Inlines.Add(where);
            }
            item.Items.Add(node);
        }
        item.ContextMenu = new ContextMenu { Items = { MenuEntry(Strings.Get("RecentHide"), ToggleRecentPanel) } };
        return item;
    }

    /// <summary>Puts the RECENT block in step with File → Open recent without rebuilding the trees.</summary>
    private void UpdateRecentBlock()
    {
        if (!_folderInit) return;
        var old = FilesTree.Items.OfType<TreeViewItem>().FirstOrDefault(i => i.Tag as string == RecentTag);
        bool wasOpen = old?.IsExpanded ?? true;
        int at = old != null ? FilesTree.Items.IndexOf(old) : (FilesTree.Items.OfType<TreeViewItem>().FirstOrDefault()?.Tag as string == FavTag ? 1 : 0);
        if (old != null) FilesTree.Items.Remove(old);
        if (MakeRecentRoot() is { } fresh) { fresh.IsExpanded = wasOpen; FilesTree.Items.Insert(Math.Min(at, FilesTree.Items.Count), fresh); }
    }

    private static TextBlock StarGlyph(bool filled, double size = 12) => new()
    {
        Text = filled ? "" : "", FontFamily = (FontFamily)Application.Current.Resources["Gm.Icons"], FontSize = size,
        VerticalAlignment = VerticalAlignment.Center, Foreground = filled ? StarBrush : null,
    };

    /// <summary>Add to / Remove from favourites, for the menu of a file row.</summary>
    private MenuItem FavoriteEntry(string path)
    {
        bool fav = IsFavorite(path);
        var mi = MenuEntry(Strings.Get(fav ? "FavRemove" : "FavAdd"), () => ToggleFavorite(path));
        mi.Icon = StarGlyph(!fav, 14);
        return mi;
    }

    /// <summary>The star at the right of a file row (MakeNode).</summary>
    private void AddStar(DockPanel header, string path)
    {
        bool fav = IsFavorite(path);
        var star = StarGlyph(fav);
        star.Tag = StarTag;
        star.Padding = new Thickness(6, 0, 3, 0);
        star.Cursor = Cursors.Hand;
        star.ToolTip = Strings.Get(fav ? "FavRemove" : "FavAdd");
        if (!fav)
        {
            star.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
            star.Opacity = 0;
            header.MouseEnter += (_, _) => star.Opacity = 0.75;
            header.MouseLeave += (_, _) => star.Opacity = 0;
        }
        DockPanel.SetDock(star, Dock.Right);
        header.Children.Add(star);
    }

    /// <summary>A click on the star of a row (OnTreeClick): toggles that file and keeps the click from opening it.</summary>
    private bool StarClicked(DependencyObject? src, string path)
    {
        for (var d = src; d != null && d is not TreeViewItem; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { Tag: StarTag })
            {
                ToggleFavorite(path);
                return true;
            }
        return false;
    }

    /// <summary>The Favourites block, first in the panel, or null when there are none.</summary>
    private TreeViewItem? MakeFavoritesRoot()
    {
        var favs = _settings.FavoriteFiles;
        if (favs.Count == 0) return null;
        bool dark = ChromeIsDark();
        Brush fill = _settings.FolderColors == "none" ? Brushes.Transparent : new SolidColorBrush(dark ? FromHsl(43, 0.32, 0.19) : FromHsl(43, 0.80, 0.93));
        Brush edge = _settings.FolderColors == "none" ? Brushes.Transparent : StarBrush;
        var icon = StarGlyph(true, 13);
        icon.Margin = new Thickness(0, 0, 7, 0);
        var name = new TextBlock { Text = Strings.Get("FavTitle"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { Text = favs.Count.ToString(), FontSize = 11, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(icon); header.Children.Add(count); header.Children.Add(name);
        var item = new TreeViewItem { Header = header, Tag = FavTag, ToolTip = Strings.Get("FavTip"), Style = (Style)FindResource("Gm.FilesRoot"), Background = fill, BorderBrush = edge };
        ToolTipService.SetInitialShowDelay(item, 900);
        var shown = FavoritesShown();
        for (int i = 0; i < shown.Count; i++)
        {
            bool dir = FavIsDir(shown[i]);
            item.Items.Add(MakeFavoriteNode(shown[i], dir, i > 0 && FavIsDir(shown[i - 1]) == dir, i < shown.Count - 1 && FavIsDir(shown[i + 1]) == dir));
        }
        item.ContextMenu = new ContextMenu
        {
            Items =
            {
                MenuEntry(Strings.Get("FavSortAz"), () =>
                {
                    _settings.FavoriteFiles = _settings.FavoriteFiles.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
                    _settings.Save(); RefreshTree();
                }),
                MenuEntry(Strings.Get("FavRemoveMissing"), () =>
                {
                    _settings.FavoriteFiles.RemoveAll(p => !File.Exists(p)); _settings.Save(); RefreshTree(); ApplyFilesVisibility(); BuildMenu();
                }, enabled: favs.Any(p => !File.Exists(p))),
                new Separator(),
                MenuEntry(Strings.Get("FavClear"), () =>
                {
                    if (ChoiceDialog.Ask(this, Strings.Get("FavClearAsk"), new[] { Strings.Get("Cancel"), Strings.Get("FavClearBtn") }, 0) != 1) return;
                    _settings.FavoriteFiles.Clear(); _settings.Save(); RefreshTree(); ApplyFilesVisibility(); BuildMenu();
                }),
            },
        };
        return item;
    }

    /// <summary>A favourite: the name, its folder in grey, the golden star; greyed out when it is gone. A favourite
    /// folder opens like a tree.</summary>
    private TreeViewItem MakeFavoriteNode(string path, bool isDir, bool canUp, bool canDown)
    {
        bool exists = isDir ? SafeDirExists(path) : File.Exists(path);
        var item = MakeNode(path, isDir && exists);
        if (item.Header is DockPanel header && header.Children.OfType<TextBlock>().LastOrDefault() is { } name)
        {
            name.Inlines.Clear();
            name.Inlines.Add(new Run(Path.GetFileName(path)));
            var where = new Run("   " + (exists ? FolderLabel(Path.GetDirectoryName(path) ?? path) : Strings.Get("FavMissing"))) { FontSize = 11 };
            where.SetResourceReference(TextElement.ForegroundProperty, "Gm.TextMuted");
            name.Inlines.Add(where);
            if (!exists) name.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
        }
        if (item.ContextMenu is { } menu)
        {
            menu.Items.Insert(0, MenuEntry(Strings.Get("FavUp"), () => MoveFavorite(path, -1), enabled: canUp));
            menu.Items.Insert(1, MenuEntry(Strings.Get("FavDown"), () => MoveFavorite(path, +1), enabled: canDown));
            menu.Items.Insert(2, new Separator());        }
        return item;
    }
}
