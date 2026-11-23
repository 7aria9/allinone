using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AllInOne.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Exécute une tâche longue en arrière-plan : désactive les onglets, affiche la progression
    /// et présente les erreurs à l'utilisateur. Renvoie true si la tâche s'est terminée sans erreur.
    /// </summary>
    public async Task<bool> RunBusyAsync(string message, Func<IProgress<double>, Task> work, string? successMessage = null)
    {
        Tabs.IsEnabled = false;
        StatusText.Text = message;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(v => Progress.Value = v);
        try
        {
            await work(progress);
            StatusText.Text = successMessage ?? "Terminé.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Échec : " + ex.Message;
            MessageBox.Show(this, ex.Message, "AllInOne", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            Tabs.IsEnabled = true;
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Propose d'ouvrir le fichier ou le dossier produit.</summary>
    public void OfferToOpen(string path, string what)
    {
        var answer = MessageBox.Show(this, $"{what}\n\nVoulez-vous l'ouvrir ?", "AllInOne",
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "AllInOne", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static MainWindow Of(DependencyObject child) =>
        (MainWindow)(GetWindow(child) ?? Application.Current.MainWindow);
}
