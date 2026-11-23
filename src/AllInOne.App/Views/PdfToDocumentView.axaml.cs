using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core.Documents;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;
using AllInOne.Core.Localization;

namespace AllInOne.App.Views;

/// <summary>PDF → document modifiable : Word (.docx), OpenDocument (.odt) ou texte brut (.txt).</summary>
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
        string[] names = ["rental-agreement.pdf", "thesis-chapter-2.pdf", "meeting-minutes.pdf", "cv-2026.pdf", "user-manual.pdf", "old-scan.pdf"];
        var states = Enum.GetValues<MediaState>();
        for (var i = 0; i < states.Length; i++)
        {
            var item = new MediaItem(Path.Combine(Path.GetTempPath(), names[i % names.Length]), "📄");
            item.SetInfoText(L.T("{0} page(s) · {1}", 12, MediaInfo.FormatSize(1_400_000)));
            item.Reset(states[i]);
            item.Progress = 0.42;
            item.StatusText = states[i] == MediaState.Done ? L.T("✓ {0} page(s), {1} paragraphe(s)", 12, 87) : MediaItems.SampleStatus(states[i]);
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
            OutputDir.Text = added[0].Folder;
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
        AddFiles(await Dialogs.OpenFiles(this, L.T("Choisir des PDF à convertir"), true, Dialogs.Pdf), _items.Count);

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
        if (_items.Count == 0) { await Dialog.Show(owner, L.T("Ajoutez au moins un PDF."), "LocalConvert"); return; }
        var outputDir = OutputDir.Text?.Trim() ?? "";
        if (outputDir.Length == 0) { await Dialog.Show(owner, L.T("Choisissez un dossier de sortie."), "LocalConvert"); return; }

        var format = Enum.Parse<TextDocumentFormat>((string)((ComboBoxItem)Format.SelectedItem!).Tag!);
        var items = _items.ToList();
        var withoutText = 0;
        MediaItems.Result? result = null;
        var ok = await owner.RunBusyAsync(L.T("Conversion de {0} PDF…", items.Count),
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, TextDocumentFormats.Extension(format),
                async (input, output, progress, token) =>
                {
                    var r = await Task.Run(() => PdfToDocument.Convert(input, output, format, progress, token), token);
                    withoutText += r.PagesWithoutText;
                    return r.PagesWithoutText > 0
                        ? L.T("✓ {0} page(s), {1} paragraphe(s) · {2} page(s) sans texte", r.Pages, r.Paragraphs, r.PagesWithoutText)
                        : L.T("✓ {0} page(s), {1} paragraphe(s)", r.Pages, r.Paragraphs);
                },
                p, owner.SetStatus, Files.ScrollIntoView, ct),
            L.T("Conversion terminée."), cancellable: true);
        if (!ok || result is null) return;

        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            await Dialog.Show(owner, L.T("{0} PDF n'ont pas pu être convertis :\n\n{1}", result.Failures.Count, details), "LocalConvert",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        var message = L.T("{0} document(s) créé(s) dans {1}.", count, outputDir);
        if (withoutText > 0)
            message += "\n" + L.T("{0} page(s) sans texte (images ou pages scannées) sont restées vides.", withoutText);
        owner.SetStatus(L.T("{0} document(s) créé(s) dans {1}.", count, outputDir));
        await owner.OfferToOpen(count == 1 ? result.Written[0] : outputDir, message);
    }
}
