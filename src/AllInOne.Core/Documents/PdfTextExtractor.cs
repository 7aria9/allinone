using System.Text;
using SkiaSharp;
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

/// <summary>
/// Une page extraite : ses paragraphes dans l'ordre de lecture, sa taille d'origine en points, si elle a été lue
/// entièrement par OCR (page scannée) et combien de ses images contenant du texte ont été lues par OCR.
/// </summary>
public sealed record TextPage(IReadOnlyList<TextParagraph> Paragraphs, double WidthPoints, double HeightPoints,
    bool ReadByOcr = false, int ImagesReadByOcr = 0);

/// <summary>Le texte d'un PDF, page par page.</summary>
public sealed record ExtractedDocument(IReadOnlyList<TextPage> Pages)
{
    public int CharacterCount => Pages.Sum(p => p.Paragraphs.Sum(x => x.Text.Length));
}

/// <summary>Options d'extraction.</summary>
public sealed record TextExtractionOptions
{
    /// <summary>
    /// Lit par OCR les pages sans texte (scans) et les images qui contiennent du texte. Si l'OCR n'est pas disponible
    /// sur l'ordinateur, il est ignoré.
    /// </summary>
    public bool Ocr { get; init; } = true;

    /// <summary>Résolution de rendu des pages pour l'OCR.</summary>
    public int OcrDpi { get; init; } = 300;
}

/// <summary>
/// Extrait le texte d'un PDF (avec PdfPig, licence Apache 2.0) en reconstituant les paragraphes et l'ordre de lecture
/// (colonnes comprises), et repère les titres d'après la taille des caractères. Les pages scannées et les images qui
/// contiennent du texte sont lues par OCR (voir <see cref="TextRecognizer"/>). Les images elles-mêmes, les tableaux et la
/// mise en page exacte ne sont pas repris.
/// </summary>
public static class PdfTextExtractor
{
    /// <summary>En dessous de ce nombre de caractères, une page est considérée comme scannée et lue entièrement par OCR.</summary>
    private const int ScannedPageMaxLetters = 20;

    public static ExtractedDocument Extract(string pdfPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        Extract(pdfPath, new TextExtractionOptions(), progress, cancellationToken);

    public static ExtractedDocument Extract(string pdfPath, TextExtractionOptions options, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
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
            var ocr = options.Ocr && TextRecognizer.IsAvailable;
            byte[]? pdfBytes = null;
            var raw = new List<RawPage>();
            for (var n = 1; n <= document.NumberOfPages; n++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(n);
                var blocks = ReadBlocks(page, out var letterCount);
                var rawPage = new RawPage(blocks, page.Width, page.Height);
                if (ocr)
                {
                    pdfBytes ??= File.ReadAllBytes(pdfPath);
                    rawPage = AddOcr(rawPage, page, letterCount, pdfBytes, n - 1, options.OcrDpi, cancellationToken);
                }
                raw.Add(rawPage);
                progress?.Report(0.95 * n / document.NumberOfPages);
            }

            var result = Finish(raw);
            progress?.Report(1);
            return result;
        }
    }

    /// <summary>Lit par OCR le texte d'une image (photo, capture, scan) : une page.</summary>
    public static ExtractedDocument ExtractImage(string imagePath, CancellationToken cancellationToken = default)
    {
        using var bitmap = LoadBitmap(imagePath);
        var lines = TextRecognizer.Recognize(bitmap, cancellationToken: cancellationToken);
        // Une image n'a pas de taille physique fiable : on la compte à 300 ppp pour estimer la taille des caractères.
        const double scale = 300 / 72.0;
        var blocks = OcrLayout.ToBlocks(lines, scale, 0, 0);
        return Finish([new RawPage(blocks, 595.3, 841.9, ReadByOcr: true)]);
    }

    private static ExtractedDocument Finish(List<RawPage> raw)
    {
        var bodySize = BodyFontSize(raw.SelectMany(p => p.Blocks));
        // Les titres les plus grands deviennent « Titre 1 », les autres « Titre 2 ».
        var largestHeading = raw.SelectMany(p => p.Blocks).Where(b => IsHeading(b, bodySize)).Select(b => Rounded(b.FontSize))
            .DefaultIfEmpty(0).Max();
        var pages = raw.Select(p => new TextPage(
            p.Blocks.Select(b => ToParagraph(b, bodySize, largestHeading)).Where(x => x.Text.Length > 0).ToList(),
            p.Width, p.Height, p.ReadByOcr, p.ImagesReadByOcr)).ToList();
        return new ExtractedDocument(pages);
    }

    /// <summary>Bloc de texte d'une page : ses lignes, la taille moyenne de ses caractères, s'ils sont tous gras, et le haut du bloc (points, depuis le haut de la page).</summary>
    internal sealed record RawBlock(IReadOnlyList<string> Lines, double FontSize, int Letters, bool Bold, double Top = 0, bool FromImage = false);

    private sealed record RawPage(List<RawBlock> Blocks, double Width, double Height, bool ReadByOcr = false, int ImagesReadByOcr = 0);

