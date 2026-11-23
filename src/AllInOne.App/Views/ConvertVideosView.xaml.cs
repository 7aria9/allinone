using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

public partial class ConvertVideosView : UserControl
{
    private static readonly string VideoFilter =
        "Vidéos|" + string.Join(";", VideoFormats.InputExtensions.Select(e => "*" + e)) + "|Tous les fichiers|*.*";

    private readonly ObservableCollection<FileItem> _items = new();

    public ConvertVideosView()
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
        var added = paths.Where(VideoFormats.IsSupportedVideo).Select(p => new FileItem(p)).ToList();
        var skipped = paths.Length - added.Count;
        index = Math.Clamp(index, 0, _items.Count);
        for (var i = 0; i < added.Count; i++) _items.Insert(index + i, added[i]);
        if (string.IsNullOrWhiteSpace(OutputDir.Text) && added.Count > 0)
            OutputDir.Text = Path.Combine(added[0].Folder, "converties");
        if (skipped > 0) MainWindow.Of(this).SetStatus($"{skipped} fichier(s) ignoré(s) : format vidéo non reconnu.");
    }

    private void Add_Click(object sender, RoutedEventArgs e) =>
        AddFiles(Dialogs.OpenFiles(VideoFilter, "Choisir des vidéos"), _items.Count);

    private void Remove_Click(object sender, RoutedEventArgs e) => ListTools.RemoveSelected(Files, _items);
    private void Clear_Click(object sender, RoutedEventArgs e) => _items.Clear();

    private void Copy_Changed(object sender, RoutedEventArgs e)
    {
        var reencode = CopyStreams.IsChecked != true;
        Quality.IsEnabled = Resolution.IsEnabled = reencode;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Dossier où enregistrer les vidéos converties", OutputDir.Text);
        if (folder is not null) OutputDir.Text = folder;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        await FfmpegSetup.DownloadAsync(this);
        RefreshFfmpegStatus();
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (_items.Count == 0) { Dialog.Show(owner, "Ajoutez au moins une vidéo.", "AllInOne"); return; }
        var outputDir = OutputDir.Text.Trim();
        if (outputDir.Length == 0) { Dialog.Show(owner, "Choisissez un dossier de sortie.", "AllInOne"); return; }

        var ready = await FfmpegSetup.EnsureAsync(this);
        RefreshFfmpegStatus();
        if (!ready) return;

        var format = Enum.Parse<VideoFormat>((string)((ComboBoxItem)Format.SelectedItem).Tag);
        var height = int.Parse((string)((ComboBoxItem)Resolution.SelectedItem).Tag);
        var options = new VideoConversionOptions
        {
            Quality = Enum.Parse<VideoQuality>((string)((ComboBoxItem)Quality.SelectedItem).Tag),
            MaxHeight = height > 0 ? height : null,
            RemoveAudio = RemoveAudio.IsChecked == true,
            CopyStreams = CopyStreams.IsChecked == true,
        };
        var paths = _items.Select(i => i.Path).ToList();
        var window = MainWindow.Of(this);
        var status = new Progress<string>(window.SetStatus);
        var count = 0;
        var ok = await window.RunBusyAsync($"Conversion de {paths.Count} vidéo(s)…",
            async (p, ct) => count = (await Task.Run(() => VideoConverter.ConvertManyAsync(paths, outputDir, format, options, p, ct, status), ct)).Count,
            $"Vidéos converties dans {outputDir}", cancellable: true);
        if (ok) window.OfferToOpen(outputDir, $"{count} vidéo(s) convertie(s) dans {outputDir}.");
    }
}
