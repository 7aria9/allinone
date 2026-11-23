using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AllInOne.Core;
using AllInOne.Core.Pdf;

namespace AllInOne.App.Views;

/// <summary>Une page dans l'éditeur : son origine (page de PDF ou image), sa rotation et sa miniature.</summary>
public sealed class PageItem : Observable
{
    private ImageSource? _thumbnail;
    private int _rotation;
    private int _number;

    public PageItem(PageSource source) => Source = source;

    public PageSource Source { get; }
    public string FilePath => Source.FilePath;

    public string Origin => Source switch
    {
        PdfPageSource p => $"{Path.GetFileName(p.Path)} · p. {p.PageIndex + 1}",
        ImagePageSource { FrameIndex: > 0 } i => $"{Path.GetFileName(i.Path)} · {i.FrameIndex + 1}",
        _ => Path.GetFileName(Source.FilePath),
    };

    public int Number { get => _number; set => Set(ref _number, value); }

    /// <summary>Rotation ajoutée par l'utilisateur (0, 90, 180, 270).</summary>
    public int Rotation { get => _rotation; set => Set(ref _rotation, PdfAngles.Normalize(value)); }

    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

    public PageSource ToSource() => Source with { Rotation = Rotation };
}

internal static class PdfAngles
{
    public static int Normalize(int degrees) => ((degrees % 360) + 360) % 360;
}

public partial class PdfEditorView : UserControl
{
    private readonly ObservableCollection<PageItem> _pages = new();
    private readonly ConcurrentDictionary<string, byte[]> _pdfCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _firstPdf;

    public PdfEditorView()
    {
        InitializeComponent();
        Pages.ItemsSource = _pages;
        _pages.CollectionChanged += Pages_CollectionChanged;
        ListTools.EnableDragDrop(Pages, _pages, (files, index) => _ = AddFilesAsync(files, index));
    }

    private void Pages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < _pages.Count; i++) _pages[i].Number = i + 1;
        var files = _pages.Select(p => p.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Summary.Text = _pages.Count == 0 ? "Aucune page." : $"{_pages.Count} page(s) provenant de {files} fichier(s).";
        if (_pages.Count == 0) { _firstPdf = null; _pdfCache.Clear(); }
    }

    private int InsertionIndex() =>
        Pages.SelectedItems.Count == 0 ? _pages.Count : Pages.SelectedItems.Cast<PageItem>().Max(_pages.IndexOf) + 1;

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var files = Dialogs.OpenFiles(Dialogs.PdfOrImageFilter, "Choisir des PDF ou des images");
        if (files.Length > 0) await AddFilesAsync(files, InsertionIndex());
    }

    private async Task AddFilesAsync(string[] files, int index)
    {
        var supported = files.Where(f => PdfComposer.IsPdf(f) || ImageFormats.IsSupportedImage(f)).ToArray();
        if (supported.Length == 0) return;
        var window = MainWindow.Of(this);
        IReadOnlyList<PageSource> sources = Array.Empty<PageSource>();
        var ok = await window.RunBusyAsync("Lecture des fichiers…", _ => Task.Run(() =>
        {
            sources = PdfComposer.ExpandFiles(supported);
            foreach (var pdf in supported.Where(PdfComposer.IsPdf))
                _pdfCache.TryAdd(Path.GetFullPath(pdf), File.ReadAllBytes(pdf));
        }), $"{supported.Length} fichier(s) ajouté(s).");
        if (!ok) return;

        _firstPdf ??= supported.FirstOrDefault(PdfComposer.IsPdf);
        var items = sources.Select(s => new PageItem(s)).ToList();
        index = Math.Clamp(index, 0, _pages.Count);
        for (var i = 0; i < items.Count; i++) _pages.Insert(index + i, items[i]);
        await LoadThumbnailsAsync(items);
    }

    private async Task LoadThumbnailsAsync(IEnumerable<PageItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                var source = item.Source;
                var png = await Task.Run(() => source switch
                {
                    PdfPageSource p => PdfRenderer.RenderThumbnailPng(
                        _pdfCache.GetOrAdd(Path.GetFullPath(p.Path), File.ReadAllBytes), p.PageIndex, 260),
                    ImagePageSource i => PdfRenderer.RenderImageThumbnailPng(i.Path, 260, i.FrameIndex),
                    _ => throw new NotSupportedException(),
                });
                item.Thumbnail = Thumbnails.FromPng(png);
            }
            catch
            {
                // Page illisible : la vignette reste vide, l'enregistrement signalera l'erreur s'il y a lieu.
            }
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => ListTools.RemoveSelected(Pages, _pages);

    private void Pages_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { ListTools.RemoveSelected(Pages, _pages); e.Handled = true; }
    }

    private void Rotate(int degrees)
    {
        foreach (var page in Pages.SelectedItems.Cast<PageItem>()) page.Rotation += degrees;
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => Rotate(-90);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => Rotate(90);
    private void Up_Click(object sender, RoutedEventArgs e) => ListTools.MoveSelected(Pages, _pages, -1);
    private void Down_Click(object sender, RoutedEventArgs e) => ListTools.MoveSelected(Pages, _pages, +1);
    private void Start_Click(object sender, RoutedEventArgs e) => ListTools.MoveSelectedToEdge(Pages, _pages, toStart: true);
    private void End_Click(object sender, RoutedEventArgs e) => ListTools.MoveSelectedToEdge(Pages, _pages, toStart: false);
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Pages.SelectAll();
    private void Clear_Click(object sender, RoutedEventArgs e) => _pages.Clear();

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < _pages.Count / 2; i++)
        {
            var j = _pages.Count - 1 - i;
            _pages.Move(j, i);
            _pages.Move(i + 1, j);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Le document ne contient aucune page.", "AllInOne");
            return;
        }

        var suggested = _firstPdf is null ? "document.pdf" : Path.GetFileNameWithoutExtension(_firstPdf) + "_modifie.pdf";
        var output = Dialogs.SavePdf(suggested);
        if (output is null) return;

        var sources = _pages.Select(p => p.ToSource()).ToList();
        var options = new ImagePageOptions
        {
            PageSize = Enum.Parse<PageSizeMode>((string)((ComboBoxItem)ImagePageSize.SelectedItem).Tag),
        };
        var window = MainWindow.Of(this);
        var ok = await window.RunBusyAsync("Enregistrement du PDF…",
            p => Task.Run(() => PdfComposer.Build(sources, output, options, p)),
            $"PDF enregistré : {output}");
        if (!ok) return;

        // Si un fichier source a été écrasé, les pages affichées n'y correspondent plus : on recharge le résultat.
        var full = Path.GetFullPath(output);
        if (sources.Any(s => string.Equals(Path.GetFullPath(s.FilePath), full, StringComparison.OrdinalIgnoreCase)))
        {
            _pages.Clear();
            await AddFilesAsync(new[] { output }, 0);
        }
        window.OfferToOpen(output, $"Le PDF a été enregistré ({sources.Count} page(s)).");
    }
}
