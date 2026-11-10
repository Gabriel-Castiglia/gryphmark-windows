// Gryphmark -- start-up: command line, profiles, one instance per profile.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 10, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace Gryphmark;

public partial class App : Application
{
    // One running instance per profile: the main one, and each dedicated instance (--profile "Name").
    private static string PipeName => "Gryphmark-" + Environment.UserName + (Settings.Profile == null ? "" : "-p-" + Settings.SafeName(Settings.Profile).ToLowerInvariant());
    private Mutex? _mutex;

    /// <summary>--profile "Name" or --profile=Name: a dedicated instance with its own settings and session.</summary>
    private static string? ProfileArg(string[] args, out int skip)
    {
        skip = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--profile=", StringComparison.OrdinalIgnoreCase)) return args[i][10..].Trim('"');
            if (args[i].Equals("--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { skip = i + 1; return args[i + 1]; }
        }
        return null;
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        int rc = Array.IndexOf(e.Args, "--render-chrome");
        if (rc >= 0 && rc + 1 < e.Args.Length)
        {
            var w = new MainWindow(null) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            w.Show();
            SelfTest.RenderChrome(w, e.Args[rc + 1]);
            Shutdown();
            return;
        }
        var profile = ProfileArg(e.Args, out int skip);
        if (!string.IsNullOrWhiteSpace(profile)) Settings.Profile = profile.Trim();
        var path = e.Args.Where((a, i) => i != skip && !a.StartsWith('-')).FirstOrDefault();
        try { if (path != null) path = Path.GetFullPath(path); } catch { path = null; }
        bool newWindow = e.Args.Contains("--new-window");

        // Single instance per profile: hand the file to the running one and leave
        _mutex = new Mutex(true, "Local\\" + PipeName, out bool first);
        if (!first && !newWindow && TrySend(path ?? ""))
        {
            Shutdown();
            return;
        }
        Settings.IsSecondary = !first;   // an extra window: the session belongs to the first one
        var main = new MainWindow(path);
        main.Show();
        if (first) StartServer(main);
    }

    private static bool TrySend(string path)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(1500);
            using var w = new StreamWriter(c) { AutoFlush = true };
            w.WriteLine(path);
            return true;
        }
        catch { return false; }
    }

    private void StartServer(MainWindow main)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    s.WaitForConnection();
                    using var r = new StreamReader(s);
                    var p = r.ReadLine();
                    Dispatcher.BeginInvoke(() => main.ActivateFromOtherInstance(p));
                }
                catch { Thread.Sleep(500); }
            }
        }) { IsBackground = true, Name = "single-instance" };
        t.Start();
    }
}
