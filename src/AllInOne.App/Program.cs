using Avalonia;

namespace AllInOne.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.InstallGlobalHandlers(args);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            App.ReportFatal(ex, "Démarrage de l'application");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .LogToTrace();
}
