// Gryphmark -- self-test in the real window, out of sight (--self-test <log>).
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 14, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Gryphmark;

/// <summary>What only the real app shows, as the macOS self-test does: per-monitor DPI, the sidebar, autosave and
/// its switch, the live reload and its green marks, [[name]] links. The window sits off the screen and never takes
/// the focus (App.OnStartup), with a profile of its own that is deleted afterwards; jstest.py covers the page.
/// Each line of the log is "ok", "FAIL" or the summary; the exit code is the number of failures.</summary>
public partial class MainWindow
{
    internal string? SelfTestLog { get; set; }
    private readonly StringBuilder _testOut = new();
    private int _testFailures;

    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);

    private void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _testFailures++;
        _testOut.AppendLine((ok ? "ok   " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : ": " + detail));
    }

    private static async Task<bool> WaitFor(Func<Task<bool>> cond, double seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) { if (await cond()) return true; await Task.Delay(100); }
        return await cond();
    }

    private async Task<int> JsInt(string expr) => int.TryParse(await Js(expr), out var n) ? n : -1;

    internal async Task RunSelfTestAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "gryphmark-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            // DPI: per monitor (v2) from app.manifest; WPF alone is system aware (1)
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int aware = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));
            Check("dpi: per-monitor awareness", aware == 2, $"awareness {aware}");

            Check("autosave: on by default", new Settings().AutoSave);
            _settings.AutoSave = true; AutoSaveBtn.IsChecked = true;
            await SetReadOnly(false); await SetMode("visual");

            // the sidebar: Files above Contents in one column, as on the Mac
            _settings.FilesPanel = true; _settings.Outline = true;
            SetLayout("left", "left", "rows", false); UpdateLayout();
            var f = FilesPane.TranslatePoint(new Point(0, 0), this); var o = OutlinePane.TranslatePoint(new Point(0, 0), this);
            var w = Web.TranslatePoint(new Point(0, 0), this);
            Check("sidebar: Files above Contents, same column and width",
                  f.Y < o.Y && Math.Abs(f.X - o.X) < 1 && Math.Abs(FilesPane.ActualWidth - OutlinePane.ActualWidth) < 1 && FilesPane.ActualHeight > 50 && o.X < w.X,
                  $"files {f} {FilesPane.ActualWidth}x{FilesPane.ActualHeight}, outline {o} {OutlinePane.ActualWidth}");

            // every other layout, measured: where each pane ends up against the text
            (Point F, Point O, Point W) Measure(string fs, string os, string arr, bool of)
            {
                SetLayout(fs, os, arr, of); UpdateLayout();
                return (FilesPane.TranslatePoint(new Point(0, 0), this), OutlinePane.TranslatePoint(new Point(0, 0), this), Web.TranslatePoint(new Point(0, 0), this));
            }
            var m = Measure("left", "left", "cols", false);
            Check("layout: Files | Contents | text", Math.Abs(m.F.Y - m.O.Y) < 1 && m.F.X < m.O.X && m.O.X < m.W.X && OutlinePane.ActualWidth > 100, $"{m}");
            m = Measure("left", "left", "rows", true);
            Check("layout: Contents above Files (swapped)", m.O.Y < m.F.Y && Math.Abs(m.F.X - m.O.X) < 1 && m.F.X < m.W.X, $"{m}");
            m = Measure("left", "right", "rows", false);
            Check("layout: Files | text | Contents", m.F.X < m.W.X && m.W.X < m.O.X && OutlinePane.ActualWidth > 100, $"{m}");
            m = Measure("right", "left", "rows", false);
            Check("layout: Contents | text | Files", m.O.X < m.W.X && m.W.X < m.F.X && FilesPane.ActualWidth > 100, $"{m}");
            m = Measure("right", "right", "rows", false);
            Check("layout: text | Files above Contents", m.W.X < m.F.X && m.F.Y < m.O.Y && Math.Abs(m.F.X - m.O.X) < 1 && SideColumn.ActualWidth == 0, $"{m}");
            m = Measure("right", "right", "cols", true);
            Check("layout: text | Contents | Files", m.W.X < m.O.X && m.O.X < m.F.X && Math.Abs(m.F.Y - m.O.Y) < 1, $"{m}");
            _settings.Outline = false;
            m = Measure("left", "right", "rows", false);
            Check("layout: a pane that is off takes no room", RightColumn.ActualWidth == 0 && OutlinePane.Visibility != Visibility.Visible && m.F.X < m.W.X, $"right {RightColumn.ActualWidth}");
            _settings.Outline = true;
            // dragging a pane by its title: the place under the pointer
            SetLayout("left", "left", "rows", false); UpdateLayout();
            double bw = BodyGrid.ActualWidth, bh = BodyGrid.ActualHeight;
            _dockPane = OutlinePane;
            var t1 = DockTargetAt(new Point(bw - 20, bh / 2)); var t2 = DockTargetAt(new Point(bw / 2, bh / 2));
            _dockPane = FilesPane;
            var t3 = DockTargetAt(new Point(20, bh * 0.9)); var t4 = DockTargetAt(new Point(20, bh / 2));
            _dockPane = null;
            Check("drag a pane: right edge, middle (nothing), below and beside the other pane",
                  t1 is { Side: "right", Arrange: null } && t2 == null && t3 is { Side: "left", Arrange: "rows", PaneFirst: false } && t4 is { Side: "left", Arrange: "cols", PaneFirst: true },
                  $"{t1} | {t2} | {t3} | {t4}");
            SetLayout("left", "left", "rows", false); UpdateLayout();

            // 2.2: a third pane (Search) placed like the others, a width per layout
            Check("search and recent: off by default", !new Settings().SearchPanel && !new Settings().RecentPanel);
            _settings.SearchPanel = true; _settings.SearchSide = "left"; _settings.PaneOrder = new() { "files", "outline", "search" };
            SetLayout("left", "left", "rows", false); UpdateLayout();
            var s1 = SearchPane.TranslatePoint(new Point(0, 0), this); f = FilesPane.TranslatePoint(new Point(0, 0), this); o = OutlinePane.TranslatePoint(new Point(0, 0), this);
            Check("layout: three panes one above the other", f.Y < o.Y && o.Y < s1.Y && Math.Abs(f.X - s1.X) < 1 && SearchPane.ActualHeight > 60, $"{f} {o} {s1}");
            DockTo("search", new DockTarget("left", "rows", true, Rect.Empty, "")); UpdateLayout();
            s1 = SearchPane.TranslatePoint(new Point(0, 0), this); f = FilesPane.TranslatePoint(new Point(0, 0), this);
            Check("drag a pane: Search dropped above the others goes first", s1.Y < f.Y && _settings.PaneOrder[0] == "search", string.Join(",", _settings.PaneOrder));
            DockTo("search", new DockTarget("right", null, true, Rect.Empty, "")); UpdateLayout();
            s1 = SearchPane.TranslatePoint(new Point(0, 0), this); w = Web.TranslatePoint(new Point(0, 0), this);
            Check("drag a pane: Search alone on the right", s1.X > w.X && RightColumn.ActualWidth > 100 && SideColumn.ActualWidth > 100, $"{s1} {w}");
            _settings.SearchPanel = false; _settings.SearchSide = "left";
            SetLayout("left", "left", "rows", false);
            _settings.LayoutWidths[WidthKey("left", PanesOn("left"))] = 333; ApplySideLayout(); UpdateLayout();
            double wRows = SideColumn.ActualWidth;
            SetLayout("left", "left", "cols", false); UpdateLayout(); double wCols = SideColumn.ActualWidth;
            SetLayout("left", "left", "rows", false); UpdateLayout();
            Check("layout: each layout keeps its own width", Math.Abs(wRows - 333) < 1 && Math.Abs(SideColumn.ActualWidth - 333) < 1 && wCols > 400, $"rows {wRows}, cols {wCols}, back {SideColumn.ActualWidth}");

            // search in the folders: every match counted, tool and hidden folders left out
            var sd = Path.Combine(tmp, "search"); Directory.CreateDirectory(Path.Combine(sd, "sub")); Directory.CreateDirectory(Path.Combine(sd, "node_modules"));
            File.WriteAllText(Path.Combine(sd, "a.md"), "# Alpha\n\nthe Gryphon and the gryphon\nnothing\n");
            File.WriteAllText(Path.Combine(sd, "sub", "b.md"), "one gryphon\n");
            File.WriteAllText(Path.Combine(sd, "node_modules", "c.md"), "gryphon\n");
            File.WriteAllText(Path.Combine(sd, "d.txt"), "gryphon\n");
            var res = SearchFolders(new[] { sd }, "gryphon", false, CancellationToken.None, out bool cutS);
            var resCase = SearchFolders(new[] { sd }, "Gryphon", true, CancellationToken.None, out _);
            var hitsA = FindInText("x gryphon\ny\nGRYPHON gryphon\n", "gryphon", false);
            Check("search: matches in Markdown files only, not in tool folders; case on demand; counted from the top",
                  res.Count == 2 && res.Sum(r => r.Hits.Count) == 3 && !cutS && resCase.Sum(r => r.Hits.Count) == 1
                  && hitsA.Count == 3 && hitsA[2].Line == 3 && hitsA[2].Occurrence == 2 && hitsA[1].Column == 0 && hitsA[2].Column == 8,
                  string.Join(" | ", res.Select(r => Path.GetFileName(r.Path) + ":" + r.Hits.Count)));
            Check("profiles: only test leftovers are cleaned up", Settings.TestProfileName.IsMatch("SelfTest-0123abcd") && Settings.TestProfileName.IsMatch("RenderChrome-ffffffff")
                  && !Settings.TestProfileName.IsMatch("My notes") && !Settings.TestProfileName.IsMatch("SelfTest-xyz") && !Settings.TestProfileName.IsMatch("SelfTest-0123abcd2"), "");

            // favourites: one list for every tree, first block of the panel, following a rename
            var favDir = Path.Combine(tmp, "fav"); Directory.CreateDirectory(favDir);
            var fa = Path.Combine(favDir, "a.md"); File.WriteAllText(fa, "# A\n");
            OpenFolder(favDir, showPanel: true);
            _settings.FavoriteFiles.Clear();
            ToggleFavorite(fa);
            var firstBlock = FilesTree.Items.OfType<System.Windows.Controls.TreeViewItem>().FirstOrDefault();
            Check("favourites: the starred file shows in the first block", firstBlock?.Tag as string == FavTag
                  && firstBlock.Items.OfType<System.Windows.Controls.TreeViewItem>().Any(i => i.Tag as string == fa), $"first {firstBlock?.Tag}");
            var fb = Path.Combine(favDir, "b.md");
            await MovePath(fa, fb, false);
            Check("favourites: follow a rename", _settings.FavoriteFiles.Contains(fb, StringComparer.OrdinalIgnoreCase) && !_settings.FavoriteFiles.Contains(fa, StringComparer.OrdinalIgnoreCase),
                  string.Join(" | ", _settings.FavoriteFiles));
            var fc = Path.Combine(favDir, "c.md"); File.WriteAllText(fc, "# C\n");
            var fsub = Path.Combine(favDir, "folder"); Directory.CreateDirectory(fsub);
            ToggleFavorite(fc); ToggleFavorite(fsub);
            var favRows = FilesTree.Items.OfType<System.Windows.Controls.TreeViewItem>().First().Items.OfType<System.Windows.Controls.TreeViewItem>().Select(i => i.Tag as string).ToList();
            Check("favourites: a folder too, shown before the files", favRows.Count == 3 && favRows[0] == fsub && favRows[1] == fb && favRows[2] == fc, string.Join(" | ", favRows));
            MoveFavoriteTo(fc, fb);
            favRows = FilesTree.Items.OfType<System.Windows.Controls.TreeViewItem>().First().Items.OfType<System.Windows.Controls.TreeViewItem>().Select(i => i.Tag as string).ToList();
            Check("favourites: dragged onto another one, it takes its place", favRows.Count == 3 && favRows[1] == fc && favRows[2] == fb, string.Join(" | ", favRows));
            _settings.Recent.Insert(0, fb); _settings.RecentPanel = false; ToggleRecentPanel();
            var blocks = FilesTree.Items.OfType<System.Windows.Controls.TreeViewItem>().Select(i => i.Tag as string).ToList();
            Check("recent: when turned on, a block under the favourites", blocks.Count >= 2 && blocks[0] == FavTag && blocks[1] == RecentTag, string.Join(" | ", blocks));
            ToggleRecentPanel();
            ToggleFavorite(fb); ToggleFavorite(fc); ToggleFavorite(fsub);
            Check("favourites: the block goes when the list is empty", _settings.FavoriteFiles.Count == 0
                  && FilesTree.Items.OfType<System.Windows.Controls.TreeViewItem>().All(i => i.Tag as string != FavTag), "");

            // updates: versions and the checksum file
            var sums = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF  Gryphmark-Setup-9.9.9.exe\n"
                     + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff *Gryphmark_Manual_EN.pdf\n";
            Check("updates: newer versions and checksums", IsNewer("v2.1.0", "2.0.9") && !IsNewer("2.0.0", "2.0.0") && !IsNewer("x", "1.0.0")
                  && HashFor(sums, "Gryphmark-Setup-9.9.9.exe") == "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                  && HashFor(sums, "Gryphmark_Manual_EN.pdf")?.StartsWith("ffff") == true && HashFor(sums, "other.exe") == null, "");

            // file operations of the tree: rename, drop into a folder, undo and redo; the open document follows
            var ops = Path.Combine(tmp, "ops"); var sub = Path.Combine(ops, "sub");
            Directory.CreateDirectory(sub);
            var n1 = Path.Combine(ops, "one.md"); File.WriteAllText(n1, "# One\n");
            OpenPath(n1);
            await WaitFor(() => Task.FromResult(string.Equals(_path, n1, StringComparison.OrdinalIgnoreCase)), 10);
            var n2 = Path.Combine(ops, "two.md");
            bool renamed = await MovePath(n1, n2, true);
            Check("files: rename on disk, the open document follows", renamed && File.Exists(n2) && !File.Exists(n1) && _path == n2, $"path {_path}");
            var n3 = Path.Combine(sub, "two.md");
            Check("files: drop target rules", CanDrop(n2, sub) && !CanDrop(n2, ops) && !CanDrop(sub, sub), "");
            bool dropped = await MovePath(n2, n3, true);
            Check("files: drop into a folder", dropped && File.Exists(n3) && _path == n3, $"path {_path}");
            await UndoFileOp(); bool u1 = File.Exists(n2) && _path == n2;
            await UndoFileOp(); bool u2 = File.Exists(n1) && _path == n1 && !FilesUndo.IsEnabled && FilesRedo.IsEnabled;
            Check("files: undo twice goes back to the start", u1 && u2, $"path {_path}");
            await RedoFileOp(); await RedoFileOp();
            Check("files: redo twice comes back", File.Exists(n3) && _path == n3 && !FilesRedo.IsEnabled, $"path {_path}");
            var caseDir = Path.Combine(ops, "Sub");
            bool cased = await MovePath(sub, caseDir, true);
            Check("files: a folder renamed only in case, with the open file inside",
                  cased && Directory.GetDirectories(ops).Select(Path.GetFileName).Contains("Sub") && _path == Path.Combine(caseDir, "two.md"), $"path {_path}");
            Check("files: two items cannot share a name", !await MovePath(Path.Combine(caseDir, "two.md"), caseDir, false), "");
            Check("files: names Windows changes or refuses are refused",
                  ValidName("a.md") && !ValidName("a.md.") && !ValidName("a.md ") && !ValidName("NUL.md") && !ValidName("com1") && ValidName("CONTENTS.md"), "");
            var moved = Path.Combine(caseDir, "two.md");
            Check("files: Open recent follows a rename", _settings.Recent.Contains(moved, StringComparer.OrdinalIgnoreCase)
                  && !_settings.Recent.Contains(n1, StringComparer.OrdinalIgnoreCase), string.Join(" | ", _settings.Recent.Take(4)));
            await NewDocAsync();

            // pictures: beside the document, in a parent folder (../), by an absolute path, and a ![[name]] found in
            // another folder of the panel; doc.local addresses go both ways
            Check("pictures: addresses go both ways", DocPath(DocUrl(@"C:\a b\c.png")) == @"C:\a b\c.png"
                  && DocPath(DocUrl(@"\\nas\share\d.png")) == @"\\nas\share\d.png" && DocPath("https://doc.local/x/y.png") == null,
                  DocUrl(@"C:\a b\c.png") + " " + DocUrl(@"\\nas\share\d.png"));
            var pics = Path.Combine(tmp, "pics"); var pdoc = Path.Combine(pics, "notes", "doc");
            Directory.CreateDirectory(pdoc); Directory.CreateDirectory(Path.Combine(pics, "att"));
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEklEQVR4nGNgYGD4z8DAwMDAAAAMAAH0xGq5AAAAAElFTkSuQmCC");
            File.WriteAllBytes(Path.Combine(pdoc, "here.png"), png);
            File.WriteAllBytes(Path.Combine(pics, "notes", "up one.png"), png);
            File.WriteAllBytes(Path.Combine(pics, "att", "far.png"), png);
            var outside = Path.Combine(tmp, "outside.png"); File.WriteAllBytes(outside, png);
            var pmd = Path.Combine(pdoc, "pics.md");
            File.WriteAllText(pmd, "# P\n\n![a](here.png)\n\n![b](<../up one.png>)\n\n![c](" + outside + ")\n\n![[far.png]]\n\n![gone](nothing.png)\n");
            OpenFolder(pics, showPanel: true);
            await LoadFileAsync(pmd);
            await SetMode("preview");
            bool shown = await WaitFor(async () => await JsInt("Array.from(document.querySelectorAll('#preview img')).filter(i => i.complete && i.naturalWidth === 2).length") == 4
                                         && await JsInt("document.querySelectorAll('#preview .gm-img-missing').length") == 1, 6);
            Check("pictures: beside, in ../, by absolute path and ![[name]] in another folder; a missing one boxed", shown,
                  await Js("Array.from(document.querySelectorAll('#preview img, #preview .gm-img-missing')).map(i => (i.src || i.title) + ':' + (i.naturalWidth ?? '-')).join(' ')"));
            Check("pictures: a file that is not a picture, outside the document's reach, is not served",
                  !InReach(Path.Combine(tmp, "auto.md")) && InReach(Path.Combine(pics, "att", "far.png")) && IsPicture(outside), "");
            await SetMode("visual");
            await NewDocAsync();

            // autosave writes after a pause; the switch turns it off and on
            var a = Path.Combine(tmp, "auto.md");
            File.WriteAllText(a, "# Auto\n\nbody\n");
            await LoadFileAsync(a);
            await Js("mdApp.focus(); mdApp.insertText('typed ')");
            bool ok = await WaitFor(() => Task.FromResult(!_dirty && File.ReadAllText(a).Contains("typed")), 4);
            Check("autosave: the file is written after a pause and the window is clean", ok, File.ReadAllText(a));
            AutoSaveBtn.IsChecked = false;
            await Js("mdApp.focus(); mdApp.insertText('manual ')");
            await Task.Delay(2500);
            Check("autosave: the switch off leaves the file alone", _dirty && !File.ReadAllText(a).Contains("manual"), File.ReadAllText(a));
            AutoSaveBtn.IsChecked = true;
            ok = await WaitFor(() => Task.FromResult(!_dirty && File.ReadAllText(a).Contains("manual")), 4);
            Check("autosave: the switch on saves what was pending", ok && _settings.AutoSave, File.ReadAllText(a));

            // another program writes: the text is shown and what changed is marked
            await Task.Delay(600);
            File.WriteAllText(a, "# Auto\n\nbody\n\nOutside, from the AI.\n");
            ok = await WaitFor(async () => await JsInt("mdApp.getMarkdown().includes('Outside') ? 1 : 0") == 1, 4);
            int marked = await JsInt("mdApp.changesMarked()");
            Check("live reload: another program's change is shown and marked in green", ok && marked > 0 && !_dirty, $"shown {ok}, marked {marked}");

            // [[name]] links: found in the open folders, a missing one marked, a click opens the file
            var root = Path.Combine(tmp, "wiki"); Directory.CreateDirectory(Path.Combine(root, "sub"));
            var w1 = Path.Combine(root, "home.md"); var w2 = Path.Combine(root, "sub", "Note-B.md");
            File.WriteAllText(w1, "# Home\n\nSee [[note-b]] and [[gone]] and `[[in code]]`.\n");
            File.WriteAllText(w2, "# B\n");
            OpenFolder(root, showPanel: true);
            await LoadFileAsync(w1);
            await SetMode("preview");
            ok = await WaitFor(async () => await JsInt("document.querySelectorAll('#preview a.wikilink').length") == 2
                                         && await JsInt("document.querySelectorAll('#preview a.wikilink.missing').length") == 1, 4);
            Check("wiki: names found in the open folders, the missing one marked, code untouched", ok,
                  await Js("Array.from(document.querySelectorAll('#preview a')).map(a => a.className + ':' + a.textContent).join(' ')"));
            OpenWiki("note-b");
            ok = await WaitFor(() => Task.FromResult(string.Equals(_path, w2, StringComparison.OrdinalIgnoreCase)), 4);
            Check("wiki: a link opens the file of that name", ok, _path ?? "(none)");
            await SetMode("visual");
        }
        catch (Exception ex) { Check("self-test ran to the end", false, ex.ToString()); }
        finally
        {
            _testOut.AppendLine(_testFailures == 0 ? "self-test: all passed" : $"self-test: {_testFailures} failed");
            try { File.WriteAllText(SelfTestLog!, _testOut.ToString()); } catch { }
            _dirty = false; _closingConfirmed = true;
            StopFeatures();
            try { Directory.Delete(tmp, true); } catch { }
            Application.Current.Shutdown(_testFailures);
        }
    }
}
