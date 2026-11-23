using AllInOne.Core.Images;
using ImageMagick;
using PDFtoImage;
using SkiaSharp;

namespace AllInOne.Core.Pdf;

/// <summary>
/// Conversion de pages PDF en images. Utilise PDFtoImage (MIT), basé sur PDFium (BSD/Apache 2.0) et SkiaSharp (MIT).
/// </summary>
public static class PdfRenderer
{
    /// <summary>
    /// Convertit les pages d'un PDF en images dans <paramref name="outputDirectory"/>.
    /// Les fichiers sont nommés « document_page01.png », etc. Renvoie la liste des fichiers créés.
    /// </summary>
    public static IReadOnlyList<string> ToImages(
        string pdfPath,
        string outputDirectory,
        ImageFormatKind format,
        int dpi = 150,
        IEnumerable<int>? pageIndexes = null,
        ImageConversionOptions? options = null,
        string? password = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ImageConversionOptions();
        var bytes = File.ReadAllBytes(pdfPath);
        var count = Conversion.GetPageCount(bytes, password);
        var pages = (pageIndexes ?? Enumerable.Range(0, count)).ToList();
        Directory.CreateDirectory(outputDirectory);

        var name = Path.GetFileNameWithoutExtension(pdfPath);
        var digits = Math.Max(2, count.ToString().Length);
        var renderOptions = new RenderOptions { Dpi = Math.Clamp(dpi, 10, 1200), WithAnnotations = true, WithFormFill = true };
        var files = new List<string>();

        for (var i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = pages[i];
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(null, $"La page {index + 1} n'existe pas (le document a {count} page(s)).");

            var target = FileNames.Unique(Path.Combine(outputDirectory,
                $"{name}_page{(index + 1).ToString().PadLeft(digits, '0')}{format.Extension()}"));

            using (var bitmap = Conversion.ToImage(bytes, index, password, renderOptions))
            using (var png = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            {
                using var image = new MagickImage(png.ToArray());
                image.Density = new Density(renderOptions.Dpi, renderOptions.Dpi, DensityUnit.PixelsPerInch);
                ImageConverter.Save(image, target, format, options);
            }

            files.Add(target);
            progress?.Report((i + 1) / (double)pages.Count);
        }
        return files;
    }

    /// <summary>Rend une miniature PNG d'une page (largeur en pixels), pour l'aperçu dans l'interface.</summary>
    public static byte[] RenderThumbnailPng(string pdfPath, int pageIndex, int width = 200, string? password = null)
    {
        var bytes = File.ReadAllBytes(pdfPath);
        return RenderThumbnailPng(bytes, pageIndex, width, password);
    }

    public static byte[] RenderThumbnailPng(byte[] pdfBytes, int pageIndex, int width = 200, string? password = null)
    {
        var options = new RenderOptions { Width = width, WithAspectRatio = true, WithAnnotations = true };
        using var bitmap = Conversion.ToImage(pdfBytes, pageIndex, password, options);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Rend une miniature PNG d'une image (largeur maximale en pixels).</summary>
    public static byte[] RenderImageThumbnailPng(string imagePath, int width = 200, int frameIndex = 0)
    {
        using var collection = new MagickImageCollection();
        collection.Read(imagePath);
        using var image = new MagickImage(collection[Math.Min(frameIndex, collection.Count - 1)]);
        image.AutoOrient();
        image.Thumbnail(new MagickGeometry((uint)width, (uint)width));
        return image.ToByteArray(MagickFormat.Png);
    }
}
