using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AllInOne.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

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
    private Bitmap? _thumbnail;

    public FileItem(string path) => Path = path;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    public Bitmap? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
}

/// <summary>Fenêtres de choix de fichiers et de dossiers (natives sous Windows, macOS et Linux).</summary>
public static class Dialogs
{
    public static readonly FilePickerFileType Images = Type("Images", ImageFormats.InputExtensions);
    public static readonly FilePickerFileType Pdf = Type("Documents PDF", new[] { ".pdf" });
    public static readonly FilePickerFileType PdfOrImages = Type("PDF et images", ImageFormats.InputExtensions.Prepend(".pdf"));
    public static readonly FilePickerFileType All = new("Tous les fichiers") { Patterns = new[] { "*" } };

    /// <summary>Type de fichier à partir d'extensions ; majuscules comprises (les filtres sont sensibles à la casse sous Linux).</summary>
    public static FilePickerFileType Type(string name, IEnumerable<string> extensions)
    {
        var list = extensions.ToList();
        return new FilePickerFileType(name)
        {
            Patterns = list.Select(e => "*" + e).Concat(list.Select(e => "*" + e.ToUpperInvariant())).Distinct().ToList(),
        };
    }

    private static IStorageProvider Storage(Visual owner) =>
        (TopLevel.GetTopLevel(owner) ?? throw new InvalidOperationException("Fenêtre introuvable.")).StorageProvider;

    public static async Task<string[]> OpenFiles(Visual owner, string title, bool multiple = true, params FilePickerFileType[] types)
    {
        var files = await Storage(owner).OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = types.Append(All).ToList(),
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public static async Task<string?> SavePdf(Visual owner, string suggestedName)
    {
        var file = await Storage(owner).SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer le PDF",
            SuggestedFileName = suggestedName,
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { Pdf },
            ShowOverwritePrompt = true,
        });
        var path = file?.TryGetLocalPath();
        if (path is not null && !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";
        return path;
    }

    public static async Task<string?> PickFolder(Visual owner, string title, string? initial = null)
    {
        var storage = Storage(owner);
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            start = await storage.TryGetFolderFromPathAsync(initial);
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Chemins locaux des fichiers glissés depuis l'Explorateur, le Finder ou le gestionnaire de fichiers.</summary>
    public static string[] DroppedFiles(DragEventArgs e) =>
#pragma warning disable CS0618 // API de glisser-déposer encore prise en charge dans Avalonia 11
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? Array.Empty<string>();
#pragma warning restore CS0618

    public static bool HasFiles(DragEventArgs e) =>
#pragma warning disable CS0618
        e.Data.Contains(DataFormats.Files);
#pragma warning restore CS0618

    /// <summary>Accepte le dépôt de fichiers n'importe où sur <paramref name="target"/>.</summary>
    public static void AcceptFileDrop(Control target, Func<string[], Task> onDrop)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = DroppedFiles(e);
            e.Handled = true;
            if (files.Length > 0) await onDrop(files);
        });
    }
}

