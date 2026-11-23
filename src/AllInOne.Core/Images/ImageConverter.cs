using ImageMagick;

namespace AllInOne.Core.Images;

/// <summary>Options de conversion d'image.</summary>
public sealed record ImageConversionOptions
{
    /// <summary>Qualité 1-100 pour JPEG et WebP.</summary>
    public int Quality { get; init; } = 90;

    /// <summary>Largeur maximale en pixels (null = taille d'origine). Les proportions sont conservées.</summary>
    public int? MaxWidth { get; init; }

    /// <summary>Hauteur maximale en pixels (null = taille d'origine). Les proportions sont conservées.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Couleur de fond appliquée quand le format cible ne gère pas la transparence (JPEG, BMP).</summary>
    public string BackgroundColor { get; init; } = "#FFFFFF";
}

/// <summary>Conversion d'images d'un format à un autre (Magick.NET, licence Apache 2.0).</summary>
public static class ImageConverter
{
    /// <summary>Convertit une image et renvoie le chemin du fichier écrit.</summary>
    public static string Convert(string inputPath, string outputPath, ImageFormatKind format, ImageConversionOptions? options = null)
    {
        options ??= new ImageConversionOptions();
        using var image = new MagickImage(inputPath);
        Save(image, outputPath, format, options);
        return outputPath;
    }

    /// <summary>
    /// Convertit plusieurs images dans un dossier de sortie. Le nom d'origine est conservé avec la nouvelle extension ;
    /// un suffixe est ajouté si un fichier du même nom existe déjà.
    /// </summary>
    public static IReadOnlyList<string> ConvertMany(
        IEnumerable<string> inputPaths,
        string outputDirectory,
        ImageFormatKind format,
        ImageConversionOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = inputPaths.ToList();
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(inputs[i]);
            var target = FileNames.Unique(Path.Combine(outputDirectory, name + format.Extension()));
            written.Add(Convert(inputs[i], target, format, options));
            progress?.Report((i + 1) / (double)inputs.Count);
        }
        return written;
    }

    internal static void Save(MagickImage image, string outputPath, ImageFormatKind format, ImageConversionOptions options)
    {
        image.AutoOrient();

        if (options.MaxWidth is not null || options.MaxHeight is not null)
        {
            var w = (uint)(options.MaxWidth ?? int.MaxValue);
            var h = (uint)(options.MaxHeight ?? int.MaxValue);
            if (image.Width > w || image.Height > h)
            {
                image.Resize(new MagickGeometry(Math.Min(w, image.Width), Math.Min(h, image.Height)) { IgnoreAspectRatio = false });
            }
        }

        if (format is ImageFormatKind.Jpeg or ImageFormatKind.Bmp && image.HasAlpha)
        {
            image.BackgroundColor = new MagickColor(options.BackgroundColor);
            image.Alpha(AlphaAction.Remove);
            image.Alpha(AlphaAction.Off);
        }

        if (format.SupportsQuality())
            image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);

        image.Format = ToMagick(format);
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        image.Write(outputPath);
    }

    internal static MagickFormat ToMagick(ImageFormatKind format) => format switch
    {
        ImageFormatKind.Png => MagickFormat.Png,
        ImageFormatKind.Jpeg => MagickFormat.Jpeg,
        ImageFormatKind.Bmp => MagickFormat.Bmp,
        ImageFormatKind.Gif => MagickFormat.Gif,
        ImageFormatKind.Tiff => MagickFormat.Tiff,
        ImageFormatKind.Webp => MagickFormat.WebP,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
