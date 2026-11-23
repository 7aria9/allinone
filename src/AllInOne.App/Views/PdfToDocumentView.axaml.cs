using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core;
using AllInOne.Core.Documents;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;
using AllInOne.Core.Localization;

namespace AllInOne.App.Views;

/// <summary>PDF ou image → document modifiable : Word (.docx), OpenDocument (.odt) ou texte brut (.txt), avec OCR des scans.</summary>
public partial class PdfToDocumentView : UserControl, IMediaListView
{
    private readonly ObservableCollection<MediaItem> _items = new();

    public PdfToDocumentView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.IsVisible = _items.Count == 0;
        ListTools.EnableDragDrop(Files, _items, AddFiles);
    }

    void IMediaListView.AddSmokeTestSamples()
    {
        string[] names = ["receipt-photo.jpg", "thesis-chapter-2.pdf", "meeting-minutes.pdf", "old-scan.pdf", "user-manual.pdf", "rental-agreement.pdf"];
        var states = Enum.GetValues<MediaState>();
        for (var i = 0; i < states.Length; i++)
        {
            var name = names[i % names.Length];
            var item = new MediaItem(Path.Combine(Path.GetTempPath(), name), name.EndsWith(".pdf") ? "📄" : "🖼");
            item.SetInfoText(name.EndsWith(".pdf") ? L.T("{0} page(s) · {1}", 12, MediaInfo.FormatSize(1_400_000))
                : L.T("Image · {0}", MediaInfo.FormatSize(2_300_000)));
            item.Reset(states[i]);
            item.Progress = 0.42;
            item.StatusText = states[i] == MediaState.Done
                ? L.T("✓ {0} page(s), {1} paragraphe(s)", 12, 87) + L.T(" · OCR : {0} page(s), {1} image(s)", 3, 1)
                : MediaItems.SampleStatus(states[i]);
            _items.Add(item);
        }
    }

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(p => PdfComposer.IsPdf(p) || ImageFormats.IsSupportedImage(p))
            .Select(p => new MediaItem(p, PdfComposer.IsPdf(p) ? "📄" : "🖼")).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        foreach (var item in added) _ = LoadInfoAsync(item);
        if (string.IsNullOrWhiteSpace(OutputDir.Text) && added.Count > 0)
            OutputDir.Text = added[0].Folder;
        if (skipped > 0) MainWindow.Of(this).SetStatus(L.T("{0} fichier(s) ignoré(s) : ce ne sont ni des PDF ni des images.", skipped));
    }

    private static async Task LoadInfoAsync(MediaItem item)
    {
        if (!PdfComposer.IsPdf(item.Path))
        {
            try { item.SetInfoText(L.T("Image · {0}", MediaInfo.FormatSize(new FileInfo(item.Path).Length))); }
            catch (IOException) { item.SetInfoText(L.T("Image")); }
            return;
        }
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
        AddFiles(await Dialogs.OpenFiles(this, L.T("Choisir des PDF ou des images à convertir"), true, Dialogs.PdfOrImages, Dialogs.Pdf, Dialogs.Images), _items.Count);

    private void Remove_Click(object? sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object? sender, RoutedEventArgs e) => _items.Clear();

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Dialogs.PickFolder(this, L.T("Dossier où enregistrer les documents"), OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        var owner = MainWindow.Of(this);
        if (_items.Count == 0) { await Dialog.Show(owner, L.T("Ajoutez au moins un fichier."), "LocalConvert"); return; }
        var outputDir = OutputDir.Text?.Trim() ?? "";
        if (outputDir.Length == 0) { await Dialog.Show(owner, L.T("Choisissez un dossier de sortie."), "LocalConvert"); return; }

        var format = Enum.Parse<TextDocumentFormat>((string)((ComboBoxItem)Format.SelectedItem!).Tag!);
        var useOcr = UseOcr.IsChecked == true;
        if (useOcr && await Task.Run(() => TextRecognizer.UnavailableReason) is { } why)
        {
            // OCR impossible ici (Mac Intel, par exemple) : on continue avec le texte des PDF seulement.
            await Dialog.Show(owner, why + "\n\n" + L.T("Seul le texte déjà présent dans les PDF sera récupéré."), "LocalConvert",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            useOcr = false;
        }
        var options = new TextExtractionOptions { Ocr = useOcr };
        var items = _items.ToList();
        var withoutText = 0;
        var readByOcr = 0;
        MediaItems.Result? result = null;
        var ok = await owner.RunBusyAsync(L.T("Conversion de {0} fichier(s)…", items.Count),
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, TextDocumentFormats.Extension(format),
                async (input, output, progress, token) =>
                {
                    var r = await Task.Run(() => PdfToDocument.Convert(input, output, format, options, progress, token), token);
                    withoutText += r.PagesWithoutText;
                    readByOcr += r.PagesReadByOcr + r.ImagesReadByOcr;
                    var text = L.T("✓ {0} page(s), {1} paragraphe(s)", r.Pages, r.Paragraphs);
                    if (r.PagesReadByOcr + r.ImagesReadByOcr > 0)
                        text += L.T(" · OCR : {0} page(s), {1} image(s)", r.PagesReadByOcr, r.ImagesReadByOcr);
                    if (r.PagesWithoutText > 0) text += L.T(" · {0} page(s) sans texte", r.PagesWithoutText);
                    return text;
                },
                p, owner.SetStatus, Files.ScrollIntoView, ct),
            L.T("Conversion terminée."), cancellable: true);
        if (!ok || result is null) return;

        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            await Dialog.Show(owner, L.T("{0} fichier(s) n'ont pas pu être convertis :\n\n{1}", result.Failures.Count, details), "LocalConvert",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        var message = L.T("{0} document(s) créé(s) dans {1}.", count, outputDir);
        if (readByOcr > 0)
            message += "\n" + L.T("Texte lu par OCR sur {0} page(s) ou image(s) : relis-le, des erreurs sont possibles.", readByOcr);
        if (withoutText > 0)
            message += "\n" + L.T("{0} page(s) sans texte reconnu sont restées vides.", withoutText);
        owner.SetStatus(L.T("{0} document(s) créé(s) dans {1}.", count, outputDir));
        await owner.OfferToOpen(count == 1 ? result.Written[0] : outputDir, message);
    }
}
