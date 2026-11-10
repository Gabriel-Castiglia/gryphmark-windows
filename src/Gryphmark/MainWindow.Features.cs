using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Gryphmark;

/// <summary>Find/replace, outline, external-change watch, crash recovery and update check.</summary>
public partial class MainWindow
{
    private bool _crlf, _bom;
    private FileSystemWatcher? _watcher;
    private DateTime _suppressWatchUntil;
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly string _recoveryId = Guid.NewGuid().ToString("N");
    private static string RecoveryDir => Path.Combine(Settings.Dir, "recovery");
    public const string Repo = "Gabriel-Castiglia/gryphmark";

    private void InitFeatures()
    {
        FindBox.TextChanged += async (_, _) => await FindRun(0);
        FindBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await FindStep(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); }
            else if (e.Key == Key.Escape) { e.Handled = true; await HideFind(); }
        };
        ReplaceBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ReplaceOne(); }
            else if (e.Key == Key.Escape) { e.Handled = true; await HideFind(); }
        };
        FindCase.Click += async (_, _) => await FindRun(0);
        FindPrev.Click += async (_, _) => await FindStep(-1);
        FindNextBtn.Click += async (_, _) => await FindStep(1);
        FindCloseBtn.Click += async (_, _) => await HideFind();
        ReplaceBtn.Click += async (_, _) => await ReplaceOne();
        ReplaceAllBtn.Click += async (_, _) => await ReplaceAllAsync();
        OutlineList.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            if (OutlineList.SelectedIndex >= 0 && _ready) await Js($"mdApp.scrollToHeading({OutlineList.SelectedIndex})");
        };
        _recoveryTimer.Tick += async (_, _) => await SaveRecoveryAsync();
        _recoveryTimer.Start();
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatus(); };
        ApplyOutlineVisibility();
        LocalizeFeatures();
        Loaded += (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(1500);
            if ((DateTime.UtcNow - _settings.LastUpdateCheck).TotalHours > 24) await CheckUpdatesAsync(false);
        });
    }

    private void LocalizeFeatures()
    {
        FindBox.Tag = Strings.Get("FindPlaceholder");
        ReplaceBox.Tag = Strings.Get("ReplacePlaceholder");
        ReplaceBtn.Content = Strings.Get("ReplaceOne");
        ReplaceAllBtn.Content = Strings.Get("ReplaceAll");
        FindCase.ToolTip = Strings.Get("MatchCase");
        FindPrev.ToolTip = Strings.Get("Prev");
        FindNextBtn.ToolTip = Strings.Get("Next");
        FindCloseBtn.ToolTip = Strings.Get("Close");
        OutlineTitle.Text = Strings.Get("OutlineTitle");
        OutlineEmpty.Text = Strings.Get("OutlineEmpty");
    }

    private void StopFeatures()
    {
        _recoveryTimer.Stop();
        _watcher?.Dispose(); _watcher = null;
    }

    private void Flash(string text)
    {
        StatusState.Text = text;
        _statusTimer.Stop(); _statusTimer.Start();
    }

    // ------------------------------------------------------------------ find / replace
    private void ShowFind(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(async () =>
        {
            if (string.IsNullOrEmpty(FindBox.Text)) { var sel = await SelectedTextAsync(); if (sel.Length is > 0 and < 200 && !sel.Contains('\n')) FindBox.Text = sel; }
            FindBox.Focus(); FindBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async Task HideFind()
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        FindBar.Visibility = Visibility.Collapsed;
        if (_ready) { await Js("mdApp.findClose()"); await Js("mdApp.focus()"); }
        Web.Focus();
    }

    private string CaseArg => FindCase.IsChecked == true ? "true" : "false";

    private async Task FindRun(int step)
    {
        if (!_ready) return;
        await Js($"mdApp.find({Q(FindBox.Text)},{CaseArg},{step})");
    }

    private async Task FindStep(int step)
    {
        if (FindBar.Visibility != Visibility.Visible) { ShowFind(false); return; }
        if (!_ready || FindBox.Text.Length == 0) return;
        await Js($"mdApp.find({Q(FindBox.Text)},{CaseArg},{step})");
    }

    private void UpdateFindCount(int index, int total)
    {
        FindCount.Text = FindBox.Text.Length == 0 ? "" : total == 0 ? Strings.Get("NoResults") : string.Format(Strings.Get("OfCount"), index + 1, total);
    }

    private async Task ReplaceOne()
    {
        if (!_ready || FindBox.Text.Length == 0) return;
        await Js($"mdApp.replace({Q(FindBox.Text)},{CaseArg},{Q(ReplaceBox.Text)})");
    }

    private async Task ReplaceAllAsync()
    {
        if (!_ready || FindBox.Text.Length == 0) return;
        var n = await Js($"mdApp.replaceAll({Q(FindBox.Text)},{CaseArg},{Q(ReplaceBox.Text)})");
        Flash(string.Format(Strings.Get("Replaced"), n));
    }

    // ------------------------------------------------------------------ outline
    private void ToggleOutline()
    {
        _settings.Outline = !_settings.Outline; _settings.Save();
        ApplyOutlineVisibility(); BuildMenu();
    }

    private void ApplyOutlineVisibility()
    {
        OutlineColumn.Width = new GridLength(_settings.Outline ? 250 : 0);
        OutlinePane.Visibility = _settings.Outline ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateOutline(JsonElement items)
    {
        OutlineList.Items.Clear();
        foreach (var it in items.EnumerateArray())
        {
            int level = it.GetProperty("level").GetInt32();
            var tb = new TextBlock
            {
                Text = it.GetProperty("text").GetString(),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity = level >= 3 ? 0.75 : 1,
            };
            OutlineList.Items.Add(new ListBoxItem { Content = tb, Padding = new Thickness(10 + (level - 1) * 14, 5, 8, 5), ToolTip = tb.Text });
        }
        OutlineEmpty.Visibility = OutlineList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ external changes
    private void Watch(string? path)
    {
        _watcher?.Dispose(); _watcher = null;
        if (path == null) return;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(() => OnExternalChange(path));
            _watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() => Flash(Strings.Get("DeletedOnDisk")));
            _watcher.Deleted += (_, _) => Dispatcher.BeginInvoke(() => Flash(Strings.Get("DeletedOnDisk")));
        }
        catch { /* network folders without notifications: nothing to watch */ }
    }

    private bool _changePending;
    private async void OnExternalChange(string path)
    {
        if (DateTime.UtcNow < _suppressWatchUntil || _changePending || !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
        _changePending = true;
        try
        {
            await Task.Delay(400);   // let the other program finish writing
            if (!File.Exists(path)) return;
            if (_dirty)
            {
                var r = MessageBox.Show(this, string.Format(Strings.Get("ChangedOnDisk"), Path.GetFileName(path)), "Gryphmark",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            _dirty = false;
            await LoadFileAsync(path);
            Flash(Strings.Get("Reloaded"));
        }
        finally { _changePending = false; }
    }

    // ------------------------------------------------------------------ crash recovery
    private string DraftPath => Path.Combine(RecoveryDir, _recoveryId + ".md");
    private string DraftMeta => Path.Combine(RecoveryDir, _recoveryId + ".json");

    private async Task SaveRecoveryAsync()
    {
        if (!_dirty || !_ready) return;
        try
        {
            Directory.CreateDirectory(RecoveryDir);
            var md = JsonSerializer.Deserialize<string>(await Js("mdApp.getMarkdownPreserving()")) ?? "";
            File.WriteAllText(DraftPath, md, new UTF8Encoding(false));
            File.WriteAllText(DraftMeta, JsonSerializer.Serialize(new { path = _path, pid = Environment.ProcessId, time = DateTime.Now }));
        }
        catch { /* best effort */ }
    }

    private void ClearRecovery()
    {
        try { File.Delete(DraftPath); File.Delete(DraftMeta); } catch { }
    }

    /// <summary>Called once the editor is ready: offer drafts left by a Gryphmark that is no longer running.</summary>
    private async Task OfferRecoveryAsync()
    {
        if (!Directory.Exists(RecoveryDir)) return;
        foreach (var meta in Directory.GetFiles(RecoveryDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                var r = doc.RootElement;
                int pid = r.GetProperty("pid").GetInt32();
                bool alive = false;
                try { alive = Process.GetProcessById(pid).ProcessName.Equals("Gryphmark", StringComparison.OrdinalIgnoreCase); } catch { }
                if (alive) continue;
                var draft = Path.ChangeExtension(meta, ".md");
                string? orig = r.GetProperty("path").GetString();
                var when = r.GetProperty("time").GetDateTime();
                var name = orig != null ? Path.GetFileName(orig) : Strings.Get("Untitled");
                var ans = MessageBox.Show(this, string.Format(Strings.Get("Recover"), name, when.ToString("g")), "Gryphmark",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans == MessageBoxResult.Yes && File.Exists(draft))
                {
                    var text = File.ReadAllText(draft, Encoding.UTF8);
                    if (orig != null && File.Exists(orig)) await LoadFileAsync(orig);
                    await Js($"mdApp.load({Q(text)},{(orig != null ? "'https://doc.local/'" : "null")})");
                    _path = orig; _dirty = true;
                    UpdateTitle(); UpdateStatus(); Flash(Strings.Get("Recovered"));
                }
                File.Delete(meta); File.Delete(draft);
                if (ans == MessageBoxResult.Yes) break;
            }
            catch { try { File.Delete(meta); } catch { } }
        }
    }

    // ------------------------------------------------------------------ updates (GitHub releases)
    private async Task CheckUpdatesAsync(bool interactive)
    {
        try
        {
            // remember the attempt even if it fails (no network, no release yet): at most one try a day
            _settings.LastUpdateCheck = DateTime.UtcNow; _settings.Save();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Gryphmark/{Version}");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = (doc.RootElement.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
            var url = doc.RootElement.GetProperty("html_url").GetString();
            if (System.Version.TryParse(tag, out var latest) && System.Version.TryParse(Version, out var mine) && latest > mine)
            {
                var r = MessageBox.Show(this, string.Format(Strings.Get("UpdateAvailable"), tag, Version), "Gryphmark", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes && url != null) OpenLink(url);
            }
            else if (interactive)
                MessageBox.Show(this, string.Format(Strings.Get("UpToDate"), Version), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (interactive) MessageBox.Show(this, string.Format(Strings.Get("UpdateError"), ex.Message), "Gryphmark", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------ single instance
    /// <summary>A second Gryphmark passed us a file (or just asked to be shown).</summary>
    public void ActivateFromOtherInstance(string? path)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); Topmost = true; Topmost = false; Focus();
        if (!string.IsNullOrEmpty(path)) OpenPath(path);
    }
}
