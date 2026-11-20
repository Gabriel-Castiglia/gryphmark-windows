// Gryphmark -- main window: renaming and moving files and folders from the Files panel.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 16, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace Gryphmark;

/// <summary>File operations of the Files panel, all of them on disk: rename (F2 or right-click → Rename) and,
/// with the tree unlocked (the padlock in its header), drag and drop onto a folder or onto a file of that
/// folder. Every rename and drop can be undone and redone with the two arrows of the header, or with
/// Ctrl+Z / Ctrl+Y while the tree has the focus. The open document follows its file, and is saved first if it
/// had changes. The open folders themselves (the coloured blocks) are not renamed or dragged from here.</summary>
public partial class MainWindow
{
    private const string DragFormat = "Gryphmark.TreePath";
    private readonly Stack<(string From, string To)> _fileUndo = new();
    private readonly Stack<(string From, string To)> _fileRedo = new();
    private Point _dragStart;
    private TreeViewItem? _dragItem;
    private TreeViewItem? _dropTarget;

    private void InitFileOps()
    {
        FilesUndo.Click += async (_, _) => await UndoFileOp();
        FilesRedo.Click += async (_, _) => await RedoFileOp();
        FilesLock.Click += (_, _) => { _settings.TreeLocked = !_settings.TreeLocked; _settings.Save(); UpdateFileOpButtons(); };
        FilesTree.AllowDrop = true;
        FilesTree.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(FilesTree);
            var src = e.OriginalSource as DependencyObject;
            _dragItem = Ancestor<ToggleButton>(src) == null ? Ancestor<TreeViewItem>(src) : null;   // not from the chevron
        };
        FilesTree.PreviewMouseMove += OnTreeDragMove;
        FilesTree.DragOver += OnTreeDragOver;
        FilesTree.DragLeave += (_, _) => SetDropTarget(null);
        FilesTree.Drop += OnTreeDrop;
        UpdateFileOpButtons();
    }

    private void UpdateFileOpButtons()
    {
        FilesUndo.IsEnabled = _fileUndo.Count > 0;
        FilesRedo.IsEnabled = _fileRedo.Count > 0;
        FilesUndo.ToolTip = _fileUndo.Count > 0 ? string.Format(Strings.Get("FileUndoTip"), Describe(_fileUndo.Peek())) : Strings.Get("FileUndoNone");
        FilesRedo.ToolTip = _fileRedo.Count > 0 ? string.Format(Strings.Get("FileRedoTip"), Describe(_fileRedo.Peek())) : Strings.Get("FileRedoNone");
        FilesLock.Content = _settings.TreeLocked ? "" : "";
        FilesLock.ToolTip = Strings.Get(_settings.TreeLocked ? "TreeLockedTip" : "TreeUnlockedTip");
    }

    private static string Describe((string From, string To) op) =>
        string.Equals(Path.GetDirectoryName(op.From), Path.GetDirectoryName(op.To), StringComparison.OrdinalIgnoreCase)
            ? $"{Path.GetFileName(op.From)} → {Path.GetFileName(op.To)}"
            : $"{Path.GetFileName(op.From)} → {Path.GetFileName(Path.GetDirectoryName(op.To))}";

    private static bool IsUnder(string path, string dir) =>
        path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Moves or renames a file or folder on disk. The open document follows it (saved first if it had
    /// changes). Returns false, with a message, when it could not be done.</summary>
    private async Task<bool> MovePath(string from, string to, bool record)
    {
        try
        {
            bool isDir = Directory.Exists(from);
            if (!isDir && !File.Exists(from)) { Flash(Strings.Get("ItemGone")); RefreshTree(); return false; }
            bool caseOnly = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(from, to, StringComparison.Ordinal)) return false;
            if (!caseOnly && (File.Exists(to) || Directory.Exists(to)))
            { Flash(string.Format(Strings.Get("NameExists"), Path.GetFileName(to))); return false; }
            if (isDir && IsUnder(to, from)) { Flash(Strings.Get("MoveIntoItself")); return false; }

            bool openAffected = _path != null && (string.Equals(_path, from, StringComparison.OrdinalIgnoreCase) || (isDir && IsUnder(_path, from)));
            if (openAffected && _dirty && !await SaveAsync(false)) return false;
            if (openAffected) Watch(null);

            if (isDir)
            {
                if (caseOnly) { var tmp = from + ".gm-rename-" + Guid.NewGuid().ToString("N")[..6]; Directory.Move(from, tmp); Directory.Move(tmp, to); }
                else Directory.Move(from, to);
            }
            else File.Move(from, to);

            if (openAffected)
            {
                var np = to + _path!.Substring(from.Length);
                _path = np;
                Watch(np);
                try { await Js($"mdApp.setBase({DocBaseJs(np)})"); } catch { }
                _settings.AddRecent(np); _settings.Save();
                BuildMenu(); UpdateTitle(); UpdateStatus();
            }
            FollowInLists(from, to, isDir);
            if (record) { _fileUndo.Push((from, to)); _fileRedo.Clear(); }
            UpdateFileOpButtons();
            _expandNext = Path.GetDirectoryName(to);
            RefreshTree();
            return true;
        }
        catch (Exception e)
        {
            if (_path != null) Watch(_path);
            Flash(string.Format(Strings.Get("MoveFailed"), e.Message));
            return false;
        }
    }

    /// <summary>File → Open recent and the recent folders follow a rename or a move (they kept the old path, which
    /// then said "not found").</summary>
    private void FollowInLists(string from, string to, bool isDir)
    {
        string Map(string p) =>
            string.Equals(p, from, StringComparison.OrdinalIgnoreCase) ? to
            : isDir && IsUnder(p, from) ? to + p[from.Length..] : p;
        bool changed = false;
        foreach (var list in new[] { _settings.Recent, _settings.RecentFolders, _settings.FavoriteFiles })
            for (int i = 0; i < list.Count; i++)
            {
                var m = Map(list[i]);
                if (!ReferenceEquals(m, list[i])) { list[i] = m; changed = true; }
            }
        if (!changed) return;
        foreach (var list in new[] { _settings.Recent, _settings.RecentFolders, _settings.FavoriteFiles })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(p => !seen.Add(p));
        }
        _settings.Save(); BuildMenu();
    }

    // Names Windows changes or refuses without saying so: a final dot or space is dropped (the file then is not at
    // the path the document follows), and CON, PRN, AUX, NUL, COM1-9 and LPT1-9 are devices, with any extension.
    private static readonly Regex ReservedName = new(@"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(\..*)?$", RegexOptions.IgnoreCase);

    internal static bool ValidName(string name) =>
        name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.Trim('.', ' ').Length > 0
        && !name.EndsWith('.') && !name.EndsWith(' ') && !name.StartsWith(' ') && !ReservedName.IsMatch(name);

    private async Task UndoFileOp()
    {
        if (_fileUndo.Count == 0) return;
        var op = _fileUndo.Peek();
        if (await MovePath(op.To, op.From, false)) { _fileUndo.Pop(); _fileRedo.Push(op); }
        UpdateFileOpButtons();
    }

    private async Task RedoFileOp()
    {
        if (_fileRedo.Count == 0) return;
        var op = _fileRedo.Peek();
        if (await MovePath(op.From, op.To, false)) { _fileRedo.Pop(); _fileUndo.Push(op); }
        UpdateFileOpButtons();
    }

    // ------------------------------------------------------------------ rename (F2 / right-click)
    private bool CanEditNode(string path) => path != FavTag && !_roots.Contains(path, PathCmp);

    private async void RenameNode(string path)
    {
        if (!CanEditNode(path)) return;
        if (!File.Exists(path) && !Directory.Exists(path)) { Flash(Strings.Get("ItemGone")); RefreshTree(); return; }
        var old = Path.GetFileName(path);
        var dlg = new InputDialog(this, Strings.Plain("Rename"), new[] { (Strings.Get("RenameName"), old) });
        if (dlg.ShowDialog() != true) return;
        var name = dlg.Values[0];
        if (name.Length == 0 || name == old) return;
        if (!ValidName(name)) { Flash(string.Format(Strings.Get("NameInvalid"), name)); return; }
        await MovePath(path, Path.Combine(Path.GetDirectoryName(path)!, name), true);
    }

    private MenuItem RenameEntry(string path)
    {
        var mi = MenuEntry(Strings.Plain("Rename"), () => RenameNode(path));
        mi.InputGestureText = "F2";
        return mi;
    }

    /// <summary>F2 renames the selected row; Ctrl+Z / Ctrl+Y undo and redo. Called from OnTreeKey.</summary>
    private bool TreeFileKey(KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.F2 && FilesTree.SelectedItem is TreeViewItem { Tag: string p }) { RenameNode(p); return true; }
        if (ctrl && e.Key == Key.Z) { _ = UndoFileOp(); return true; }
        if (ctrl && e.Key == Key.Y) { _ = RedoFileOp(); return true; }
        return false;
    }

    // ------------------------------------------------------------------ drag and drop (tree unlocked)
    private void OnTreeDragMove(object sender, MouseEventArgs e)
    {
        if (_settings.TreeLocked || e.LeftButton != MouseButtonState.Pressed || _dragItem is not { Tag: string path }) return;
        var d = e.GetPosition(FilesTree) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragItem; _dragItem = null;
        if (!CanEditNode(path)) return;
        try { DragDrop.DoDragDrop(item, new DataObject(DragFormat, path), DragDropEffects.Move); }
        finally { SetDropTarget(null); }
    }

    /// <summary>The folder a drop on this row goes into: the folder itself, or the folder of a file.</summary>
    private string? DropDir(DragEventArgs e, out TreeViewItem? row)
    {
        row = Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (row?.Tag is not string p) return null;
        if (SafeDirExists(p)) return p;
        return Path.GetDirectoryName(p);
    }

    private bool CanDrop(string src, string? dir) =>
        dir != null && SafeDirExists(dir)
        && !string.Equals(Path.GetDirectoryName(src), dir, StringComparison.OrdinalIgnoreCase)   // already there
        && !string.Equals(src, dir, StringComparison.OrdinalIgnoreCase) && !IsUnder(dir, src);  // not into itself

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return;   // anything else (files from Explorer) is left to the window
        var src = (string)e.Data.GetData(DragFormat);
        var dir = DropDir(e, out var row);
        bool ok = CanDrop(src, dir);
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(ok ? FindRow(dir!) ?? row : null);
        AutoScroll(e);
        e.Handled = true;
    }

    private async void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return;
        e.Handled = true;
        var src = (string)e.Data.GetData(DragFormat);
        var dir = DropDir(e, out _);
        SetDropTarget(null);
        if (!CanDrop(src, dir)) return;
        await MovePath(src, Path.Combine(dir!, Path.GetFileName(src)), true);
    }

    private TreeViewItem? FindRow(string path)
    {
        TreeViewItem? Find(ItemCollection items)
        {
            foreach (var i in items.OfType<TreeViewItem>())
            {
                if (i.Tag is string p && string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) return i;
                if (i.IsExpanded && Find(i.Items) is { } f) return f;
            }
            return null;
        }
        return Find(FilesTree.Items);
    }

    private void SetDropTarget(TreeViewItem? row)
    {
        if (ReferenceEquals(row, _dropTarget)) return;
        _dropTarget?.ClearValue(OpacityProperty);
        _dropTarget = row;
        if (row != null) { row.IsSelected = true; row.Opacity = 0.75; }
    }

    private void AutoScroll(DragEventArgs e)
    {
        if (Ancestor<ScrollViewer>(e.OriginalSource as DependencyObject) is not { } sv) return;
        var y = e.GetPosition(sv).Y;
        if (y < 24) sv.ScrollToVerticalOffset(sv.VerticalOffset - 12);
        else if (y > sv.ActualHeight - 24) sv.ScrollToVerticalOffset(sv.VerticalOffset + 12);
    }
}
