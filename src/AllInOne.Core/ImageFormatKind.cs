namespace AllInOne.Core;

/// <summary>Formats d'image pris en charge en sortie.</summary>
public enum ImageFormatKind
{
    Png,
    Jpeg,
    Bmp,
    Gif,
    Tiff,
    Webp,
}

public static class ImageFormats
{
    /// <summary>Extensions reconnues en entrée (lecture).</summary>
    public static readonly IReadOnlyList<string> InputExtensions = new[]
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico", ".heic", ".heif", ".avif",
    };

    public static bool IsSupportedImage(string path) =>
        InputExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string Extension(this ImageFormatKind format) => format switch
    {
        ImageFormatKind.Png => ".png",
        ImageFormatKind.Jpeg => ".jpg",
        ImageFormatKind.Bmp => ".bmp",
        ImageFormatKind.Gif => ".gif",
        ImageFormatKind.Tiff => ".tif",
        ImageFormatKind.Webp => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Indique si le format accepte un réglage de qualité (compression avec perte).</summary>
    public static bool SupportsQuality(this ImageFormatKind format) =>
        format is ImageFormatKind.Jpeg or ImageFormatKind.Webp;

    public static ImageFormatKind Parse(string value)
    {
        var v = value.Trim().TrimStart('.').ToLowerInvariant();
        return v switch
        {
            "png" => ImageFormatKind.Png,
            "jpg" or "jpeg" => ImageFormatKind.Jpeg,
            "bmp" => ImageFormatKind.Bmp,
            "gif" => ImageFormatKind.Gif,
            "tif" or "tiff" => ImageFormatKind.Tiff,
            "webp" => ImageFormatKind.Webp,
            _ => throw new ArgumentException($"Format d'image inconnu : {value}"),
        };
    }
}
