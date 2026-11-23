using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Documents;

/// <summary>Un paragraphe extrait : son texte, son niveau de titre (0 = texte normal) et s'il est entièrement en gras.</summary>
public sealed record TextParagraph(string Text, int HeadingLevel = 0, bool Bold = false);

/// <summary>Une page extraite : ses paragraphes dans l'ordre de lecture et sa taille d'origine en points.</summary>
public sealed record TextPage(IReadOnlyList<TextParagraph> Paragraphs, double WidthPoints, double HeightPoints);

/// <summary>Le texte d'un PDF, page par page.</summary>
public sealed record ExtractedDocument(IReadOnlyList<TextPage> Pages)
{
    public int CharacterCount => Pages.Sum(p => p.Paragraphs.Sum(x => x.Text.Length));
}

/// <summary>
/// Extrait le texte d'un PDF (avec PdfPig, licence Apache 2.0) en reconstituant les paragraphes et l'ordre de lecture
/// (colonnes comprises), et repère les titres d'après la taille des caractères. Les images, les tableaux et la mise en
/// page exacte ne sont pas repris. Un PDF scanné (pages en image) ne contient pas de texte : aucune reconnaissance de
/// caractères (OCR) n'est faite.
/// </summary>
public static class PdfTextExtractor
{
    public static ExtractedDocument Extract(string pdfPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfPath);
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidOperationException(L.T("Ce PDF est protégé par un mot de passe : impossible d'en lire le texte."));
        }

        using (document)
        {
            var raw = new List<(List<RawBlock> Blocks, double Width, double Height)>();
            for (var n = 1; n <= document.NumberOfPages; n++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(n);
                raw.Add((ReadBlocks(page), page.Width, page.Height));
                progress?.Report(0.9 * n / document.NumberOfPages);
            }

            var bodySize = BodyFontSize(raw.SelectMany(p => p.Blocks));
            // Les titres les plus grands deviennent « Titre 1 », les autres « Titre 2 ».
            var largestHeading = raw.SelectMany(p => p.Blocks).Where(b => IsHeading(b, bodySize)).Select(b => Rounded(b.FontSize))
                .DefaultIfEmpty(0).Max();
            var pages = raw.Select(p => new TextPage(p.Blocks.Select(b => ToParagraph(b, bodySize, largestHeading)).Where(x => x.Text.Length > 0).ToList(),
                p.Width, p.Height)).ToList();
            progress?.Report(1);
            return new ExtractedDocument(pages);
        }
    }

    /// <summary>Bloc de texte d'une page : ses lignes, la taille moyenne de ses caractères et s'ils sont tous gras.</summary>
    private sealed record RawBlock(IReadOnlyList<string> Lines, double FontSize, int Letters, bool Bold);

    private static List<RawBlock> ReadBlocks(Page page)
    {
        var letters = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        if (letters.Count == 0) return new();
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters).ToList();
        if (words.Count == 0) return new();
        IReadOnlyList<TextBlock> blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks).ToList();

        var result = new List<RawBlock>();
        foreach (var block in ordered)
        {
            var lines = block.TextLines.Select(l => string.Join(" ", l.Words.Select(w => w.Text))).ToList();
            var blockLetters = block.TextLines.SelectMany(l => l.Words).SelectMany(w => w.Letters).ToList();
            if (blockLetters.Count == 0) continue;
            var size = blockLetters.Average(l => l.PointSize);
            var bold = blockLetters.All(IsBold);
            result.Add(new RawBlock(lines, size, blockLetters.Count, bold));
        }
        return result;
    }

    private static bool IsBold(Letter letter) =>
        letter.FontDetails?.IsBold == true
        || (letter.FontName ?? "").Contains("Bold", StringComparison.OrdinalIgnoreCase)
        || (letter.FontName ?? "").Contains("Black", StringComparison.OrdinalIgnoreCase)
        || (letter.FontName ?? "").Contains("Heavy", StringComparison.OrdinalIgnoreCase);

    /// <summary>Taille de caractère la plus fréquente (celle du texte courant), arrondie au demi-point.</summary>
    private static double BodyFontSize(IEnumerable<RawBlock> blocks)
    {
        var bySize = blocks.GroupBy(b => Rounded(b.FontSize)).Select(g => (Size: g.Key, Letters: g.Sum(b => b.Letters))).ToList();
        return bySize.Count == 0 ? 0 : bySize.MaxBy(s => s.Letters).Size;
    }

    private static double Rounded(double size) => Math.Round(size * 2) / 2;

    /// <summary>Un titre : nettement plus grand que le texte courant, et court (quelques lignes au plus).</summary>
    private static bool IsHeading(RawBlock block, double bodySize) =>
        bodySize > 0 && block.Lines.Count <= 3 && block.Lines.Sum(l => l.Length) <= 200 && block.FontSize >= bodySize * 1.15;

    private static TextParagraph ToParagraph(RawBlock block, double bodySize, double largestHeading)
    {
        var text = JoinLines(block.Lines);
        var level = !IsHeading(block, bodySize) ? 0 : Rounded(block.FontSize) >= largestHeading ? 1 : 2;
        return new TextParagraph(text, level, level == 0 && block.Bold);
    }

    /// <summary>Recolle les lignes d'un paragraphe, en supprimant les coupures de mots en fin de ligne (« exem- ple »).</summary>
    internal static string JoinLines(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = Clean(raw);
            if (line.Length == 0) continue;
            if (sb.Length > 0)
            {
                var hyphenated = sb.Length >= 2 && sb[^1] is '-' or '­' && char.IsLower(sb[^2]) && char.IsLower(line[0]);
                if (hyphenated) sb.Length--;
                else sb.Append(' ');
            }
            sb.Append(line);
        }
        return sb.ToString();
    }

    /// <summary>Espaces multiples réduits à un seul ; caractères de contrôle (interdits en XML) retirés.</summary>
    internal static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = sb.Length > 0;
                continue;
            }
            if (c is '\uFFFE' or '\uFFFF') continue;
            if (char.IsSurrogate(c))
            {
                // Paire valide gardée telle quelle (émojis, symboles…), demi-paire isolée retirée.
                if (!char.IsHighSurrogate(c) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) continue;
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c).Append(text[++i]);
                continue;
            }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
