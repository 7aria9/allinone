using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;
using AllInOne.Core.Localization;

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
        string[] names = ["signed-contract.pdf", "annual-report.pdf", "invoices-2025.pdf", "brochure.pdf", "lecture-notes.pdf", "site-photos.pdf"];
        var states = Enum.GetValues<MediaState>();
        for (var i = 0; i < states.Length; i++)
        {
            var item = new MediaItem(Path.Combine(Path.GetTempPath(), names[i % names.Length]), "📄");
            item.SetInfoText(L.T("{0} page(s) · {1}", 12, MediaInfo.FormatSize(8_800_000)));
            item.Reset(states[i]);
            item.Progress = 0.42;
            item.StatusText = states[i] == MediaState.Done
                ? $"✓ {MediaInfo.FormatSize(8_800_000)} → {MediaInfo.FormatSize(1_900_000)} (−78 %)"
                : MediaItems.SampleStatus(states[i]);
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
        if (skipped > 0) MainWindow.Of(this).SetStatus(L.T("{0} fichier(s) ignoré(s) : ce ne sont pas des PDF.", skipped));
    }

    private static async Task LoadInfoAsync(MediaItem item)
    {
        try
        {
            var (pages, size) = await Task.Run(() => (PdfInfo.GetPageCount(item.Path), new FileInfo(item.Path).Length));
            item.SetInfoText(L.T("{0} page(s) · {1}", pages, MediaInfo.FormatSize(size)));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Lecture du PDF " + item.Path);
            item.SetInfoText(L.T("PDF illisible ou protégé par mot de passe"));
        }
    }

    private async void Add_Click(object? sender, RoutedEventArgs e) =>
        AddFiles((await Dialogs.OpenFiles(this, L.T("Choisir des PDF à compresser"), true, Dialogs.Pdf)), _items.Count);

    private void Remove_Click(object? sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object? sender, RoutedEventArgs e) => _items.Clear();

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Dialogs.PickFolder(this, L.T("Dossier où enregistrer les PDF compressés"), OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private async void Compress_Click(object? sender, RoutedEventArgs e)
    {
        var owner = MainWindow.Of(this);
        if (_items.Count == 0) { await Dialog.Show(owner, L.T("Ajoutez au moins un PDF."), "LocalConvert"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { await Dialog.Show(owner, L.T("Choisissez un dossier de sortie."), "LocalConvert"); return; }
        if (_items.Any(i => string.Equals(Path.GetFullPath(i.Folder), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase)))
        {
            await Dialog.Show(owner, L.T("Choisissez un dossier de sortie différent de celui des originaux, pour ne pas les écraser."), "LocalConvert");
            return;
        }

        var level = Enum.Parse<PdfCompressionLevel>((string)((ComboBoxItem)Level.SelectedItem).Tag);
        var items = _items.ToList();
        var window = MainWindow.Of(this);
        long before = 0, after = 0;
        MediaItems.Result? result = null;
        var ok = await window.RunBusyAsync(L.T("Compression de {0} PDF…", items.Count),
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, ".pdf",
                async (input, output, progress, token) =>
                {
                    var r = await Task.Run(() => PdfCompressor.Compress(input, output, level, progress, token), token);
                    before += r.OriginalBytes;
                    after += r.CompressedBytes;
                    return r.KeptOriginal
                        ? L.T("✓ Déjà optimisé : copié tel quel")
                        : $"✓ {MediaInfo.FormatSize(r.OriginalBytes)} → {MediaInfo.FormatSize(r.CompressedBytes)} (−{r.Saving:0 %})";
                },
                p, window.SetStatus, Files.ScrollIntoView, ct),
            L.T("Compression terminée."), cancellable: true);
        if (!ok || result is null) return;

        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            await Dialog.Show(owner, L.T("{0} PDF n'ont pas pu être compressés :\n\n{1}", result.Failures.Count, details), "LocalConvert",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        var saved = before == 0 ? 0 : 1 - (double)after / before;
        window.SetStatus(L.T("{0} PDF compressé(s) : {1} → {2} (−{3:0 %}).", count, MediaInfo.FormatSize(before), MediaInfo.FormatSize(after), saved));
        await window.OfferToOpen(outputDir, L.T("{0} PDF compressé(s) dans {1}.\nTaille totale : {2} → {3}.", count, outputDir, MediaInfo.FormatSize(before), MediaInfo.FormatSize(after)));
    }
}
