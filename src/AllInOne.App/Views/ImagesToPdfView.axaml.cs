using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.Globalization;
using AllInOne.Core;
using AllInOne.Core.Pdf;

namespace AllInOne.App.Views;

public partial class ImagesToPdfView : UserControl
{
    private readonly ObservableCollection<FileItem> _items = new();

    public ImagesToPdfView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.IsVisible = _items.Count == 0;
        ListTools.EnableDragDrop(Files, _items, AddFiles);
    }

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(ImageFormats.IsSupportedImage).Select(p => new FileItem(p)).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        if (skipped > 0) MainWindow.Of(this).SetStatus($"{skipped} fichier(s) ignoré(s) : format d'image non reconnu.");
        _ = Thumbnails.LoadImageThumbnailsAsync(added);
    }

    private async void Add_Click(object? sender, RoutedEventArgs e) =>
        AddFiles((await Dialogs.OpenFiles(this, "Choisir des images", true, Dialogs.Images)), _items.Count);

    private void Remove_Click(object? sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Up_Click(object? sender, RoutedEventArgs e) => ListTools.MoveSelected(Files, _items, -1);
    private void Down_Click(object? sender, RoutedEventArgs e) => ListTools.MoveSelected(Files, _items, +1);
    private void Clear_Click(object? sender, RoutedEventArgs e) => _items.Clear();

    private void Sort_Click(object? sender, RoutedEventArgs e)
    {
        var sorted = _items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _items.Clear();
        foreach (var item in sorted) _items.Add(item);
    }

    private async void Files_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Files.SelectedItem is not FileItem item) { Preview.Source = null; return; }
        try
        {
            var png = await Task.Run(() => PdfRenderer.RenderImageThumbnailPng(item.Path, 600));
            if (Files.SelectedItem == item) Preview.Source = Thumbnails.FromPng(png);
        }
        catch
        {
            Preview.Source = null;
        }
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            await Dialog.Show(MainWindow.Of(this), "Ajoutez au moins une image.", "AllInOne");
            return;
        }
        if (!double.TryParse(MarginBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var margin) || margin < 0)
        {
            await Dialog.Show(MainWindow.Of(this), "La marge doit être un nombre positif (en millimètres).", "AllInOne");
            return;
        }

        var options = new ImagePageOptions
        {
            PageSize = Enum.Parse<PageSizeMode>((string)((ComboBoxItem)PageSize.SelectedItem).Tag),
            Orientation = Enum.Parse<PageOrientation>((string)((ComboBoxItem)Orientation.SelectedItem).Tag),
            MarginMm = margin,
            EnlargeSmallImages = Enlarge.IsChecked == true,
        };

        var suggested = _items.Count == 1 ? Path.GetFileNameWithoutExtension(_items[0].Path) + ".pdf" : "images.pdf";
        var output = await Dialogs.SavePdf(this, suggested);
        if (output is null) return;

        var paths = _items.Select(i => i.Path).ToList();
        var window = MainWindow.Of(this);
        var ok = await window.RunBusyAsync("Création du PDF…",
            p => Task.Run(() => PdfTools.ImagesToPdf(paths, output, options, p)),
            $"PDF créé : {output}");
        if (ok) await window.OfferToOpen(output, $"Le PDF a été créé ({paths.Count} image(s)).");
    }
}
