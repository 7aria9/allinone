using AllInOne.Core.Localization;

namespace AllInOne.Core.Documents;

/// <param name="Pages">Nombre de pages.</param>
/// <param name="Paragraphs">Nombre de paragraphes écrits.</param>
/// <param name="Characters">Nombre de caractères écrits.</param>
/// <param name="PagesWithoutText">Pages restées vides (sans texte, et non lues par OCR ou sans texte reconnu).</param>
/// <param name="PagesReadByOcr">Pages scannées lues par OCR.</param>
/// <param name="ImagesReadByOcr">Images contenant du texte lues par OCR dans des pages normales.</param>
public sealed record PdfToDocumentResult(int Pages, int Paragraphs, int Characters, int PagesWithoutText,
    int PagesReadByOcr = 0, int ImagesReadByOcr = 0);

/// <summary>
/// Convertit un PDF, ou une image (photo, scan), en document Word (.docx), OpenDocument (.odt) ou texte (.txt),
/// sans connexion Internet. Les scans et les images sont lus par OCR.
/// </summary>
public static class PdfToDocument
{
    public static PdfToDocumentResult Convert(string inputPath, string outputPath, TextDocumentFormat format,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        Convert(inputPath, outputPath, format, new TextExtractionOptions(), progress, cancellationToken);

    public static PdfToDocumentResult Convert(string inputPath, string outputPath, TextDocumentFormat format,
        TextExtractionOptions options, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("Le fichier créé doit être différent du fichier d'origine."));

        var isPdf = string.Equals(Path.GetExtension(inputPath), ".pdf", StringComparison.OrdinalIgnoreCase);
        ExtractedDocument document;
        if (isPdf)
        {
            document = PdfTextExtractor.Extract(inputPath, options, progress, cancellationToken);
        }
        else
        {
            if (!options.Ocr) throw new InvalidOperationException(L.T("Le texte d'une image ne peut être lu que par OCR : active l'OCR."));
            if (TextRecognizer.UnavailableReason is { } why) throw new InvalidOperationException(why);
            document = PdfTextExtractor.ExtractImage(inputPath, cancellationToken);
            progress?.Report(1);
        }

        if (document.CharacterCount == 0)
        {
            var ocrRan = options.Ocr && TextRecognizer.IsAvailable;
            throw new InvalidOperationException(
                !isPdf ? L.T("Aucun texte reconnu dans cette image.")
                : ocrRan ? L.T("Aucun texte trouvé dans ce PDF, même par reconnaissance de caractères (OCR).")
                : TextRecognizer.UnavailableReason is { } why
                    ? L.T("Aucun texte trouvé dans ce PDF : c'est sans doute un document scanné (des images de pages).") + " " + why
                    : L.T("Aucun texte trouvé dans ce PDF : c'est sans doute un document scanné (des images de pages). Active l'OCR pour lire son texte."));
        }
        cancellationToken.ThrowIfCancellationRequested();
        TextDocumentWriter.Write(document, outputPath, format);
        return new PdfToDocumentResult(document.Pages.Count, document.Pages.Sum(p => p.Paragraphs.Count), document.CharacterCount,
            document.Pages.Count(p => p.Paragraphs.Count == 0),
            document.Pages.Count(p => p.ReadByOcr), document.Pages.Sum(p => p.ImagesReadByOcr));
    }
}
