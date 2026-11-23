using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AllInOne.Core.Localization;

namespace AllInOne.App;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cancel;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var (code, name) in L.Languages)
            LanguageBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        LanguageBox.SelectedIndex = L.Languages.ToList().FindIndex(l => l.Code == L.Language);
        LanguageBox.SelectionChanged += Language_Changed;
        Translator.Apply(this);
    }

    /// <summary>Change de langue : la fenêtre est reconstruite dans la nouvelle langue, au même endroit.</summary>
    private void Language_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code } || code == L.Language) return;
        Settings.Language = code;
        L.SetLanguage(code);
        var page = Pages.Children.ToList().FindIndex(c => c.IsVisible);
        var replacement = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = Position,
            Width = Width,
            Height = Height,
            WindowState = WindowState,
        };
        replacement.SelectPage(page);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = replacement;
        replacement.Show();
        Close();
    }

    private void SelectPage(int index)
    {
        if (index < 0) return;
        var buttons = Nav.GetLogicalDescendants().OfType<RadioButton>().ToList();
        if (index < buttons.Count) buttons[index].IsChecked = true;
    }

    private void Nav_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string tag }) ShowPage(int.Parse(tag));
    }

    private void ShowPage(int index)
    {
        if (Pages is null) return; // pendant InitializeComponent
        for (var i = 0; i < Pages.Children.Count; i++) Pages.Children[i].IsVisible = i == index;
    }

    /// <summary>
    /// Test de démarrage : affiche chaque écran (avec des lignes d'exemple dans chaque état pour les listes)
    /// pour que leurs modèles XAML soient réellement construits, et en enregistre éventuellement une capture.
    /// </summary>
    internal async Task RenderEveryPageForSmokeTestAsync(string? screenshotDirectory)
    {
        if (screenshotDirectory is not null) Directory.CreateDirectory(screenshotDirectory);
        var names = new[] { "1-images-pdf", "2-modifier", "3-compresser", "4-decouper", "5-pdf-images", "6-images", "7-videos", "8-sons" };
        for (var i = 0; i < Pages.Children.Count; i++)
        {
            if (Pages.Children[i] is Views.IMediaListView media) media.AddSmokeTestSamples();
            SelectPage(i); // coche aussi le bouton de navigation, pour des captures fidèles
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            UpdateLayout();
            await Task.Delay(150);
            if (screenshotDirectory is not null)
            {
                var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var bitmap = new RenderTargetBitmap(size);
                bitmap.Render(this);
                bitmap.Save(Path.Combine(screenshotDirectory, (i < names.Length ? names[i] : $"page{i + 1}") + ".png"));
            }
        }
        SelectPage(0);
    }

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
        Pages.IsEnabled = Nav.IsEnabled = LanguageBox.IsEnabled = false;
        StatusText.Text = message;
        Progress.Value = 0;
        Progress.IsVisible = true;
        _cancel = new CancellationTokenSource();
        CancelButton.IsVisible = cancellable;
        CancelButton.IsEnabled = true;
        var progress = new Progress<double>(v => Progress.Value = v);
        try
        {
            await work(progress, _cancel.Token);
            StatusText.Text = successMessage ?? L.T("Terminé.");
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = L.T("Arrêté.");
            return false;
        }
        catch (Exception ex)
        {
            StatusText.Text = L.T("Échec : ") + ex.Message.Split('\n')[0];
            CrashLog.Write(ex, message);
            await Dialog.Show(this, ex.Message, "LocalConvert", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _cancel.Dispose();
            _cancel = null;
            CancelButton.IsVisible = false;
            Progress.IsVisible = false;
            Pages.IsEnabled = Nav.IsEnabled = LanguageBox.IsEnabled = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = L.T("Arrêt en cours…");
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Propose d'ouvrir le fichier ou le dossier produit (Explorateur, Finder ou gestionnaire de fichiers).</summary>
    public async Task OfferToOpen(string path, string what)
    {
        var answer = await Dialog.Show(this, L.T("{0}\n\nVoulez-vous l'ouvrir ?", what), L.T("C'est fait !"),
            MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            var opened = Directory.Exists(path) ? await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path))
                : File.Exists(path) && await Launcher.LaunchFileInfoAsync(new FileInfo(path));
            if (!opened) await Dialog.Show(this, L.T("Impossible de l'ouvrir automatiquement :\n{0}", path), "LocalConvert");
        }
        catch (Exception ex)
        {
            await Dialog.Show(this, ex.Message, "LocalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static MainWindow Of(Visual child) =>
        TopLevel.GetTopLevel(child) as MainWindow
        ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow
        ?? throw new InvalidOperationException("Fenêtre principale introuvable.");
}
