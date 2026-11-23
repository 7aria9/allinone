using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

public partial class SplitPdfView : UserControl, IMediaListView
{
    private readonly ObservableCollection<MediaItem> _results = new();
    private int _pageCount;

    public SplitPdfView()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
        _results.CollectionChanged += (_, _) => EmptyHint.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void IMediaListView.AddSmokeTestSamples()
    {
        var item = new MediaItem(Environment.ProcessPath ?? "exemple.pdf", "📄");
        item.SetInfoText("3 pages · 120 Ko");
        _results.Add(item);
        Mode.SelectedIndex = 2;
    }

    private string SelectedMode => (string)((ComboBoxItem)Mode.SelectedItem).Tag;

    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EveryPanel is null || RangesPanel is null) return; // pendant InitializeComponent
        EveryPanel.Visibility = SelectedMode == "Every" ? Visibility.Visible : Visibility.Collapsed;
        RangesPanel.Visibility = SelectedMode == "Ranges" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowsePdf_Click(object sender, RoutedEventArgs e)
    {
        var file = Dialogs.OpenFiles(Dialogs.PdfFilter, "Choisir un PDF à découper", multiple: false).FirstOrDefault();
        if (file is not null) await LoadPdfAsync(file);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Dossier où enregistrer les morceaux", OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(PdfComposer.IsPdf) is { } pdf)
            await LoadPdfAsync(pdf);
    }

    private async Task LoadPdfAsync(string file)
    {
        try
        {
            var (count, size) = await Task.Run(() => (PdfInfo.GetPageCount(file), new FileInfo(file).Length));
            _pageCount = count;
            PdfPath.Text = file;
            PageCount.Text = $"{count} page(s) · {MediaInfo.FormatSize(size)}";
            _results.Clear();
            OutputDir.Text = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "_découpé");
        }
        catch (Exception ex)
        {
            Dialog.Show(Window.GetWindow(this), $"Impossible d'ouvrir ce PDF : {ex.Message}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var pdf = PdfPath.Text;
        if (string.IsNullOrWhiteSpace(pdf) || !File.Exists(pdf)) { Dialog.Show(owner, "Choisissez d'abord un PDF.", "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne"); return; }

        var mode = SelectedMode;
        var every = 0;
        if (mode == "Every" && (!int.TryParse(EveryCount.Text.Trim(), out every) || every < 1))
        {
            Dialog.Show(owner, "Indiquez un nombre de pages par fichier (1 ou plus).", "AllInOne");
            return;
        }
        var ranges = Ranges.Text;
        if (mode == "Ranges" && string.IsNullOrWhiteSpace(ranges))
        {
            Dialog.Show(owner, "Indiquez les plages de pages, par exemple « 1-3, 4-7, 8- ».", "AllInOne");
            return;
        }

        var window = MainWindow.Of(this);
        IReadOnlyList<string> files = Array.Empty<string>();
        var ok = await window.RunBusyAsync("Découpage du PDF…", async p =>
        {
            files = await Task.Run(() => mode switch
            {
                "Every" => PdfTools.SplitEvery(pdf, outputDir, every, p),
                "Ranges" => PdfTools.SplitByRanges(pdf, outputDir, ranges, p),
                _ => PdfTools.SplitEvery(pdf, outputDir, 1, p),
            });
        }, null);
        if (!ok) return;

        _results.Clear();
        foreach (var file in files)
        {
            var item = new MediaItem(file, "📄");
            try
            {
                item.SetInfoText($"{PdfInfo.GetPageCount(file)} page(s) · {MediaInfo.FormatSize(new FileInfo(file).Length)}");
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "Lecture de " + file);
            }
            _results.Add(item);
        }
        window.SetStatus($"{files.Count} fichier(s) créé(s) à partir de {_pageCount} page(s).");
        window.OfferToOpen(outputDir, $"{files.Count} PDF créé(s) dans {outputDir}.");
    }
}