    private static List<RawBlock> ReadBlocks(Page page, out int letterCount)
    {
        var letters = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        letterCount = letters.Count;
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
            result.Add(new RawBlock(lines, size, blockLetters.Count, bold, page.Height - block.BoundingBox.Top));
        }
        return result;
    }

    // ---------- OCR ----------

    /// <summary>
    /// Page scannée (presque sans texte) : lue entièrement par OCR. Sinon, les images assez grandes qui ne sont pas
    /// déjà recouvertes de texte (cas des PDF scannés puis « océrisés ») sont lues par OCR et leur texte est inséré
    /// à leur place dans l'ordre de lecture.
    /// </summary>
    private static RawPage AddOcr(RawPage page, Page pdfPage, int letterCount, byte[] pdfBytes, int pageIndex, int dpi,
        CancellationToken cancellationToken)
    {
        var crop = pdfPage.CropBox.Bounds;
        var scale = dpi / 72.0;
        SKBitmap? rendered = null;
        SKBitmap Render() => rendered ??= PDFtoImage.Conversion.ToImage(pdfBytes, pageIndex, null,
            new PDFtoImage.RenderOptions { Dpi = dpi, WithAnnotations = true, WithFormFill = true });
        try
        {
            if (letterCount < ScannedPageMaxLetters)
            {
                var lines = TextRecognizer.Recognize(Render(), cancellationToken: cancellationToken);
                if (lines.Count == 0) return page;
                return page with { Blocks = OcrLayout.ToBlocks(lines, scale, 0, 0), ReadByOcr = true };
            }

            // Images à lire : rotation de page non gérée pour le positionnement (cas rare), et images trop petites ignorées.
            if (pdfPage.Rotation.Value % 360 != 0) return page;
            var pageArea = crop.Width * crop.Height;
            var candidates = pdfPage.GetImages()
                .Select(i => i.BoundingBox)
                .Where(b => b.Width >= 72 && b.Height >= 24 && b.Width * b.Height >= pageArea * 0.02)
                .Where(b => pdfPage.Letters.Count(l => Inside(b, l.BoundingBox.Centroid)) < 5)
                .ToList();
            if (candidates.Count == 0) return page;

            var blocks = new List<RawBlock>(page.Blocks);
            var read = 0;
            foreach (var bounds in candidates.OrderBy(b => -b.Top))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = Render();
                var area = SKRectI.Intersect(new SKRectI(0, 0, bitmap.Width, bitmap.Height), new SKRectI(
                    (int)Math.Floor((bounds.Left - crop.Left) * scale), (int)Math.Floor((crop.Top - bounds.Top) * scale),
                    (int)Math.Ceiling((bounds.Right - crop.Left) * scale), (int)Math.Ceiling((crop.Top - bounds.Bottom) * scale)));
                if (area.Width < 32 || area.Height < 16) continue;
                using var part = new SKBitmap(area.Width, area.Height);
                if (!bitmap.ExtractSubset(part, area)) continue;
                // Seuil de confiance plus élevé que pour une page : une photo sans texte ne doit pas produire de « texte » parasite.
                var lines = TextRecognizer.Recognize(part, 0.75f, cancellationToken).Where(l => l.Text.Length >= 3).ToList();
                if (lines.Count == 0) continue;
                var top = crop.Top - bounds.Top;
                var ocrBlocks = OcrLayout.ToBlocks(lines, scale, top, 0, fromImage: true);
                // Inséré avant le premier bloc (dans l'ordre de lecture) qui commence plus bas que l'image.
                var at = blocks.FindIndex(b => b.Top > top + 1);
                blocks.InsertRange(at < 0 ? blocks.Count : at, ocrBlocks);
                read++;
            }
            return read == 0 ? page : page with { Blocks = blocks, ImagesReadByOcr = read };
        }
        finally
        {
            rendered?.Dispose();
        }
    }

    private static bool Inside(UglyToad.PdfPig.Core.PdfRectangle r, UglyToad.PdfPig.Core.PdfPoint p) =>
        p.X >= r.Left && p.X <= r.Right && p.Y >= r.Bottom && p.Y <= r.Top;

    private static SKBitmap LoadBitmap(string imagePath)
    {
        // ImageMagick lit tous les formats (HEIC, TIFF, WebP…) et applique l'orientation EXIF des photos.
        using var image = new ImageMagick.MagickImage(imagePath);
        image.AutoOrient();
        image.Format = ImageMagick.MagickFormat.Png;
        var bitmap = SKBitmap.Decode(image.ToByteArray());
        return bitmap ?? throw new InvalidOperationException(L.T("Image illisible : {0}", Path.GetFileName(imagePath)));
    }

    private static bool IsBold(Letter letter) =>
        letter.FontDetails?.IsBold == true
        || (letter.FontName ?? "").Contains("Bold", StringComparison.OrdinalIgnoreCase)
        || (letter.FontName ?? "").Contains("Black", StringComparison.OrdinalIgnoreCase)
        || (letter.FontName ?? "").Contains("Heavy", StringComparison.OrdinalIgnoreCase);

    /// <summary>Taille de caractère la plus fréquente (celle du texte courant), arrondie au demi-point.</summary>
    private static double BodyFontSize(IEnumerable<RawBlock> blocks)
    {
        var bySize = blocks.Where(b => !b.FromImage).GroupBy(b => Rounded(b.FontSize)).Select(g => (Size: g.Key, Letters: g.Sum(b => b.Letters))).ToList();
        return bySize.Count == 0 ? 0 : bySize.MaxBy(s => s.Letters).Size;
    }

    private static double Rounded(double size) => Math.Round(size * 2) / 2;

    /// <summary>Un titre : nettement plus grand que le texte courant, et court (quelques lignes au plus).</summary>
    private static bool IsHeading(RawBlock block, double bodySize) =>
        bodySize > 0 && !block.FromImage && block.Lines.Count <= 3 && block.Lines.Sum(l => l.Length) <= 200 && block.FontSize >= bodySize * 1.15;

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
