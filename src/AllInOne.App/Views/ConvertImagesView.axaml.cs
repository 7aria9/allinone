using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core;
using AllInOne.Core.Images;
using AllInOne.Core.Localization;

namespace AllInOne.App.Views;

public partial class ConvertImagesView : UserControl
{
    private readonly ObservableCollection<FileItem> _items = new();

    public ConvertImagesView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.IsVisible = _items.Count == 0;
        FormatChoices.Fill(Format, ImageFormatKind.Jpeg);
        ListTools.EnableDragDrop(Files, _items, AddFiles);
    }

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(ImageFormats.IsSupportedImage).Select(p => new FileItem(p)).ToList();
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        if (string.IsNullOrWhiteSpace(OutputDir.Text) && added.Count > 0)
            OutputDir.Text = Path.Combine(added[0].Folder, "converties");
        _ = Thumbnails.LoadImageThumbnailsAsync(added);
    }

    private void Format_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Quality is null) return; // appelé pendant InitializeComponent
        Quality.IsEnabled = QualityLabel.IsEnabled = FormatChoices.Selected(Format).SupportsQuality();
    }

    private async void Add_Click(object? sender, RoutedEventArgs e) =>
        AddFiles((await Dialogs.OpenFiles(this, L.T("Choisir des images"), true, Dialogs.Images)), _items.Count);

    private void Remove_Click(object? sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object? sender, RoutedEventArgs e) => _items.Clear();

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Dialogs.PickFolder(this, L.T("Dossier où enregistrer les images converties"), OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private static bool TryParseSize(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), out var v) && v > 0) { value = v; return true; }
        return false;
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        var owner = MainWindow.Of(this);
        if (_items.Count == 0) { await Dialog.Show(owner, L.T("Ajoutez au moins une image."), "LocalConvert"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { await Dialog.Show(owner, L.T("Choisissez un dossier de sortie."), "LocalConvert"); return; }
        if (!TryParseSize(MaxWidthBox.Text, out var maxW) || !TryParseSize(MaxHeightBox.Text, out var maxH))
        {
            await Dialog.Show(owner, L.T("Les dimensions maximales doivent être des nombres entiers positifs."), "LocalConvert");
            return;
        }

        var format = FormatChoices.Selected(Format);
        var options = new ImageConversionOptions { Quality = (int)Quality.Value, MaxWidth = maxW, MaxHeight = maxH };
        var paths = _items.Select(i => i.Path).ToList();
        var window = MainWindow.Of(this);
        var ok = await window.RunBusyAsync(L.T("Conversion de {0} image(s)…", paths.Count),
            p => Task.Run(() => ImageConverter.ConvertMany(paths, outputDir, format, options, p)),
            L.T("Images converties dans {0}", outputDir));
        if (ok) await window.OfferToOpen(outputDir, L.T("{0} image(s) convertie(s) dans {1}.", paths.Count, outputDir));
    }
}
