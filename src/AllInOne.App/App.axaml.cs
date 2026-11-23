using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AllInOne.Core.Localization;

namespace AllInOne.App;

public partial class App : Application
{
    /// <summary>
    /// Avec « --smoke-test », l'application affiche chaque écran puis se ferme d'elle-même (code 0), ou se termine avec
    /// le code 1 en cas d'erreur, sans boîte de dialogue. « --screenshots &lt;dossier&gt; » y enregistre une capture de
    /// chaque écran. Sert au test de démarrage en CI. « --lang en|de|es|fr » choisit la langue pour ce lancement.
    /// </summary>
    internal static bool SmokeTest { get; private set; }
    internal static string? ScreenshotDirectory { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    internal static void InstallGlobalHandlers(string[] args)
    {
        SmokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var i = Array.FindIndex(args, a => a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length) ScreenshotDirectory = args[i + 1];
        // Langue : « --lang en » pour cette fois, sinon celle choisie dans l'application, sinon celle du système.
        var lang = Array.FindIndex(args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        L.Initialize(lang >= 0 && lang + 1 < args.Length ? args[lang + 1] : null);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ReportFatal(ex, "Erreur fatale");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "Tâche en arrière-plan");
            e.SetObserved();
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Report(e.Exception, L.T("Erreur inattendue"));
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (SmokeTest) window.Opened += async (_, _) => await RunSmokeTestAsync(window, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunSmokeTestAsync(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await window.RenderEveryPageForSmokeTestAsync(ScreenshotDirectory);
            Console.WriteLine("OK : la fenêtre principale et tous les écrans se sont affichés.");
            desktop.Shutdown(0);
        }
        catch (Exception ex)
        {
            ReportFatal(ex, "Test de démarrage");
        }
    }

    /// <summary>Erreur non fatale : journalisée puis affichée dans une boîte de dialogue.</summary>
    private static void Report(Exception ex, string context)
    {
        var log = CrashLog.Write(ex, context);
        if (SmokeTest) { Console.Error.WriteLine(ex); Environment.Exit(1); }
        var text = CrashLog.Describe(ex) + (log is null ? "" : L.T("\n\nDétails enregistrés dans :\n{0}", log));
        _ = Dialog.Show(null, text, L.T("LocalConvert : erreur inattendue"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Erreur qui empêche l'application de continuer : journalisée, écrite sur la console, puis sortie.</summary>
    internal static void ReportFatal(Exception ex, string context)
    {
        var log = CrashLog.Write(ex, context);
        Console.Error.WriteLine($"LocalConvert : {context}");
        Console.Error.WriteLine(ex);
        if (log is not null) Console.Error.WriteLine(L.T("Détails enregistrés dans : {0}", log));
        // Sous Windows, une application graphique n'a pas de console : boîte de message système, qui ne dépend pas d'Avalonia.
        if (!SmokeTest && OperatingSystem.IsWindows())
        {
            var text = CrashLog.Describe(ex) + (log is null ? "" : L.T("\n\nDétails enregistrés dans :\n{0}", log));
            try { NativeMessageBox(IntPtr.Zero, text, L.T("LocalConvert : erreur"), 0x10); } catch { /* rien de plus à faire */ }
        }
        Environment.Exit(1);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NativeMessageBox(IntPtr owner, string text, string caption, uint type);
}
