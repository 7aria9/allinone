using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AllInOne.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Pages is null || sender is not RadioButton { Tag: string tag }) return; // pendant InitializeComponent
        var index = int.Parse(tag);
        for (var i = 0; i < Pages.Children.Count; i++)
            Pages.Children[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Exécute une tâche longue en arrière-plan : désactive l'interface, affiche la progression
    /// et présente les erreurs à l'utilisateur. Renvoie true si la tâche s'est terminée sans erreur.
    /// </summary>
    public async Task<bool> RunBusyAsync(string message, Func<IProgress<double>, Task> work, string? successMessage = null)
    {
        Pages.IsEnabled = Nav.IsEnabled = false;
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
            Dialog.Show(this, ex.Message, "AllInOne", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            Pages.IsEnabled = Nav.IsEnabled = true;
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Propose d'ouvrir le fichier ou le dossier produit.</summary>
    public void OfferToOpen(string path, string what)
    {
        var answer = Dialog.Show(this, $"{what}\n\nVoulez-vous l'ouvrir ?", "C'est fait !",
            MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialog.Show(this, ex.Message, "AllInOne", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static MainWindow Of(DependencyObject child) =>
        (MainWindow)(GetWindow(child) ?? Application.Current.MainWindow);
}
