using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AllInOne.Core;
using AllInOne.Core.Images;
using AllInOne.Core.Pdf;

namespace AllInOne.App.Views;

public partial class PdfToImagesView : UserControl
{
    private int _pageCount;

    public PdfToImagesView()
    {
        InitializeComponent();
        FormatChoices.Fill(Format, ImageFormatKind.Png);
    }

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Quality is null) return; // appelé pendant InitializeComponent
        var lossy = FormatChoices.Selected(Format).SupportsQuality();
        Quality.IsEnabled = QualityLabel.IsEnabled = lossy;
    }

    private async void BrowsePdf_Click(object sender, RoutedEventArgs e)
    {
        var file = Dialogs.OpenFiles(Dialogs.PdfFilter, "Choisir un PDF", multiple: false).FirstOrDefault();
        if (file is not null) await LoadPdfAsync(file);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Dossier où enregistrer les images", OutputDir.Text);
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
            var (count, png) = await Task.Run(() =>
            {
                var c = PdfInfo.GetPageCount(file);
                return (c, PdfRenderer.RenderThumbnailPng(file, 0, 600));
            });
            _pageCount = count;
            PdfPath.Text = file;
            PageCount.Text = $"sur {count} page(s)";
            Preview.Source = Thumbnails.FromPng(png);
            if (string.IsNullOrWhiteSpace(OutputDir.Text))
                OutputDir.Text = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "_images");
        }
        catch (Exception ex)
        {
            Dialog.Show(Window.GetWindow(this), $"Impossible d'ouvrir ce PDF : {ex.Message}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var pdf = PdfPath.Text;
        if (string.IsNullOrWhiteSpace(pdf) || !File.Exists(pdf))
        {
            Dialog.Show(owner, "Choisissez d'abord un fichier PDF.", "AllInOne");
            return;
        }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0)
        {
            Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne");
            return;
        }
        var dpi = Dpi.SelectedItem is ComboBoxItem { Tag: string t } ? int.Parse(t, CultureInfo.InvariantCulture) : 150;

        IReadOnlyList<int> pages;
        try
        {
            pages = AllInOne.Core.PageRange.Parse(PageText.Text, _pageCount);
        }
        catch (Exception ex)
        {
            Dialog.Show(owner, ex.Message, "AllInOne");
            return;
        }

        var format = FormatChoices.Selected(Format);
        var options = new ImageConversionOptions { Quality = (int)Quality.Value };
        var window = MainWindow.Of(this);
        var count = 0;
        var ok = await window.RunBusyAsync($"Conversion de {pages.Count} page(s)…",
            p => Task.Run(() => count = PdfRenderer.ToImages(pdf, outputDir, format, dpi, pages, options, progress: p).Count),
            $"Images enregistrées dans {outputDir}");
        if (ok) window.OfferToOpen(outputDir, $"{count} image(s) créée(s) dans {outputDir}.");
    }
}
