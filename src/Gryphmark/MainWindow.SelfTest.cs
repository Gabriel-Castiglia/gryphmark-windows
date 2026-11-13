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
            _settings.FilesPanel = true; _settings.Outline = true; ApplySideLayout(); UpdateLayout();
            var f = FilesPane.TranslatePoint(new Point(0, 0), this); var o = OutlinePane.TranslatePoint(new Point(0, 0), this);
            Check("sidebar: Files above Contents, same column and width",
                  f.Y < o.Y && Math.Abs(f.X - o.X) < 1 && Math.Abs(FilesPane.ActualWidth - OutlinePane.ActualWidth) < 1 && FilesPane.ActualHeight > 50,
                  $"files {f} {FilesPane.ActualWidth}x{FilesPane.ActualHeight}, outline {o} {OutlinePane.ActualWidth}");

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
