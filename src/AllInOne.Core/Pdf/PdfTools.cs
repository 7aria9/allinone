namespace AllInOne.Core.Pdf;

/// <summary>Opérations courantes sur les PDF. Les index de pages commencent à 0.</summary>
public static class PdfTools
{
    /// <summary>Crée un PDF à partir d'une ou plusieurs images (une page par image).</summary>
    public static void ImagesToPdf(IEnumerable<string> imagePaths, string outputPath, ImagePageOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new ImagePageOptions();
        var pages = PdfComposer.ExpandFiles(imagePaths, options);
        PdfComposer.Build(pages, outputPath, options, progress, cancellationToken);
    }

    /// <summary>Fusionne des PDF (et éventuellement des images) dans l'ordre donné.</summary>
    public static void Merge(IEnumerable<string> inputPaths, string outputPath, ImagePageOptions? imageOptions = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var pages = PdfComposer.ExpandFiles(inputPaths, imageOptions);
        PdfComposer.Build(pages, outputPath, imageOptions, progress, cancellationToken);
    }

    /// <summary>Supprime les pages indiquées.</summary>
    public static void DeletePages(string inputPath, string outputPath, IEnumerable<int> pagesToDelete)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        var remove = pagesToDelete.ToHashSet();
        foreach (var p in remove) CheckIndex(p, count);
        var keep = Enumerable.Range(0, count).Where(i => !remove.Contains(i)).ToList();
        if (keep.Count == 0)
            throw new InvalidOperationException("Impossible de supprimer toutes les pages du document.");
        PdfComposer.Build(keep.Select(i => PageSource.FromPdf(inputPath, i)), outputPath);
    }

    /// <summary>Ne garde que les pages indiquées, dans l'ordre indiqué (extraction ou réorganisation).</summary>
    public static void ExtractPages(string inputPath, string outputPath, IEnumerable<int> pages)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        var list = pages.ToList();
        foreach (var p in list) CheckIndex(p, count);
        PdfComposer.Build(list.Select(i => PageSource.FromPdf(inputPath, i)), outputPath);
    }

    /// <summary>
    /// Réorganise toutes les pages. <paramref name="newOrder"/> doit contenir chaque index de page une seule fois,
    /// par exemple [2, 0, 1] place la 3e page en premier.
    /// </summary>
    public static void ReorderPages(string inputPath, string outputPath, IReadOnlyList<int> newOrder)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        if (newOrder.Count != count || newOrder.Distinct().Count() != count || newOrder.Any(i => i < 0 || i >= count))
            throw new ArgumentException($"Le nouvel ordre doit contenir chaque page de 1 à {count} exactement une fois.");
        ExtractPages(inputPath, outputPath, newOrder);
    }

    /// <summary>Déplace une page vers une nouvelle position.</summary>
    public static void MovePage(string inputPath, string outputPath, int fromIndex, int toIndex)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        CheckIndex(fromIndex, count);
        CheckIndex(toIndex, count);
        var order = Enumerable.Range(0, count).ToList();
        order.RemoveAt(fromIndex);
        order.Insert(toIndex, fromIndex);
        ExtractPages(inputPath, outputPath, order);
    }

    /// <summary>
    /// Insère des pages (toutes les pages des PDF et/ou des images de <paramref name="filesToInsert"/>)
    /// avant la page <paramref name="position"/>. Une position égale au nombre de pages ajoute à la fin.
    /// </summary>
    public static void InsertPages(string inputPath, string outputPath, int position, IEnumerable<string> filesToInsert,
        ImagePageOptions? imageOptions = null)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        if (position < 0 || position > count)
            throw new ArgumentOutOfRangeException(null, $"La position doit être comprise entre 1 et {count + 1}.");
        var pages = Enumerable.Range(0, count).Select(i => PageSource.FromPdf(inputPath, i)).ToList();
        pages.InsertRange(position, PdfComposer.ExpandFiles(filesToInsert, imageOptions));
        PdfComposer.Build(pages, outputPath, imageOptions);
    }

    /// <summary>Fait pivoter les pages indiquées (multiple de 90°, sens horaire).</summary>
    public static void RotatePages(string inputPath, string outputPath, IEnumerable<int> pages, int degrees)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        var set = pages.ToHashSet();
        foreach (var p in set) CheckIndex(p, count);
        PdfComposer.Build(
            Enumerable.Range(0, count).Select(i => PageSource.FromPdf(inputPath, i, set.Contains(i) ? degrees : 0)),
            outputPath);
    }

    /// <summary>Découpe un PDF en un fichier par page. Renvoie les fichiers créés.</summary>
    public static IReadOnlyList<string> SplitPages(string inputPath, string outputDirectory)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        Directory.CreateDirectory(outputDirectory);
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var digits = Math.Max(2, count.ToString().Length);
        var files = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var target = FileNames.Unique(Path.Combine(outputDirectory, $"{name}_page{(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
            PdfComposer.Build(new[] { PageSource.FromPdf(inputPath, i) }, target);
            files.Add(target);
        }
        return files;
    }

    private static void CheckIndex(int index, int count)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(null, $"La page {index + 1} n'existe pas (le document a {count} page(s)).");
    }
}
