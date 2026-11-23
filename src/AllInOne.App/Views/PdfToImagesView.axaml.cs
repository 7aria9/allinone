using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Globalization;
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
        Dialogs.AcceptFileDrop(Root, async files =>
        {
            if (files.FirstOrDefault(PdfComposer.IsPdf) is { } pdf) await LoadPdfAsync(pdf);
        });
        FormatChoices.Fill(Format, ImageFormatKind.Png);
    }

    private void Format_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Quality is null) return; // appelé pendant InitializeComponent
        var lossy = FormatChoices.Selected(Format).SupportsQuality();
        Quality.IsEnabled = QualityLabel.IsEnabled = lossy;
    }

    private async void BrowsePdf_Click(object? sender, RoutedEventArgs e)
    {
        var file = (await Dialogs.OpenFiles(this, "Choisir un PDF", false, Dialogs.Pdf)).FirstOrDefault();
        if (file is not null) await LoadPdfAsync(file);
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Dialogs.PickFolder(this, "Dossier où enregistrer les images", OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
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
            await Dialog.Show(MainWindow.Of(this), $"Impossible d'ouvrir ce PDF : {ex.Message}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        var owner = MainWindow.Of(this);
        var pdf = PdfPath.Text;
        if (string.IsNullOrWhiteSpace(pdf) || !File.Exists(pdf))
        {
            await Dialog.Show(owner, "Choisissez d'abord un fichier PDF.", "AllInOne");
            return;
        }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0)
        {
            await Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne");
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
            await Dialog.Show(owner, ex.Message, "AllInOne");
            return;
        }

        var format = FormatChoices.Selected(Format);
        var options = new ImageConversionOptions { Quality = (int)Quality.Value };
        var window = MainWindow.Of(this);
        var count = 0;
        var ok = await window.RunBusyAsync($"Conversion de {pages.Count} page(s)…",
            p => Task.Run(() => count = PdfRenderer.ToImages(pdf, outputDir, format, dpi, pages, options, progress: p).Count),
            $"Images enregistrées dans {outputDir}");
        if (ok) await window.OfferToOpen(outputDir, $"{count} image(s) créée(s) dans {outputDir}.");
    }
}
