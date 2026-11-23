using AllInOne.Core.Localization;

namespace AllInOne.Core.Documents;

public sealed record PdfToDocumentResult(int Pages, int Paragraphs, int Characters, int PagesWithoutText);

/// <summary>Convertit un PDF en document Word (.docx), OpenDocument (.odt) ou texte (.txt), sans connexion Internet.</summary>
public static class PdfToDocument
{
    public static PdfToDocumentResult Convert(string pdfPath, string outputPath, TextDocumentFormat format,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetFullPath(pdfPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("Le fichier créé doit être différent du PDF d'origine."));
        var document = PdfTextExtractor.Extract(pdfPath, progress, cancellationToken);
        if (document.CharacterCount == 0)
            throw new InvalidOperationException(L.T("Aucun texte trouvé dans ce PDF : c'est sans doute un document scanné (des images de pages). LocalConvert ne fait pas de reconnaissance de caractères (OCR)."));
        cancellationToken.ThrowIfCancellationRequested();
        TextDocumentWriter.Write(document, outputPath, format);
        return new PdfToDocumentResult(document.Pages.Count, document.Pages.Sum(p => p.Paragraphs.Count), document.CharacterCount,
            document.Pages.Count(p => p.Paragraphs.Count == 0));
    }
}
