using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AllInOne.Core.Pdf;

/// <summary>
/// Construit un PDF à partir d'une liste ordonnée de pages (pages de PDF existants et/ou images).
/// C'est la brique commune à la fusion, la suppression, l'insertion et la réorganisation de pages.
/// Utilise PDFsharp (licence MIT).
/// </summary>
public static class PdfComposer
{
    private const double PointsPerMm = 72.0 / 25.4;

    public static void Build(
        IEnumerable<PageSource> pages,
        string outputPath,
        ImagePageOptions? imageOptions = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        imageOptions ??= new ImagePageOptions();
        var list = pages.ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("Le document doit contenir au moins une page.");

        // Les PDF sources sont lus en mémoire : on peut ainsi écraser l'un d'eux avec le résultat.
        var opened = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
        using var output = new PdfDocument();
        output.Info.Creator = "AllInOne";

        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (list[i])
                {
                    case PdfPageSource p:
                        AddPdfPage(output, OpenCached(opened, p.Path), p);
                        break;
                    case ImagePageSource img:
                        AddImagePage(output, img, imageOptions);
                        break;
                    default:
                        throw new NotSupportedException(list[i].GetType().Name);
                }
                progress?.Report((i + 1) / (double)list.Count * 0.95);
            }

            SaveAtomically(output, outputPath);
            progress?.Report(1);
        }
        finally
        {
            foreach (var doc in opened.Values) doc.Dispose();
        }
    }

    /// <summary>Développe une liste de fichiers (PDF ou images) en pages, dans l'ordre.</summary>
    public static IReadOnlyList<PageSource> ExpandFiles(IEnumerable<string> files, ImagePageOptions? imageOptions = null)
    {
        imageOptions ??= new ImagePageOptions();
        var result = new List<PageSource>();
        foreach (var file in files)
        {
            if (IsPdf(file))
            {
                var count = PdfInfo.GetPageCount(file);
                for (var i = 0; i < count; i++) result.Add(PageSource.FromPdf(file, i));
            }
            else
            {
                var frames = imageOptions.AllTiffFrames ? CountFrames(file) : 1;
                for (var i = 0; i < frames; i++) result.Add(PageSource.FromImage(file, i));
            }
        }
        return result;
    }

    public static bool IsPdf(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    internal static int CountFrames(string imagePath)
    {
        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is not (".tif" or ".tiff")) return 1;
        using var collection = new MagickImageCollection();
        collection.Ping(imagePath);
        return Math.Max(1, collection.Count);
    }

    internal static PdfDocument OpenForImport(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
    }

    private static PdfDocument OpenCached(Dictionary<string, PdfDocument> cache, string path)
    {
        var key = Path.GetFullPath(path);
        if (!cache.TryGetValue(key, out var doc))
        {
            doc = OpenForImport(path);
            cache[key] = doc;
        }
        return doc;
    }

    private static void AddPdfPage(PdfDocument output, PdfDocument source, PdfPageSource p)
    {
        if (p.PageIndex < 0 || p.PageIndex >= source.PageCount)
            throw new ArgumentOutOfRangeException(null, $"La page {p.PageIndex + 1} n'existe pas dans {Path.GetFileName(p.Path)}.");
        var page = output.AddPage(source.Pages[p.PageIndex]);
        ApplyRotation(page, p.Rotation);
    }

    private static void ApplyRotation(PdfPage page, int extraRotation)
    {
        if (extraRotation % 90 != 0)
            throw new ArgumentException("La rotation doit être un multiple de 90°.");
        if (extraRotation == 0) return;
        page.Rotate = Normalize(page.Rotate + extraRotation);
    }

    internal static int Normalize(int degrees) => ((degrees % 360) + 360) % 360;

    private static void AddImagePage(PdfDocument output, ImagePageSource source, ImagePageOptions options)
    {
        using var image = LoadFrame(source.Path, source.FrameIndex);
        image.AutoOrient();

        var dpiX = image.Density.X > 1 ? image.Density.X : options.DefaultDpi;
        var dpiY = image.Density.Y > 1 ? image.Density.Y : options.DefaultDpi;
        if (image.Density.Units == DensityUnit.PixelsPerCentimeter) { dpiX *= 2.54; dpiY *= 2.54; }
        var naturalW = image.Width * 72.0 / dpiX;
        var naturalH = image.Height * 72.0 / dpiY;

        using var encoded = EncodeForPdf(image, options.JpegQuality);
        using var xImage = XImage.FromStream(encoded);

        var margin = Math.Max(0, options.MarginMm) * PointsPerMm;
        double pageW, pageH;
        if (options.PageSize == PageSizeMode.FitImage)
        {
            pageW = naturalW + 2 * margin;
            pageH = naturalH + 2 * margin;
        }
        else
        {
            (pageW, pageH) = ImagePageOptions.SizeInPoints(options.PageSize);
            var landscape = options.Orientation switch
            {
                PageOrientation.Landscape => true,
                PageOrientation.Portrait => false,
                _ => naturalW > naturalH,
            };
            if (landscape) (pageW, pageH) = (pageH, pageW);
        }

        var page = output.AddPage();
        page.Width = XUnit.FromPoint(pageW);
        page.Height = XUnit.FromPoint(pageH);

        var boxW = Math.Max(1, pageW - 2 * margin);
        var boxH = Math.Max(1, pageH - 2 * margin);
        var scale = Math.Min(boxW / naturalW, boxH / naturalH);
        if (!options.EnlargeSmallImages) scale = Math.Min(scale, 1);
        var drawW = naturalW * scale;
        var drawH = naturalH * scale;
        var x = (pageW - drawW) / 2;
        var y = (pageH - drawH) / 2;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawImage(xImage, x, y, drawW, drawH);
        }

        ApplyRotation(page, source.Rotation);
    }

    private static MagickImage LoadFrame(string path, int frameIndex)
    {
        if (frameIndex == 0) return new MagickImage(path);
        using var collection = new MagickImageCollection(path);
        if (frameIndex >= collection.Count)
            throw new ArgumentOutOfRangeException(null, $"L'image {Path.GetFileName(path)} n'a que {collection.Count} page(s).");
        return new MagickImage(collection[frameIndex]);
    }

    /// <summary>Photos opaques en JPEG (fichiers compacts), le reste en PNG sans perte.</summary>
    private static MemoryStream EncodeForPdf(MagickImage image, int jpegQuality)
    {
        var ms = new MemoryStream();
        // Les photos déjà compressées avec perte (JPEG, HEIC d'iPhone, AVIF) restent en JPEG : un PNG serait bien plus lourd.
        var keepJpeg = image.Format is MagickFormat.Jpeg or MagickFormat.Jpg or MagickFormat.Heic or MagickFormat.Heif or MagickFormat.Avif;
        image.Strip();
        if (keepJpeg && !image.HasAlpha)
        {
            image.ColorSpace = ColorSpace.sRGB;
            image.Quality = (uint)Math.Clamp(jpegQuality, 1, 100);
            image.Write(ms, MagickFormat.Jpeg);
        }
        else
        {
            image.Write(ms, MagickFormat.Png);
        }
        ms.Position = 0;
        return ms;
    }

    private static void SaveAtomically(PdfDocument document, string outputPath)
    {
        var full = Path.GetFullPath(outputPath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            document.Save(temp);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
