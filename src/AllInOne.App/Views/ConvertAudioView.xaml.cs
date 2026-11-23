using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

public partial class ConvertAudioView : UserControl, IMediaListView
{
    private static readonly string AudioFilter =
        "Sons et vidéos|" + string.Join(";", AudioFormats.InputExtensions.Concat(VideoFormats.InputExtensions).Select(e => "*" + e)) +
        "|Sons|" + string.Join(";", AudioFormats.InputExtensions.Select(e => "*" + e)) + "|Tous les fichiers|*.*";

    private readonly ObservableCollection<MediaItem> _items = new();

    public ConvertAudioView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListTools.EnableDragDrop(Files, _items, AddFiles);
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshFfmpegStatus(); };
        Loaded += (_, _) => RefreshFfmpegStatus();
    }

    void IMediaListView.AddSmokeTestSamples()
    {
        foreach (var item in MediaItems.SmokeTestSamples(isVideo: false)) _items.Add(item);
    }

    private void RefreshFfmpegStatus()
    {
        FfmpegSetup.RefreshStatus(FfmpegStatus, DownloadButton);
        MediaItems.LoadInfo(_items); // par exemple juste après l'installation de FFmpeg
    }

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(AudioFormats.IsSupportedInput).Select(p => new MediaItem(p, isVideo: false)).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        MediaItems.LoadInfo(added);
        if (string.IsNullOrWhiteSpace(OutputDir.Text) && added.Count > 0)
            OutputDir.Text = Path.Combine(added[0].Folder, "converties");
        if (skipped > 0) MainWindow.Of(this).SetStatus($"{skipped} fichier(s) ignoré(s) : format non reconnu.");
    }

    private void Add_Click(object sender, RoutedEventArgs e) =>
        AddFiles(Dialogs.OpenFiles(AudioFilter, "Choisir des sons ou des vidéos"), _items.Count);

    private void Remove_Click(object sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object sender, RoutedEventArgs e) => _items.Clear();

    private AudioFormat SelectedFormat => Enum.Parse<AudioFormat>((string)((ComboBoxItem)Format.SelectedItem).Tag);

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Quality is null) return; // pendant InitializeComponent
        Quality.IsEnabled = QualityLabel.IsEnabled = !SelectedFormat.IsLossless();
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Dossier où enregistrer les sons convertis", OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        await FfmpegSetup.DownloadAsync(this);
        RefreshFfmpegStatus();
    }

    private static int? TagNumber(ComboBox combo)
    {
        var value = int.Parse((string)((ComboBoxItem)combo.SelectedItem).Tag);
        return value > 0 ? value : null;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (_items.Count == 0) { Dialog.Show(owner, "Ajoutez au moins un fichier.", "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne"); return; }

        if (!MediaItems.TryReadTrim(TrimStart, TrimEnd, owner, out var trimStart, out var trimEnd)) return;

        var ready = await FfmpegSetup.EnsureAsync(this);
        RefreshFfmpegStatus();
        if (!ready) return;

        var format = SelectedFormat;
        var options = new AudioConversionOptions
        {
            Quality = Enum.Parse<VideoQuality>((string)((ComboBoxItem)Quality.SelectedItem).Tag),
            SampleRate = TagNumber(SampleRate),
            Channels = TagNumber(Channels),
            Start = trimStart,
            End = trimEnd,
        };
        var items = _items.ToList();
        var window = MainWindow.Of(this);
        MediaItems.Result? result = null;
        var ok = await window.RunBusyAsync($"Conversion de {items.Count} fichier(s)…",
            async (p, ct) => result = await MediaItems.ConvertAsync(items, outputDir, format.Extension(),
                (input, output, progress, token) => AudioConverter.ConvertAsync(input, output, format, options, progress, token),
                p, window.SetStatus, Files.ScrollIntoView, ct, trimStart, trimEnd),
            $"Sons convertis dans {outputDir}", cancellable: true);
        if (!ok || result is null) return;
        var count = result.Written.Count;
        if (result.Failures.Count > 0)
        {
            window.SetStatus($"{count} fichier(s) converti(s), {result.Failures.Count} en échec.");
            var details = string.Join("\n\n", result.Failures.Take(5).Select(f => f.Message));
            if (result.Failures.Count > 5) details += $"\n\n… et {result.Failures.Count - 5} autre(s).";
            Dialog.Show(owner, $"{result.Failures.Count} fichier(s) n'ont pas pu être convertis :\n\n{details}", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            if (count == 0) return;
        }
        window.OfferToOpen(outputDir, $"{count} fichier(s) converti(s) dans {outputDir}.");
    }
}
