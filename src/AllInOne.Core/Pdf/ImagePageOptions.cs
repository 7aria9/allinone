namespace AllInOne.Core.Pdf;

public enum PageSizeMode
{
    /// <summary>La page prend exactement la taille de l'image.</summary>
    FitImage,
    A4,
    A3,
    A5,
    Letter,
    Legal,
}

public enum PageOrientation
{
    /// <summary>Paysage si l'image est plus large que haute, portrait sinon.</summary>
    Auto,
    Portrait,
    Landscape,
}

/// <summary>Mise en page des images converties en pages PDF.</summary>
public sealed record ImagePageOptions
{
    public PageSizeMode PageSize { get; init; } = PageSizeMode.A4;
    public PageOrientation Orientation { get; init; } = PageOrientation.Auto;

    /// <summary>Marge autour de l'image, en millimètres.</summary>
    public double MarginMm { get; init; } = 10;

    /// <summary>Agrandir les petites images pour remplir la page (sinon elles gardent leur taille réelle).</summary>
    public bool EnlargeSmallImages { get; init; } = true;

    /// <summary>Qualité JPEG utilisée quand une image doit être réencodée (1-100).</summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>Résolution supposée quand l'image n'en indique pas (points par pouce).</summary>
    public double DefaultDpi { get; init; } = 96;

    /// <summary>Insérer toutes les pages d'un TIFF multipage (sinon seulement la première).</summary>
    public bool AllTiffFrames { get; init; } = true;

    internal static (double Width, double Height) SizeInPoints(PageSizeMode mode) => mode switch
    {
        PageSizeMode.A4 => (595.28, 841.89),
        PageSizeMode.A3 => (841.89, 1190.55),
        PageSizeMode.A5 => (419.53, 595.28),
        PageSizeMode.Letter => (612, 792),
        PageSizeMode.Legal => (612, 1008),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
