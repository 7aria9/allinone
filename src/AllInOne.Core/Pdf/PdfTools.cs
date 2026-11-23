using AllInOne.Core.Localization;
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
            throw new InvalidOperationException(L.T("Impossible de supprimer toutes les pages du document."));
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
            throw new ArgumentException(L.T("Le nouvel ordre doit contenir chaque page de 1 à {0} exactement une fois.", count));
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
            throw new ArgumentOutOfRangeException(null, L.T("La position doit être comprise entre 1 et {0}.", count + 1));
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

    /// <summary>Découpe un PDF en fichiers de <paramref name="pagesPerFile"/> pages (le dernier peut en avoir moins).</summary>
    public static IReadOnlyList<string> SplitEvery(string inputPath, string outputDirectory, int pagesPerFile,
        IProgress<double>? progress = null)
    {
        if (pagesPerFile < 1) throw new ArgumentOutOfRangeException(nameof(pagesPerFile), L.T("Il faut au moins une page par fichier."));
        var count = PdfInfo.GetPageCount(inputPath);
        var groups = new List<IReadOnlyList<int>>();
        for (var start = 0; start < count; start += pagesPerFile)
            groups.Add(Enumerable.Range(start, Math.Min(pagesPerFile, count - start)).ToList());
        return WriteGroups(inputPath, outputDirectory, groups, count, progress);
    }

    /// <summary>
    /// Découpe un PDF selon des plages saisies : chaque partie séparée par une virgule ou un point-virgule
    /// donne un fichier (« 1-3, 4-7, 8- » → trois fichiers).
    /// </summary>
    public static IReadOnlyList<string> SplitByRanges(string inputPath, string outputDirectory, string ranges,
        IProgress<double>? progress = null)
    {
        var count = PdfInfo.GetPageCount(inputPath);
        var groups = ranges.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => PageRange.Parse(part, count))
            .Where(g => g.Count > 0)
            .ToList();
        if (groups.Count == 0) throw new ArgumentException(L.T("Indiquez au moins une plage de pages, par exemple « 1-3, 4-6 »."));
        return WriteGroups(inputPath, outputDirectory, groups, count, progress);
    }

    private static IReadOnlyList<string> WriteGroups(string inputPath, string outputDirectory,
        IReadOnlyList<IReadOnlyList<int>> groups, int pageCount, IProgress<double>? progress)
    {
        Directory.CreateDirectory(outputDirectory);
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var digits = Math.Max(2, pageCount.ToString().Length);
        string N(int index) => (index + 1).ToString().PadLeft(digits, '0');
        var files = new List<string>();
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var first = g[0];
            var last = g[^1];
            var contiguous = g.Count == Math.Abs(last - first) + 1;
            var suffix = g.Count == 1 ? $"_page{N(first)}" : contiguous ? $"_pages{N(first)}-{N(last)}" : $"_partie{i + 1}";
            var target = FileNames.Unique(Path.Combine(outputDirectory, name + suffix + ".pdf"));
            PdfComposer.Build(g.Select(p => PageSource.FromPdf(inputPath, p)).ToList(), target);
            files.Add(target);
            progress?.Report((i + 1) / (double)groups.Count);
        }
        return files;
    }

    private static void CheckIndex(int index, int count)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(null, L.T("La page {0} n'existe pas (le document a {1} page(s)).", index + 1, count));
    }
}
