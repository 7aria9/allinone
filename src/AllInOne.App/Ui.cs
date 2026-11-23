using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllInOne.Core;
using Microsoft.Win32;

namespace AllInOne.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Un fichier dans une liste, avec sa miniature chargée en arrière-plan.</summary>
public sealed class FileItem : Observable
{
    private ImageSource? _thumbnail;

    public FileItem(string path) => Path = path;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
}

public static class Dialogs
{
    public static readonly string ImageFilter =
        "Images|" + string.Join(";", ImageFormats.InputExtensions.Select(e => "*" + e)) + "|Tous les fichiers|*.*";

    public const string PdfFilter = "Documents PDF|*.pdf";

    public static readonly string PdfOrImageFilter =
        "PDF et images|*.pdf;" + string.Join(";", ImageFormats.InputExtensions.Select(e => "*" + e)) +
        "|Documents PDF|*.pdf|" + ImageFilter;

    public static string[] OpenFiles(string filter, string title, bool multiple = true)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiple, Title = title };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public static string? SavePdf(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = PdfFilter,
            FileName = suggestedName,
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            Title = "Enregistrer le PDF",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public static string? PickFolder(string title, string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

public static class Thumbnails
{
    /// <summary>Crée une image WPF figée (utilisable depuis n'importe quel thread) à partir d'octets PNG.</summary>
    public static BitmapImage FromPng(byte[] png)
    {
        var bitmap = new BitmapImage();
        using var ms = new MemoryStream(png);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Charge les miniatures d'images une par une, sans bloquer l'interface. Les erreurs sont ignorées.</summary>
    public static async Task LoadImageThumbnailsAsync(IEnumerable<FileItem> items, int width = 96)
    {
        foreach (var item in items.ToList())
        {
            try
            {
                var png = await Task.Run(() => Core.Pdf.PdfRenderer.RenderImageThumbnailPng(item.Path, width));
                item.Thumbnail = FromPng(png);
            }
            catch
            {
                // Fichier illisible : pas de miniature.
            }
        }
    }
}

public static class ListTools
{
    /// <summary>Déplace les éléments sélectionnés d'un cran vers le haut (-1) ou le bas (+1).</summary>
    public static void MoveSelected<T>(ListBox list, ObservableCollection<T> items, int direction)
    {
        var selected = list.SelectedItems.Cast<T>().ToList();
        if (selected.Count == 0) return;
        var indexes = selected.Select(items.IndexOf).OrderBy(i => i).ToList();
        if (direction < 0)
        {
            if (indexes[0] == 0) return;
            foreach (var i in indexes) items.Move(i, i - 1);
        }
        else
        {
            if (indexes[^1] == items.Count - 1) return;
            foreach (var i in Enumerable.Reverse(indexes)) items.Move(i, i + 1);
        }
        Reselect(list, selected);
    }

    /// <summary>Déplace les éléments sélectionnés au début ou à la fin.</summary>
    public static void MoveSelectedToEdge<T>(ListBox list, ObservableCollection<T> items, bool toStart)
    {
        var selected = list.SelectedItems.Cast<T>().OrderBy(items.IndexOf).ToList();
        if (selected.Count == 0) return;
        foreach (var s in selected) items.Remove(s);
        if (toStart) for (var i = 0; i < selected.Count; i++) items.Insert(i, selected[i]);
        else foreach (var s in selected) items.Add(s);
        Reselect(list, selected);
    }

    public static void RemoveSelected<T>(ListBox list, ObservableCollection<T> items)
    {
        var first = list.SelectedIndex;
        foreach (var s in list.SelectedItems.Cast<T>().ToList()) items.Remove(s);
        if (items.Count > 0) list.SelectedIndex = Math.Clamp(first, 0, items.Count - 1);
    }

    private static void Reselect<T>(ListBox list, IEnumerable<T> selected)
    {
        list.SelectedItems.Clear();
        foreach (var s in selected) list.SelectedItems.Add(s);
        list.ScrollIntoView(selected.First());
    }

    /// <summary>
    /// Active le glisser-déposer : réorganisation des éléments à la souris, et dépôt de fichiers depuis l'Explorateur
    /// (<paramref name="onFilesDropped"/> reçoit les fichiers et l'index d'insertion).
    /// </summary>
    public static void EnableDragDrop<T>(ListBox list, ObservableCollection<T> items, Action<string[], int> onFilesDropped)
        where T : class
    {
        Point? start = null;
        list.AllowDrop = true;

        list.PreviewMouseLeftButtonDown += (_, e) =>
            start = ContainerAt(list, e.OriginalSource as DependencyObject) is null ? null : e.GetPosition(list);

        list.PreviewMouseMove += (_, e) =>
        {
            if (start is null || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(list) - start.Value;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            var dragged = list.SelectedItems.Cast<T>().OrderBy(items.IndexOf).ToList();
            if (dragged.Count == 0) return;
            DragDrop.DoDragDrop(list, new DataObject(typeof(List<T>), dragged), DragDropEffects.Move);
        };

        list.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy
                : e.Data.GetDataPresent(typeof(List<T>)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };

        list.Drop += (_, e) =>
        {
            var target = ContainerAt(list, e.OriginalSource as DependencyObject);
            var index = target is null ? items.Count : list.ItemContainerGenerator.IndexFromContainer(target);
            if (target is not null && IsAfterMiddle(target, e)) index++;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                onFilesDropped(files, index);
            }
            else if (e.Data.GetData(typeof(List<T>)) is List<T> dragged)
            {
                var insertAt = index - dragged.Count(d => items.IndexOf(d) < index);
                foreach (var d in dragged) items.Remove(d);
                insertAt = Math.Clamp(insertAt, 0, items.Count);
                for (var i = 0; i < dragged.Count; i++) items.Insert(insertAt + i, dragged[i]);
                Reselect(list, dragged);
            }
            e.Handled = true;
        };
    }

    private static bool IsAfterMiddle(ListBoxItem item, DragEventArgs e)
    {
        var p = e.GetPosition(item);
        // Les vignettes de pages sont disposées horizontalement (WrapPanel), les fichiers verticalement.
        return item.ActualWidth > item.ActualHeight * 1.5 ? p.Y > item.ActualHeight / 2 : p.X > item.ActualWidth / 2;
    }

    private static ListBoxItem? ContainerAt(ListBox list, DependencyObject? source)
    {
        while (source is not null and not ListBox)
        {
            if (source is ListBoxItem item) return item;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
}
