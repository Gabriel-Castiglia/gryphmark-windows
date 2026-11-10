using System.Windows;

namespace Quillforge;

public partial class App : Application
{
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
        new MainWindow(path).Show();
    }
}
