namespace AllInOne.Core.Pdf;

/// <summary>Une page du document final : page d'un PDF existant ou image à insérer.</summary>
public abstract record PageSource
{
    /// <summary>Rotation supplémentaire en degrés (multiple de 90, sens horaire).</summary>
    public int Rotation { get; init; }

    public abstract string FilePath { get; }

    public static PageSource FromPdf(string path, int pageIndex, int rotation = 0) =>
        new PdfPageSource(path, pageIndex) { Rotation = rotation };

    public static PageSource FromImage(string path, int frameIndex = 0, int rotation = 0) =>
        new ImagePageSource(path, frameIndex) { Rotation = rotation };
}

/// <summary>Page <see cref="PageIndex"/> (à partir de 0) du PDF <see cref="Path"/>.</summary>
public sealed record PdfPageSource(string Path, int PageIndex) : PageSource
{
    public override string FilePath => Path;
}

/// <summary>Image (ou image n° <see cref="FrameIndex"/> d'un TIFF multipage) placée sur sa propre page.</summary>
public sealed record ImagePageSource(string Path, int FrameIndex = 0) : PageSource
{
    public override string FilePath => Path;
}
