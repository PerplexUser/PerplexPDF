using System.Windows;

namespace PerplexPDF.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow = new MainWindow(e.Args);
        MainWindow.Show();
    }
}
