using System.Windows;

namespace Quillforge;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var path = e.Args.FirstOrDefault(a => !a.StartsWith('-'));
        new MainWindow(path).Show();
    }
}