public static class Thumbnails
{
    public static Bitmap FromPng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    /// <summary>Charge les miniatures d'images une par une, sans bloquer l'interface. Les erreurs sont ignorées.</summary>
    public static async Task LoadImageThumbnailsAsync(IEnumerable<FileItem> items, int width = 96)
    {
        foreach (var item in items.ToList())
        {
            try
            {
                var bitmap = await Task.Run(() => FromPng(Core.Pdf.PdfRenderer.RenderImageThumbnailPng(item.Path, width)));
                item.Thumbnail = bitmap;
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
    private const string DragFormat = "application/x-allinone-items";

    private static List<T> Selected<T>(ListBox list) => (list.SelectedItems ?? new ArrayList()).Cast<T>().ToList();

    /// <summary>Déplace les éléments sélectionnés d'un cran vers le haut (-1) ou le bas (+1).</summary>
    public static void MoveSelected<T>(ListBox list, ObservableCollection<T> items, int direction)
    {
        var selected = Selected<T>(list);
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
        var selected = Selected<T>(list).OrderBy(items.IndexOf).ToList();
        if (selected.Count == 0) return;
        foreach (var s in selected) items.Remove(s);
        if (toStart) for (var i = 0; i < selected.Count; i++) items.Insert(i, selected[i]);
        else foreach (var s in selected) items.Add(s);
        Reselect(list, selected);
    }

    public static void RemoveSelected<T>(ListBox list, ObservableCollection<T> items)
    {
        var first = list.SelectedIndex;
        foreach (var s in Selected<T>(list)) items.Remove(s);
        if (items.Count > 0) list.SelectedIndex = Math.Clamp(first, 0, items.Count - 1);
    }

    private static void Reselect<T>(ListBox list, IReadOnlyList<T> selected)
    {
        list.SelectedItems?.Clear();
        foreach (var s in selected) list.SelectedItems?.Add(s);
        if (selected.Count > 0) list.ScrollIntoView(selected[0]!);
    }

    /// <summary>
    /// Active le glisser-déposer : réorganisation des éléments à la souris, et dépôt de fichiers depuis le gestionnaire
    /// de fichiers (<paramref name="onFilesDropped"/> reçoit les fichiers et l'index d'insertion).
    /// </summary>
    public static void EnableDragDrop<T>(ListBox list, ObservableCollection<T> items, Action<string[], int> onFilesDropped)
        where T : class
    {
        Point? start = null;
        PointerPressedEventArgs? pressEvent = null;
        DragDrop.SetAllowDrop(list, true);

        list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            var onItem = ContainerAt(e.Source) is not null && e.GetCurrentPoint(list).Properties.IsLeftButtonPressed;
            start = onItem ? e.GetPosition(list) : null;
            pressEvent = onItem ? e : null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        list.AddHandler(InputElement.PointerReleasedEvent, (_, _) => start = null, RoutingStrategies.Tunnel, handledEventsToo: true);

        list.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (start is null || pressEvent is null || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(list) - start.Value;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
            start = null;
            var dragged = Selected<T>(list).OrderBy(items.IndexOf).ToList();
            if (dragged.Count == 0) return;
#pragma warning disable CS0618
            var data = new DataObject();
            data.Set(DragFormat, dragged);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        list.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
#pragma warning disable CS0618
            e.DragEffects = Dialogs.HasFiles(e) ? DragDropEffects.Copy
                : e.Data.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
#pragma warning restore CS0618
            e.Handled = true;
        });

        list.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var target = ContainerAt(e.Source);
            var index = target is null ? items.Count : list.IndexFromContainer(target);
            if (index < 0) index = items.Count;
            if (target is not null && IsAfterMiddle(target, e)) index++;

            var files = Dialogs.DroppedFiles(e);
#pragma warning disable CS0618
            if (files.Length > 0)
            {
                onFilesDropped(files, index);
            }
            else if (e.Data.Get(DragFormat) is List<T> dragged)
            {
                var insertAt = index - dragged.Count(d => items.IndexOf(d) < index);
                foreach (var d in dragged) items.Remove(d);
                insertAt = Math.Clamp(insertAt, 0, items.Count);
                for (var i = 0; i < dragged.Count; i++) items.Insert(insertAt + i, dragged[i]);
                Reselect(list, dragged);
            }
#pragma warning restore CS0618
            e.Handled = true;
        });
    }

    private static bool IsAfterMiddle(ListBoxItem item, DragEventArgs e)
    {
        var p = e.GetPosition(item);
        var size = item.Bounds.Size;
        // Les vignettes de pages sont disposées en grille (WrapPanel), les fichiers en colonne.
        return size.Width > size.Height * 1.5 ? p.Y > size.Height / 2 : p.X > size.Width / 2;
    }

    private static ListBoxItem? ContainerAt(object? source) =>
        source as ListBoxItem ?? (source as Visual)?.FindAncestorOfType<ListBoxItem>();
}
