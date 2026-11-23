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
    /// Test de démarrage : affiche chaque page (avec des lignes d'exemple dans chaque état pour les sons et vidéos)
    /// afin que leurs modèles XAML soient réellement construits. Une erreur remonte au gestionnaire global.
    /// </summary>
    internal void RenderEveryPageForSmokeTest()
    {
        for (var i = 0; i < Pages.Children.Count; i++)
        {
            if (Pages.Children[i] is Views.IMediaListView media) media.AddSmokeTestSamples();
            for (var j = 0; j < Pages.Children.Count; j++)
                Pages.Children[j].Visibility = i == j ? Visibility.Visible : Visibility.Collapsed;
            UpdateLayout();
        }
    }

    private CancellationTokenSource? _cancel;

    /// <summary>
    /// Exécute une tâche longue en arrière-plan : désactive l'interface, affiche la progression
    /// et présente les erreurs à l'utilisateur. Renvoie true si la tâche s'est terminée sans erreur.
    /// Avec <paramref name="cancellable"/>, un bouton « Arrêter » permet d'interrompre la tâche
    /// (le jeton est passé en second paramètre de <paramref name="work"/>).
    /// </summary>
    public Task<bool> RunBusyAsync(string message, Func<IProgress<double>, Task> work, string? successMessage = null) =>
        RunBusyAsync(message, (p, _) => work(p), successMessage, cancellable: false);

    public async Task<bool> RunBusyAsync(string message, Func<IProgress<double>, CancellationToken, Task> work,
        string? successMessage, bool cancellable)
    {
        Pages.IsEnabled = Nav.IsEnabled = false;
        StatusText.Text = message;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        _cancel = new CancellationTokenSource();
        CancelButton.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = true;
        var progress = new Progress<double>(v => Progress.Value = v);
        try
        {
            await work(progress, _cancel.Token);
            StatusText.Text = successMessage ?? "Terminé.";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Arrêté.";
            return false;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Échec : " + ex.Message.Split('\n')[0];
            Dialog.Show(this, ex.Message, "AllInOne", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _cancel.Dispose();
            _cancel = null;
            CancelButton.Visibility = Visibility.Collapsed;
            Progress.Visibility = Visibility.Collapsed;
            Pages.IsEnabled = Nav.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Arrêt en cours…";
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
