using System.Windows;
using System.Windows.Threading;

namespace AllInOne.App;

public partial class App : Application
{
    /// <summary>
    /// Avec « --smoke-test », l'application s'ouvre puis se ferme d'elle-même (code 0) dès que la fenêtre principale
    /// est affichée, ou se termine avec le code 1 en cas d'erreur, sans boîte de dialogue. Sert au test de démarrage en CI.
    /// </summary>
    private bool _smokeTest;

    public App()
    {
        // Branché dans le constructeur, avant le chargement de App.xaml et de la fenêtre principale :
        // une erreur de démarrage est ainsi toujours journalisée et affichée.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(ex, "Erreur fatale", fatal: true);
        };
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "Tâche en arrière-plan");
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        DarkTitleBar.Register();
        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            if (_smokeTest)
                window.ContentRendered += (_, _) =>
                {
                    window.RenderEveryPageForSmokeTest();
                    Dispatcher.BeginInvoke(() => Shutdown(0), DispatcherPriority.ApplicationIdle);
                };
            window.Show();
        }
        catch (Exception ex)
        {
            Report(ex, "Ouverture de la fenêtre principale", fatal: true);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        // Si aucune fenêtre n'est ouverte, l'erreur empêche l'application de démarrer : on la ferme après le message.
        Report(e.Exception, "Erreur inattendue", fatal: MainWindow is null || !MainWindow.IsLoaded);
    }

    private void Report(Exception ex, string context, bool fatal)
    {
        var log = CrashLog.Write(ex, context);
        if (_smokeTest || Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(ex);
            Environment.Exit(1);
        }

        var text = CrashLog.Describe(ex) + (log is null ? "" : $"\n\nDétails enregistrés dans :\n{log}");
        try
        {
            // La boîte de dialogue du thème peut elle-même échouer si l'erreur vient des ressources : MessageBox en secours.
            if (fatal) MessageBox.Show(text, "AllInOne : erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            else Dialog.Show(text, "AllInOne : erreur inattendue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            MessageBox.Show(text, "AllInOne : erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        if (fatal) Environment.Exit(1);
    }
}
