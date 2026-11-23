using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core.Video;
using AllInOne.Core.Localization;

namespace AllInOne.App.Views;

public partial class ConvertVideosView : UserControl, IMediaListView
{

    private readonly ObservableCollection<MediaItem> _items = new();

    public ConvertVideosView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.IsVisible = _items.Count == 0;
        ListTools.EnableDragDrop(Files, _items, AddFiles);
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) RefreshFfmpegStatus(); };
        Loaded += (_, _) => RefreshFfmpegStatus();
    }

    void IMediaListView.AddSmokeTestSamples()
    {
        foreach (var item in MediaItems.SmokeTestSamples(isVideo: true)) _items.Add(item);
    }

    private void RefreshFfmpegStatus()
    {
        FfmpegSetup.RefreshStatus(FfmpegStatus, DownloadButton);
        MediaItems.LoadInfo(_items); // par exemple juste après l'installation de FFmpeg
    }

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(VideoFormats.IsSupportedVideo).Select(p => new MediaItem(p, isVideo: true)).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        MediaItems.LoadInfo(added);
        if (string.IsNullOrWhiteSpace(OutputDir.Text) && added.Count > 0)
            OutputDir.Text = Path.Combine(added[0].Folder, "converties");
        if (skipped > 0) MainWindow.Of(this).SetStatus(L.T("{0} fichier(s) ignoré(s) : format vidéo non reconnu.", skipped));
    }

    private async void Add_Click(object? sender, RoutedEventArgs e) =>
        AddFiles(await Dialogs.OpenFiles(this, L.T("Choisir des vidéos"), true, Dialogs.Type(L.T("Vidéos"), VideoFormats.InputExtensions)), _items.Count);

    private void Remove_Click(object? sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object? sender, RoutedEventArgs e) => _items.Clear();

    private string SelectedTag => (string)((ComboBoxItem)Format.SelectedItem!).Tag!;

    /// <summary>Extraction des images (PNG ou JPEG) au lieu d'une conversion vidéo.</summary>
    private bool ExtractFrames => SelectedTag.StartsWith("Frames", StringComparison.Ordinal);

    private VideoFormat SelectedFormat => ExtractFrames ? VideoFormat.Mp4 : Enum.Parse<VideoFormat>(SelectedTag);

    private void Copy_Changed(object? sender, RoutedEventArgs e) => UpdateOptions();
    private void Format_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateOptions();

    /// <summary>Le GIF n'a pas de son et se réencode toujours ; la copie des flux ignore qualité et résolution.</summary>
    private void UpdateOptions()
    {
        if (CopyStreams is null || RemoveAudio is null || FrameRate is null) return; // pendant InitializeComponent
        var frames = ExtractFrames;
        var gif = !frames && SelectedFormat == VideoFormat.Gif;
        CopyStreams.IsEnabled = RemoveAudio.IsEnabled = !gif && !frames;
        var reencode = gif || frames || CopyStreams.IsChecked != true;
        Resolution.IsEnabled = reencode;
        Quality.IsEnabled = reencode && SelectedTag != "FramesPng";
        FrameRate.IsVisible = FrameRateLabel.IsVisible = frames;
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Dialogs.PickFolder(this, L.T("Dossier où enregistrer les vidéos converties"), OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private async void Download_Click(object? sender, RoutedEventArgs e)
    {
        await FfmpegSetup.DownloadAsync(this);
        RefreshFfmpegStatus();
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        var owner = MainWindow.Of(this);
        if (_items.Count == 0) { await Dialog.Show(owner, L.T("Ajoutez au moins une vidéo."), "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { await Dialog.Show(owner, L.T("Choisissez un dossier de sortie."), "AllInOne"); return; }

        var (trimOk, trimStart, trimEnd) = await MediaItems.ReadTrimAsync(TrimStart, TrimEnd, owner);
        if (!trimOk) return;

        var ready = await FfmpegSetup.EnsureAsync(this);
        RefreshFfmpegStatus();
        if (!ready) return;

        if (ExtractFrames)
        {
            await ExtractFramesAsync(outputDir, trimStart, trimEnd);
            return;
        }

        var format = SelectedFormat;
        var height = int.Parse((string)((ComboBoxItem)Resolution.SelectedItem).Tag);
        var options = new VideoConversionOptions
        {
            Quality = Enum.Parse<VideoQuality>((string)((ComboBoxItem)Quality.SelectedItem).Tag),
            MaxHeight = height > 0 ? height : null,
            CopyStreams = CopyStreams.IsChecked == true && format != VideoFormat.Gif,
            RemoveAudio = RemoveAudio.IsChecked == true,
            Start = trimStart,
            End = trimEnd,
        };
        var items = _items.ToList();
        var window = MainWindow.Of(this);
        MediaItems.Result? result = null;
        var ok = await window.RunBusyAsync(L.T("Conversion de {0} vidéo(s)…", items.Count),
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, format.Extension(),
                (input, output, progress, token) => VideoConverter.ConvertAsync(input, output, format, options, progress, token),
                p, window.SetStatus, Files.ScrollIntoView, ct, trimStart, trimEnd),
            L.T("Vidéos converties dans {0}", outputDir), cancellable: true);
        if (!ok || result is null) return;
        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            window.SetStatus(L.T("{0} fichier(s) converti(s), {1} en échec.", count, result.Failures.Count));
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            if (result.Failures.Count > 5) details += L.T("\n\n… et {0} autre(s).", result.Failures.Count - 5);
            await Dialog.Show(owner, L.T("{0} fichier(s) n'ont pas pu être convertis :\n\n{1}", result.Failures.Count, details), "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        await window.OfferToOpen(outputDir, L.T("{0} vidéo(s) convertie(s) dans {1}.", count, outputDir));
    }

    private async Task ExtractFramesAsync(string outputDir, TimeSpan? trimStart, TimeSpan? trimEnd)
    {
        var owner = MainWindow.Of(this);
        var height = int.Parse((string)((ComboBoxItem)Resolution.SelectedItem).Tag);
        var rate = double.Parse((string)((ComboBoxItem)FrameRate.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);
        var quality = Enum.Parse<VideoQuality>((string)((ComboBoxItem)Quality.SelectedItem).Tag);
        var options = new FrameExtractionOptions
        {
            Format = SelectedTag == "FramesJpeg" ? AllInOne.Core.ImageFormatKind.Jpeg : AllInOne.Core.ImageFormatKind.Png,
            FramesPerSecond = rate > 0 ? rate : null,
            JpegQuality = quality switch { VideoQuality.High => 95, VideoQuality.Low => 70, _ => 85 },
            MaxHeight = height > 0 ? height : null,
            Start = trimStart,
            End = trimEnd,
        };

        var estimate = _items.Sum(i => i.Media is { } m ? VideoFrames.EstimateCount(m, options) : 0);
        if (estimate > 3000 && await Dialog.Show(owner,
                L.T("Cela va créer environ {0:N0} images.\n\nContinuer ? Tu peux en garder moins avec « 1 image par seconde » ou en choisissant un passage.", estimate),
                L.T("Beaucoup d'images"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var items = _items.ToList();
        var window = MainWindow.Of(this);
        var total = 0;
        MediaItems.Result? result = null;
        var ok = await window.RunBusyAsync(L.T("Extraction des images de {0} vidéo(s)…", items.Count),
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, "",
                async (input, _, progress, token) =>
                {
                    var folder = AllInOne.Core.FileNames.UniqueDirectory(
                        Path.Combine(outputDir, Path.GetFileNameWithoutExtension(input) + "_images"));
                    var files = await VideoFrames.ExtractAsync(input, folder, options, progress, token);
                    total += files.Count;
                    return L.T("✓ {0} image(s) dans « {1} »", files.Count, Path.GetFileName(folder));
                },
                p, window.SetStatus, Files.ScrollIntoView, ct, trimStart, trimEnd),
            L.T("Extraction terminée."), cancellable: true);
        if (!ok || result is null) return;

        if (result.Failures.Count > 0)
        {
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            await Dialog.Show(owner, L.T("{0} vidéo(s) n'ont pas pu être traitées :\n\n{1}", result.Failures.Count, details), "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (result.Written.Count == 0) return;
        }
        window.SetStatus(L.T("{0} image(s) extraite(s) de {1} vidéo(s).", total, result.Written.Count));
        await window.OfferToOpen(outputDir, L.T("{0} image(s) extraite(s) dans {1}.", total, outputDir));
    }
}
