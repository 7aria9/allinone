using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

public partial class ConvertAudioView : UserControl
{
    private static readonly string AudioFilter =
        "Sons et vidéos|" + string.Join(";", AudioFormats.InputExtensions.Concat(VideoFormats.InputExtensions).Select(e => "*" + e)) +
        "|Sons|" + string.Join(";", AudioFormats.InputExtensions.Select(e => "*" + e)) + "|Tous les fichiers|*.*";

    private readonly ObservableCollection<FileItem> _items = new();

    public ConvertAudioView()
    {
        InitializeComponent();
        Files.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListTools.EnableDragDrop(Files, _items, AddFiles);
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshFfmpegStatus(); };
    }

    private void RefreshFfmpegStatus() => FfmpegSetup.RefreshStatus(FfmpegStatus, DownloadButton);

    private void AddFiles(string[] paths, int index)
    {
        var added = paths.Where(AudioFormats.IsSupportedInput).Select(p => new FileItem(p)).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
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

        var ready = await FfmpegSetup.EnsureAsync(this);
        RefreshFfmpegStatus();
        if (!ready) return;

        var format = SelectedFormat;
        var options = new AudioConversionOptions
        {
            Quality = Enum.Parse<VideoQuality>((string)((ComboBoxItem)Quality.SelectedItem).Tag),
            SampleRate = TagNumber(SampleRate),
            Channels = TagNumber(Channels),
        };
        var paths = _items.Select(i => i.Path).ToList();
        var window = MainWindow.Of(this);
        var status = new Progress<string>(window.SetStatus);
        var count = 0;
        var ok = await window.RunBusyAsync($"Conversion de {paths.Count} fichier(s)…",
            async (p, ct) => count = (await Task.Run(() => AudioConverter.ConvertManyAsync(paths, outputDir, format, options, p, ct, status), ct)).Count,
            $"Sons convertis dans {outputDir}", cancellable: true);
        if (ok) window.OfferToOpen(outputDir, $"{count} fichier(s) converti(s) dans {outputDir}.");
    }
}
