using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AllInOne.Core;
using AllInOne.Core.Images;

namespace AllInOne.App.Views;

public partial class ConvertImagesView : UserControl
{
    private readonly ObservableCollection<FileItem> _items = new();

    public ConvertImagesView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Quality is null) return; // appelé pendant InitializeComponent
        Quality.IsEnabled = QualityLabel.IsEnabled = FormatChoices.Selected(Format).SupportsQuality();
    }

    private void Add_Click(object sender, RoutedEventArgs e) =>
        AddFiles(Dialogs.OpenFiles(Dialogs.ImageFilter, "Choisir des images"), _items.Count);

    private void Remove_Click(object sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object sender, RoutedEventArgs e) => _items.Clear();

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Dossier où enregistrer les images converties", OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private static bool TryParseSize(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), out var v) && v > 0) { value = v; return true; }
        return false;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (_items.Count == 0) { Dialog.Show(owner, "Ajoutez au moins une image.", "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne"); return; }
        if (!TryParseSize(MaxWidthBox.Text, out var maxW) || !TryParseSize(MaxHeightBox.Text, out var maxH))
        {
            Dialog.Show(owner, "Les dimensions maximales doivent être des nombres entiers positifs.", "AllInOne");
            return;
        }

        var format = FormatChoices.Selected(Format);
        var options = new ImageConversionOptions { Quality = (int)Quality.Value, MaxWidth = maxW, MaxHeight = maxH };
        var paths = _items.Select(i => i.Path).ToList();
        var window = MainWindow.Of(this);
        var ok = await window.RunBusyAsync($"Conversion de {paths.Count} image(s)…",
            p => Task.Run(() => ImageConverter.ConvertMany(paths, outputDir, format, options, p)),
            $"Images converties dans {outputDir}");
        if (ok) window.OfferToOpen(outputDir, $"{paths.Count} image(s) convertie(s) dans {outputDir}.");
    }
}
