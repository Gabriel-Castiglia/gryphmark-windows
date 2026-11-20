// Vigia -- main window: the Search pane (every file of the open folders).
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 20, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Vigia;

/// <summary>Search, as Ctrl+Shift+F in VS Code: a word or a phrase in every Markdown file of the open folders (the
/// same files the trees show). Results come grouped by file, each line with its number and the match in bold; a
/// click opens the file at that match, with the find bar showing it. It is a pane like Files and Contents: on or off
/// (View → Search, the toolbar, Ctrl+Shift+F) and placed with the layout button or by dragging its title. The search
/// runs off the window's thread, a moment after the typing stops, and a new search cancels the one before.</summary>
public partial class MainWindow
{
    internal sealed record SearchHit(int Line, string Text, int Occurrence, int Column);
    internal sealed record SearchFile(string Path, List<SearchHit> Hits);

    private const int SearchMaxFiles = 300, SearchMaxHits = 5000;
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? _searchCts;

    private void InitSearch()
    {
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); RunSearch(); };
        SearchBox.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };
        SearchCase.Click += (_, _) => RunSearch();
        SearchClose.Click += (_, _) => ToggleSearch();
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; _searchDelay.Stop(); RunSearch(); }
            else if (e.Key == Key.Escape) { e.Handled = true; SearchBox.Clear(); }
        };
        SearchTree.PreviewMouseLeftButtonUp += OnSearchClick;
        SearchTree.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || SearchTree.SelectedItem is not TreeViewItem item) return;
            e.Handled = true; ActivateSearchItem(item);
        };
        LocalizeSearch();
    }

    private void LocalizeSearch()
    {
        SearchTitle.Text = Strings.Get("SearchTitle");
        SearchCase.ToolTip = Strings.Get("MatchCase");
        SearchClose.ToolTip = Strings.Get("SearchCloseTip");
        SearchBox.ToolTip = Strings.Get("SearchBoxTip");
        if (SearchTree.Items.Count == 0) SearchStatus.Text = Strings.Get(_roots.Count == 0 ? "SearchNoFolder" : "SearchHint");
    }

    private void ToggleSearch()
    {
        if (_settings.SearchPanel) { _settings.SearchPanel = false; _settings.Save(); ApplySideLayout(); BuildMenu(); }
        else ShowSearch();
    }

    /// <summary>Shows the pane and puts the cursor in its box (with the selected words, if a few are selected).</summary>
    private async void ShowSearch()
    {
        if (!_settings.SearchPanel) { _settings.SearchPanel = true; _settings.Save(); ApplySideLayout(); BuildMenu(); }
        if (SearchBox.Text.Length == 0 && _ready)
        {
            var sel = await SelectedTextAsync();
            if (sel.Length is > 1 and < 120 && !sel.Contains('\n')) SearchBox.Text = sel;
        }
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { SearchBox.Focus(); SearchBox.SelectAll(); });
    }

    /// <summary>The Markdown files under a folder, as the trees list them: hidden, system and tool folders left out.</summary>
    internal static IEnumerable<string> MarkdownFiles(string root, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); } catch { continue; }
            foreach (var e in entries.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if ((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (e is DirectoryInfo d) { if (!SkippedDirs.Contains(d.Name)) stack.Push(d.FullName); }
                else if (MdExtensions.Contains(e.Extension)) yield return e.FullName;
            }
        }
    }

    /// <summary>Every match of q in a text: line number (1-based), the line, and the match's number in the whole text
    /// (what the find bar counts, so a click lands on that one).</summary>
    internal static List<SearchHit> FindInText(string text, string q, bool matchCase)
    {
        var hits = new List<SearchHit>();
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        int occurrence = 0, lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.TrimEnd('\r');
            for (int at = line.IndexOf(q, cmp); at >= 0; at = line.IndexOf(q, at + Math.Max(1, q.Length), cmp))
            {
                hits.Add(new SearchHit(lineNo, line, occurrence, at));
                occurrence++;
            }
        }
        return hits;
    }

    /// <summary>The search itself, off the window's thread. Stops at SearchMaxFiles files or SearchMaxHits matches.</summary>
    internal static List<SearchFile> SearchFolders(IEnumerable<string> roots, string q, bool matchCase, CancellationToken ct, out bool cut)
    {
        var found = new List<SearchFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        cut = false;
        foreach (var root in roots)
            foreach (var f in MarkdownFiles(root, ct))
            {
                if (!seen.Add(f)) continue;   // a folder open inside another one
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                var hits = FindInText(text, q, matchCase);
                if (hits.Count == 0) continue;
                found.Add(new SearchFile(f, hits));
                total += hits.Count;
                if (found.Count >= SearchMaxFiles || total >= SearchMaxHits) { cut = true; return found; }
            }
        return found;
    }

    private async void RunSearch()
    {
        _searchCts?.Cancel();
        var q = SearchBox.Text;
        SearchTree.Items.Clear();
        if (q.Trim().Length < 2) { SearchStatus.Text = Strings.Get(_roots.Count == 0 ? "SearchNoFolder" : "SearchHint"); return; }
        var roots = _roots.Where(r => !_missing.Contains(r)).ToList();
        if (roots.Count == 0) { SearchStatus.Text = Strings.Get("SearchNoFolder"); return; }
        var cts = _searchCts = new CancellationTokenSource();
        bool cs = SearchCase.IsChecked == true;
        SearchStatus.Text = Strings.Get("Searching");
        List<SearchFile> found; bool cut = false;
        try { found = await Task.Run(() => SearchFolders(roots, q, cs, cts.Token, out cut), cts.Token); }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) return;
        ShowResults(found, q, cs, cut);
    }

    private void ShowResults(List<SearchFile> found, string q, bool matchCase, bool cut)
    {
        SearchTree.Items.Clear();
        int hits = found.Sum(f => f.Hits.Count);
        SearchStatus.Text = found.Count == 0 ? Strings.Get("SearchNone")
            : string.Format(Strings.Get(cut ? "SearchCountCut" : "SearchCount"), hits, found.Count);
        bool expand = hits <= 400;
        foreach (var f in found)
        {
            var node = new TreeViewItem { Header = SearchFileHeader(f), Tag = f, ToolTip = f.Path, Style = (Style)FindResource("Gm.FilesItem"), IsExpanded = expand };
            ToolTipService.SetInitialShowDelay(node, 900);
            foreach (var h in f.Hits)
                node.Items.Add(new TreeViewItem { Header = SearchLineHeader(h, q, matchCase), Tag = (f.Path, h, q, matchCase, f.Hits.Count), Style = (Style)FindResource("Gm.FilesItem") });
            SearchTree.Items.Add(node);
        }
    }

    private FrameworkElement SearchFileHeader(SearchFile f)
    {
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 12, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
        var count = new TextBlock { Text = f.Hits.Count.ToString(), FontSize = 11, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        name.Inlines.Add(new Run(Path.GetFileName(f.Path)));
        var where = new Run("   " + FolderLabel(Path.GetDirectoryName(f.Path) ?? f.Path)) { FontSize = 11, FontWeight = FontWeights.Normal };
        where.SetResourceReference(TextElement.ForegroundProperty, "Gm.TextMuted");
        name.Inlines.Add(where);
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left); DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(icon); header.Children.Add(count); header.Children.Add(name);
        return header;
    }

    /// <summary>"12  …text around the match…" with this match in bold (the second of a line is the second one).</summary>
    private static FrameworkElement SearchLineHeader(SearchHit h, string q, bool matchCase)
    {
        var line = h.Text.Replace('\t', ' ');
        int at = Math.Clamp(h.Column, 0, line.Length);
        int start = Math.Max(0, at - 30);
        string before = (start > 0 ? "…" : "") + line[start..at].TrimStart();
        string match = line.Substring(at, Math.Min(q.Length, line.Length - at));
        string after = line[Math.Min(line.Length, at + q.Length)..];
        if (after.Length > 80) after = after[..80] + "…";
        var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var num = new Run(h.Line + "  ") { FontSize = 11 };
        num.SetResourceReference(TextElement.ForegroundProperty, "Gm.TextMuted");
        t.Inlines.Add(num);
        t.Inlines.Add(new Run(before));
        t.Inlines.Add(new Run(match) { FontWeight = FontWeights.Bold });
        t.Inlines.Add(new Run(after));
        return t;
    }

    private void OnSearchClick(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (Ancestor<System.Windows.Controls.Primitives.ToggleButton>(src) != null) return;   // the chevron
        if (Ancestor<TreeViewItem>(src) is { } item) ActivateSearchItem(item);
    }

    private async void ActivateSearchItem(TreeViewItem item)
    {
        switch (item.Tag)
        {
            case SearchFile f: await OpenAtMatch(f.Path, SearchBox.Text, SearchCase.IsChecked == true, 0, f.Hits.FirstOrDefault()?.Text, f.Hits.Count); break;
            case ValueTuple<string, SearchHit, string, bool, int> t: await OpenAtMatch(t.Item1, t.Item3, t.Item4, t.Item2.Occurrence, t.Item2.Text, t.Item5); break;
        }
    }

    /// <summary>Opens the file (asking about unsaved changes first, as always) and goes to its n-th match, with the
    /// find bar showing the words. The line and the file's number of matches let the page pick the right one in a
    /// view that hides some of the Markdown (app.js, findAt).</summary>
    private async Task OpenAtMatch(string path, string q, bool matchCase, int occurrence, string? line, int total)
    {
        if (!File.Exists(path)) { Flash(Strings.Get("ItemGone")); return; }
        if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
        {
            if (!await ConfirmDiscardAsync()) return;
            await LoadFileAsync(path);
            await Task.Delay(60);
        }
        FindCase.IsChecked = matchCase;
        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = Visibility.Collapsed;
        FindBox.Text = q;   // the find bar runs its own search on this
        if (_ready) await Js($"mdApp.findAt({Q(q)},{(matchCase ? "true" : "false")},{occurrence},{Q(line ?? "")},{total})");
    }
}
