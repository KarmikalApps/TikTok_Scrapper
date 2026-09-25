using System.Windows;
namespace TikTokScrapper;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        if (e.Args.Length == 2 && e.Args[0] == "--render-preview")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await window.RenderPreviewsAsync(e.Args[1]);
            Shutdown();
            return;
        }
        MainWindow = window;
        window.Show();
    }
}
