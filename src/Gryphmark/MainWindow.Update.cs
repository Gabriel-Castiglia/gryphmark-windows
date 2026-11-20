// Gryphmark -- main window: updates that install themselves.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 20, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Gryphmark;

/// <summary>Help → Check for updates (and once a day by itself): when GitHub has a newer release, one question,
/// and then everything happens alone. The installer is downloaded and checked against the release's SHA256SUMS.txt
/// (a file that does not match is thrown away, never run); every other Gryphmark window is asked to close (each one
/// saves or asks as when it is closed by hand) and notes how to come back; the installer runs in silence keeping the
/// install options, waits for this window to be gone, and reopens every window that was open, each with its
/// profile and its file. If a window does not close (its question was answered "Cancel"), nothing is installed and
/// the windows that had closed come back.</summary>
public partial class MainWindow
{
    private static string UpdateDir => Path.Combine(Path.GetTempPath(), "GryphmarkUpdate");
    private static string RelaunchDir => Path.Combine(UpdateDir, "relaunch");
    private static string UpdateEventName => @"Local\Gryphmark-" + Environment.UserName + "-update";
    private static string CheckStamp => Path.Combine(Settings.BaseDir, "update-check.txt");   // one daily check for all the profiles

    private EventWaitHandle? _updateSignal;
    private bool _updating;

    /// <summary>Called from the constructor: every window listens for the signal of a window that is updating.</summary>
    private void InitUpdate()
    {
        try
        {
            _updateSignal = new EventWaitHandle(false, EventResetMode.ManualReset, UpdateEventName);
            var signal = _updateSignal;
            // Listens for good: a window whose close was cancelled, or an update that did not go on, must still hear
            // the next one. After each signal it waits for the updating window to reset it before listening again.
            new Thread(() =>
            {
                while (true)
                {
                    try { signal.WaitOne(); } catch { return; }
                    Dispatcher.BeginInvoke(CloseForUpdate);
                    try { while (signal.WaitOne(0)) Thread.Sleep(500); } catch { return; }
                }
            }) { IsBackground = true, Name = "update-signal" }.Start();
        }
        catch { }
        if (Environment.GetCommandLineArgs().Contains("--updated"))
            Loaded += (_, _) => Flash(string.Format(Strings.Get("UpdatedTo"), Version));
    }

