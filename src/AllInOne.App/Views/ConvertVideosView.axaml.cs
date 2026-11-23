using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using AllInOne.Core.Video;

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
        if (skipped > 0) MainWindow.Of(this).SetStatus($"{skipped} fichier(s) ignoré(s) : format vidéo non reconnu.");
    }

    private async void Add_Click(object? sender, RoutedEventArgs e) =>
        AddFiles(await Dialogs.OpenFiles(this, "Choisir des vidéos", true, Dialogs.Type("Vidéos", VideoFormats.InputExtensions)), _items.Count);

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
        var folder = await Dialogs.PickFolder(this, "Dossier où enregistrer les vidéos converties", OutputDir.Text);
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
        if (_items.Count == 0) { await Dialog.Show(owner, "Ajoutez au moins une vidéo.", "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { await Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne"); return; }

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
        var ok = await window.RunBusyAsync($"Conversion de {items.Count} vidéo(s)…",
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, format.Extension(),
                (input, output, progress, token) => VideoConverter.ConvertAsync(input, output, format, options, progress, token),
                p, window.SetStatus, Files.ScrollIntoView, ct, trimStart, trimEnd),
            $"Vidéos converties dans {outputDir}", cancellable: true);
        if (!ok || result is null) return;
        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            window.SetStatus($"{count} fichier(s) converti(s), {result.Failures.Count} en échec.");
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            if (result.Failures.Count > 5) details += $"\n\n… et {result.Failures.Count - 5} autre(s).";
            await Dialog.Show(owner, $"{result.Failures.Count} fichier(s) n'ont pas pu être convertis :\n\n{details}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        await window.OfferToOpen(outputDir, $"{count} vidéo(s) convertie(s) dans {outputDir}.");
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
                $"Cela va créer environ {estimate:N0} images.\n\nContinuer ? Tu peux en garder moins avec « 1 image par seconde » ou en choisissant un passage.",
                "Beaucoup d'images", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var items = _items.ToList();
        var window = MainWindow.Of(this);
        var total = 0;
        MediaItems.Result? result = null;
        var ok = await window.RunBusyAsync($"Extraction des images de {items.Count} vidéo(s)…",
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, "",
                async (input, _, progress, token) =>
                {
                    var folder = AllInOne.Core.FileNames.UniqueDirectory(
                        Path.Combine(outputDir, Path.GetFileNameWithoutExtension(input) + "_images"));
                    var files = await VideoFrames.ExtractAsync(input, folder, options, progress, token);
                    total += files.Count;
                    return $"✓ {files.Count} image(s) dans « {Path.GetFileName(folder)} »";
                },
                p, window.SetStatus, Files.ScrollIntoView, ct, trimStart, trimEnd),
            "Extraction terminée.", cancellable: true);
        if (!ok || result is null) return;

        if (result.Failures.Count > 0)
        {
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            await Dialog.Show(owner, $"{result.Failures.Count} vidéo(s) n'ont pas pu être traitées :\n\n{details}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (result.Written.Count == 0) return;
        }
        window.SetStatus($"{total} image(s) extraite(s) de {result.Written.Count} vidéo(s).");
        await window.OfferToOpen(outputDir, $"{total} image(s) extraite(s) dans {outputDir}.");
    }
}
