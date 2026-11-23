namespace AllInOne.Core.Pdf;

public static class PdfInfo
{
    public static int GetPageCount(string pdfPath)
    {
        using var doc = PdfComposer.OpenForImport(pdfPath);
        return doc.PageCount;
    }
}
