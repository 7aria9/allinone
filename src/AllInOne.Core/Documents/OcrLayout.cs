using SkiaSharp;

namespace AllInOne.Core.Documents;

/// <summary>
/// Regroupe les lignes lues par OCR en paragraphes, dans l'ordre de lecture : découpe récursive de la page en colonnes
/// (bandes verticales séparées par une gouttière) et en bandes horizontales, puis fusion des lignes qui se suivent.
/// </summary>
internal static class OcrLayout
{
    /// <summary>Largeur moyenne d'un caractère, en proportion de la taille de police (≈ 0,45 em pour un texte courant).</summary>
    private const double AverageCharWidthEm = 0.45;

    /// <param name="lines">Lignes reconnues (coordonnées en pixels).</param>
    /// <param name="scale">Pixels par point (300 ppp = 4,17).</param>
    /// <param name="topOffset">Position (en points depuis le haut de la page) du haut de l'image analysée.</param>
    /// <param name="leftOffset">Position (en points) du bord gauche de l'image analysée (non utilisée pour l'instant).</param>
    /// <param name="fromImage">Texte d'une image dans une page (figure, capture) : jamais un titre du document.</param>
    public static List<PdfTextExtractor.RawBlock> ToBlocks(IReadOnlyList<OcrLine> lines, double scale, double topOffset, double leftOffset,
        bool fromImage = false)
    {
        var ordered = new List<OcrLine>();
        Order(lines.ToList(), ordered);

        var blocks = new List<PdfTextExtractor.RawBlock>();
        var current = new List<OcrLine>();
        foreach (var line in ordered)
        {
            if (current.Count > 0 && !Continues(current, line))
            {
                blocks.Add(ToBlock(current, scale, topOffset, fromImage));
                current = new();
            }
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(ToBlock(current, scale, topOffset, fromImage));
        return blocks;
    }

    /// <summary>Taille de police estimée d'une ligne (en pixels), d'après la largeur moyenne de ses caractères.</summary>
    private static double Size(OcrLine line) => line.Bounds.Width / (double)Math.Max(1, line.Text.Length) / AverageCharWidthEm;

    /// <summary>La ligne prolonge-t-elle le paragraphe en cours ?</summary>
    private static bool Continues(List<OcrLine> paragraph, OcrLine next)
    {
        var last = paragraph[^1];
        var height = paragraph.Average(l => l.Bounds.Height);
        var gap = next.Bounds.Top - last.Bounds.Bottom;
        // Dans un paragraphe, les cadres des lignes se touchent presque ; entre deux paragraphes, il y a un blanc.
        if (gap > height * 0.35 || gap < -height * 0.5) return false;
        // Les deux lignes doivent être l'une sous l'autre.
        var overlap = Math.Min(last.Bounds.Right, next.Bounds.Right) - Math.Max(last.Bounds.Left, next.Bounds.Left);
        if (overlap < Math.Min(last.Bounds.Width, next.Bounds.Width) * 0.3) return false;
        // Changement de taille de caractères : titre puis texte, par exemple.
        var ratio = Size(next) / Size(last);
        if (ratio > 1.25 || ratio < 0.8) return false;
        // Ligne précédente courte et terminée par une ponctuation finale : fin de paragraphe.
        var width = paragraph.Max(l => l.Bounds.Width);
        if (last.Bounds.Width < width * 0.75 && last.Text[^1] is '.' or '!' or '?' or ':' && paragraph.Count > 1) return false;
        return true;
    }

    private static PdfTextExtractor.RawBlock ToBlock(List<OcrLine> lines, double scale, double topOffset, bool fromImage)
    {
        var letters = lines.Sum(l => l.Text.Length);
        var size = lines.Sum(l => Size(l) * l.Text.Length) / Math.Max(1, letters) / scale;
        return new PdfTextExtractor.RawBlock(lines.Select(l => l.Text).ToList(), size, letters, Bold: false,
            Top: topOffset + lines.Min(l => l.Bounds.Top) / scale, FromImage: fromImage);
    }

    /// <summary>Découpe récursive (« XY-cut ») : colonnes d'abord, de gauche à droite, puis bandes de haut en bas.</summary>
    private static void Order(List<OcrLine> lines, List<OcrLine> output)
    {
        if (lines.Count <= 1)
        {
            output.AddRange(lines);
            return;
        }
        if (SplitColumns(lines) is { } columns)
        {
            foreach (var column in columns) Order(column, output);
            return;
        }
        if (SplitBands(lines) is { } bands)
        {
            foreach (var band in bands) Order(band, output);
            return;
        }
        output.AddRange(lines.OrderBy(l => l.Bounds.Top).ThenBy(l => l.Bounds.Left));
    }

    /// <summary>
    /// Colonnes séparées par une gouttière verticale que ne traverse aucune ligne. Seules les vraies colonnes de texte
    /// comptent (chaque côté occupe au moins 40 % de la hauteur), pour ne pas séparer les deux moitiés d'un formulaire.
    /// </summary>
    private static List<List<OcrLine>>? SplitColumns(List<OcrLine> lines)
    {
        var left = lines.Min(l => l.Bounds.Left);
        var right = lines.Max(l => l.Bounds.Right);
        var minGap = Math.Max(8, (right - left) * 0.015);
        var groups = Split(lines, l => l.Bounds.Left, l => l.Bounds.Right, minGap);
        if (groups is null) return null;
        var top = lines.Min(l => l.Bounds.Top);
        var height = Math.Max(1, lines.Max(l => l.Bounds.Bottom) - top);
        var tall = groups.All(g => g.Sum(l => l.Bounds.Height) >= height * 0.4);
        return tall ? groups : null;
    }

    /// <summary>
    /// Bandes horizontales séparées par un espace vide sur toute la largeur, plus haut que l'interligne : on ne coupe
    /// pas entre deux lignes d'un même paragraphe, sinon deux colonnes dont les lignes sont alignées seraient lues
    /// ligne à ligne, en alternant d'une colonne à l'autre.
    /// </summary>
    private static List<List<OcrLine>>? SplitBands(List<OcrLine> lines)
    {
        var heights = lines.Select(l => l.Bounds.Height).Order().ToList();
        return Split(lines, l => l.Bounds.Top, l => l.Bounds.Bottom, Math.Max(2, heights[heights.Count / 2] * 0.8));
    }

    /// <summary>Découpe selon les trous de la projection des lignes sur un axe (null s'il n'y en a pas).</summary>
    private static List<List<OcrLine>>? Split(List<OcrLine> lines, Func<OcrLine, int> start, Func<OcrLine, int> end, double minGap)
    {
        var sorted = lines.OrderBy(start).ToList();
        var groups = new List<List<OcrLine>> { new() { sorted[0] } };
        var reach = end(sorted[0]);
        foreach (var line in sorted.Skip(1))
        {
            if (start(line) - reach >= minGap) groups.Add(new());
            groups[^1].Add(line);
            reach = Math.Max(reach, end(line));
        }
        return groups.Count > 1 ? groups : null;
    }
}
