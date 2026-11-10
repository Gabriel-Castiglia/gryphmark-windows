using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace Quillforge;

public partial class App : Application
{
    private static readonly string PipeName = "Quillforge-" + Environment.UserName;
    private Mutex? _mutex;

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
        var path = e.Args.FirstOrDefault(a => !a.StartsWith('-'));
        if (path != null) path = Path.GetFullPath(path);
        bool newWindow = e.Args.Contains("--new-window");

        // Single instance: hand the file to the running Quillforge and leave
        _mutex = new Mutex(true, "Local\\" + PipeName, out bool first);
        if (!first && !newWindow && TrySend(path ?? ""))
        {
            Shutdown();
            return;
        }
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
