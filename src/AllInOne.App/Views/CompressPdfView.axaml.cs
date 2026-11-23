using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

public partial class CompressPdfView : UserControl, IMediaListView
{
    private readonly ObservableCollection<MediaItem> _items = new();

    public CompressPdfView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.IsVisible = _items.Count == 0;
        ListTools.EnableDragDrop(Files, _items, AddFiles);
    }

    void IMediaListView.AddSmokeTestSamples()
    {
        foreach (var state in Enum.GetValues<MediaState>())
        {
            var item = new MediaItem(Environment.ProcessPath ?? "exemple.pdf", "📄");
            item.SetInfoText("12 pages · 8,4 Mo");
            item.Reset(state);
            item.StatusText = state.ToString();
            _items.Add(item);
        }
    }

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(PdfComposer.IsPdf).Select(p => new MediaItem(p, "📄")).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        foreach (var item in added) _ = LoadInfoAsync(item);
        if (string.IsNullOrWhiteSpace(OutputDir.Text) && added.Count > 0)
            OutputDir.Text = Path.Combine(added[0].Folder, "compressés");
        if (skipped > 0) MainWindow.Of(this).SetStatus($"{skipped} fichier(s) ignoré(s) : ce ne sont pas des PDF.");
    }

    private static async Task LoadInfoAsync(MediaItem item)
    {
        try
        {
            var (pages, size) = await Task.Run(() => (PdfInfo.GetPageCount(item.Path), new FileInfo(item.Path).Length));
            item.SetInfoText($"{pages} page(s) · {MediaInfo.FormatSize(size)}");
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Lecture du PDF " + item.Path);
            item.SetInfoText("PDF illisible ou protégé par mot de passe");
        }
    }

    private async void Add_Click(object? sender, RoutedEventArgs e) =>
        AddFiles((await Dialogs.OpenFiles(this, "Choisir des PDF à compresser", true, Dialogs.Pdf)), _items.Count);

    private void Remove_Click(object? sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object? sender, RoutedEventArgs e) => _items.Clear();

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Dialogs.PickFolder(this, "Dossier où enregistrer les PDF compressés", OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private async void Compress_Click(object? sender, RoutedEventArgs e)
    {
        var owner = MainWindow.Of(this);
        if (_items.Count == 0) { await Dialog.Show(owner, "Ajoutez au moins un PDF.", "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { await Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne"); return; }
        if (_items.Any(i => string.Equals(Path.GetFullPath(i.Folder), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase)))
        {
            await Dialog.Show(owner, "Choisissez un dossier de sortie différent de celui des originaux, pour ne pas les écraser.", "AllInOne");
            return;
        }

        var level = Enum.Parse<PdfCompressionLevel>((string)((ComboBoxItem)Level.SelectedItem).Tag);
        var items = _items.ToList();
        var window = MainWindow.Of(this);
        long before = 0, after = 0;
        MediaItems.Result? result = null;
        var ok = await window.RunBusyAsync($"Compression de {items.Count} PDF…",
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, ".pdf",
                async (input, output, progress, token) =>
                {
                    var r = await Task.Run(() => PdfCompressor.Compress(input, output, level, progress, token), token);
                    before += r.OriginalBytes;
                    after += r.CompressedBytes;
                    return r.KeptOriginal
                        ? "✓ Déjà optimisé : copié tel quel"
                        : $"✓ {MediaInfo.FormatSize(r.OriginalBytes)} → {MediaInfo.FormatSize(r.CompressedBytes)} (−{r.Saving:0 %})";
                },
                p, window.SetStatus, Files.ScrollIntoView, ct),
            "Compression terminée.", cancellable: true);
        if (!ok || result is null) return;

        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            await Dialog.Show(owner, $"{result.Failures.Count} PDF n'ont pas pu être compressés :\n\n{details}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        var saved = before == 0 ? 0 : 1 - (double)after / before;
        window.SetStatus($"{count} PDF compressé(s) : {MediaInfo.FormatSize(before)} → {MediaInfo.FormatSize(after)} (−{saved:0 %}).");
        await window.OfferToOpen(outputDir, $"{count} PDF compressé(s) dans {outputDir}.\nTaille totale : {MediaInfo.FormatSize(before)} → {MediaInfo.FormatSize(after)}.");
    }
}
