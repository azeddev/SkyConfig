using System.Windows;

namespace SkyConfig.App;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Length > 0)
            window.OpenInitialPath(e.Args[0]);
    }
}