    private static bool DailyCheckDue()
    {
        try { return !File.Exists(CheckStamp) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(CheckStamp)).TotalHours > 24; }
        catch { return false; }
    }

    private static void StampCheck()
    {
        try { Directory.CreateDirectory(Settings.BaseDir); File.WriteAllText(CheckStamp, DateTime.UtcNow.ToString("o")); } catch { }
    }

    internal static bool IsNewer(string tag, string mine) =>
        System.Version.TryParse(tag.TrimStart('v', 'V'), out var a) && System.Version.TryParse(mine, out var b) && a > b;

    /// <summary>The hash of one file in a SHA256SUMS.txt ("hash  name" or "hash *name" per line), or null.</summary>
    internal static string? HashFor(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            int sp = line.IndexOfAny(new[] { ' ', '\t' });
            if (sp != 64) continue;
            var name = line[sp..].Trim().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return line[..64].ToLowerInvariant();
        }
        return null;
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (_updating) return;
        if (!interactive && !DailyCheckDue()) return;
        try
        {
            StampCheck();   // even if it fails (no network): at most one try a day
            using var http = NewHttp(TimeSpan.FromSeconds(10));
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
            var page = root.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;
            if (!IsNewer(tag, Version))
            {
                if (interactive) MessageBox.Show(this, string.Format(Strings.Get("UpToDate"), Version), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string? setupUrl = null, setupName = null, sumsUrl = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString();
                // the name becomes a file name in %TEMP%: letters, digits, dot, hyphen and underscore only
                if (name.StartsWith("Gryphmark-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') && url?.StartsWith("https://", StringComparison.Ordinal) == true)
                { setupUrl = url; setupName = name; }
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
            }
            if (setupUrl == null || setupName == null || sumsUrl == null)
            {
                // a release without its installer or its hashes: only the page can be offered
                if (MessageBox.Show(this, string.Format(Strings.Get("UpdateAvailable"), tag, Version), "Gryphmark", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes && page != null)
                    OpenLink(page);
                return;
            }
            int r = ChoiceDialog.Ask(this, string.Format(Strings.Get("UpdateAsk"), tag, Version),
                                     new[] { Strings.Get("UpdateLater"), Strings.Get("UpdateNotes"), Strings.Get("UpdateNow") }, 2);
            if (r == 1 && page != null) { OpenLink(page); return; }
            if (r != 2) return;
            await InstallUpdateAsync(tag, setupUrl, setupName, sumsUrl);
        }
        catch (Exception ex)
        {
            if (interactive) MessageBox.Show(this, string.Format(Strings.Get("UpdateError"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static HttpClient NewHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Gryphmark/{Version}");
        return http;
    }

    private void UpdateStatusText(string text) { _statusTimer.Stop(); StatusState.Text = text; }

    private async Task InstallUpdateAsync(string tag, string setupUrl, string setupName, string sumsUrl)
    {
        _updating = true;
        try
        {
            Directory.CreateDirectory(UpdateDir);
            var file = Path.Combine(UpdateDir, setupName);
            using (var http = NewHttp(TimeSpan.FromMinutes(15)))
            {
                var expected = HashFor(await http.GetStringAsync(sumsUrl), setupName)
                               ?? throw new InvalidOperationException(Strings.Get("UpdateNoHash"));
                using (var resp = await http.GetAsync(setupUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
                    await using var input = await resp.Content.ReadAsStreamAsync();
                    await using var output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
                    var buf = new byte[81920];
                    int n, lastPct = -1;
                    while ((n = await input.ReadAsync(buf)) > 0)
                    {
                        await output.WriteAsync(buf.AsMemory(0, n));
                        done += n;
                        int pct = total > 0 ? (int)(done * 100 / total) : -1;
                        if (pct != lastPct) { lastPct = pct; UpdateStatusText(string.Format(Strings.Get("UpdateDownloading"), tag, pct >= 0 ? pct + " %" : (done >> 20) + " MB")); }
                    }
                }
                string actual;
                await using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
                if (actual != expected)
                {
                    try { File.Delete(file); } catch { }
                    throw new InvalidOperationException(Strings.Get("UpdateBadHash"));
                }
            }

            // the other windows close (each saves or asks, as by hand) and note how to come back
            UpdateStatusText(Strings.Get("UpdateClosing"));
            try { if (Directory.Exists(RelaunchDir)) Directory.Delete(RelaunchDir, true); } catch { }
            Directory.CreateDirectory(RelaunchDir);
            var others = OtherWindows();
            if (others.Count > 0)
            {
                _updateSignal?.Set();
                var end = DateTime.UtcNow.AddMinutes(2);
                while (DateTime.UtcNow < end && others.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                    await Task.Delay(300);
                _updateSignal?.Reset();
                if (others.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                {
                    RelaunchClosed();
                    throw new InvalidOperationException(Strings.Get("UpdateOtherOpen"));
                }
            }
            // and this one: unsaved changes are saved or asked about now
            if (!await ConfirmDiscardAsync()) { RelaunchClosed(); _updating = false; UpdateStatus(); return; }
            WriteRelaunch();

            var psi = new ProcessStartInfo(file) { UseShellExecute = true };
            foreach (var a in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/GMWAITPID=" + Environment.ProcessId, "/GMRELAUNCH=1" })
                psi.ArgumentList.Add(a);
            Process.Start(psi);
            _closingConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateSignal?.Reset();
            RelaunchClosed();   // whatever closed for the update comes back
            UpdateStatus();
            MessageBox.Show(this, string.Format(Strings.Get("UpdateFailed"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>The other Gryphmark processes of this same installation.</summary>
    private static List<Process> OtherWindows()
    {
        var me = Environment.ProcessPath;
        var list = new List<Process>();
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(me ?? "Gryphmark")))
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                if (string.Equals(p.MainModule?.FileName, me, StringComparison.OrdinalIgnoreCase)) list.Add(p);
            }
            catch { }
        }
        return list;
    }

    /// <summary>Another window is updating: note how to reopen this one, then close as by hand.</summary>
    private void CloseForUpdate()
    {
        if (_updating || SelfTestLog != null) return;
        WriteRelaunch();
        Close();   // a question about unsaved changes may stop it: the update then waits, and gives up after a while
    }

    private string RelaunchFile(int pid) => Path.Combine(RelaunchDir, pid + ".json");

    private void WriteRelaunch()
    {
        try
        {
            Directory.CreateDirectory(RelaunchDir);
            var args = new List<string>();
            if (Settings.Profile != null) { args.Add("--profile"); args.Add(Settings.Profile); }
            if (Settings.IsSecondary) args.Add("--new-window");
            if (_path != null && File.Exists(_path)) args.Add(_path);
            File.WriteAllText(RelaunchFile(Environment.ProcessId), JsonSerializer.Serialize(args));
        }
        catch { }
    }

    /// <summary>The update did not go on: the windows that had closed for it come back, the others keep going.</summary>
    private static void RelaunchClosed()
    {
        var alive = Process.GetProcesses().Select(p => p.Id).ToHashSet();
        foreach (var f in SafeFiles(RelaunchDir))
        {
            bool closed = !int.TryParse(Path.GetFileNameWithoutExtension(f), out var pid) || !alive.Contains(pid) || pid == Environment.ProcessId;
            if (closed && pid != Environment.ProcessId) StartFromFile(f, false);
            try { File.Delete(f); } catch { }
        }
    }

    private static string[] SafeFiles(string dir) { try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>(); } catch { return Array.Empty<string>(); } }

    private static List<string> ReadArgs(string f)
    {
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(f)) ?? new(); } catch { return new(); }
    }

    private static void StartFromFile(string f, bool updated)
    {
        var args = ReadArgs(f);
        if (updated) args.Add("--updated");
        StartSelf(args.ToArray());
    }

    /// <summary>Gryphmark --after-update (run by the installer): reopens every window that was open. The main
    /// window first, and the others once it is up, so an extra window never takes the main one's place.</summary>
    internal static void RelaunchAfterUpdate()
    {
        var files = SafeFiles(RelaunchDir).Select(f => (File: f, Args: ReadArgs(f))).ToList();
        if (files.Count == 0) { StartSelf("--updated"); return; }
        bool IsMain(List<string> a) => !a.Contains("--profile") && !a.Contains("--new-window");
        var main = files.Where(x => IsMain(x.Args)).ToList();
        foreach (var x in main) StartSelf(x.Args.Append("--updated").ToArray());
        if (main.Count > 0 && files.Any(x => x.Args.Contains("--new-window")))
        {
            // the extra windows need the main one's lock to exist, or the first of them would become the main one
            var end = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < end && !Mutex.TryOpenExisting(@"Local\Gryphmark-" + Environment.UserName, out _)) Thread.Sleep(200);
        }
        foreach (var x in files.Where(x => !IsMain(x.Args))) StartSelf(x.Args.Append("--updated").ToArray());
        foreach (var x in files) try { File.Delete(x.File); } catch { }
    }
}
