using ImageMagick;

namespace AllInOne.Core.Tests;

/// <summary>Dossier temporaire et fichiers de test générés à la volée.</summary>
public sealed class TestFiles : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "allinone-tests-" + Guid.NewGuid().ToString("N"));

    public TestFiles() => Directory.CreateDirectory(Dir);

    public string Path_(string name) => Path.Combine(Dir, name);

    public string Image(string name, uint width, uint height, string color, MagickFormat format = MagickFormat.Png)
    {
        var path = Path_(name);
        using var image = new MagickImage(new MagickColor(color), width, height);
        image.Format = format;
        image.Write(path);
        return path;
    }

    /// <summary>PDF de <paramref name="pages"/> pages, chaque page ayant une largeur distincte (100 + 10*i points) pour être reconnaissable.</summary>
    public string Pdf(string name, int pages)
    {
        var path = Path_(name);
        using var doc = new PdfSharp.Pdf.PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(100 + 10 * i);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(200);
        }
        doc.Save(path);
        return path;
    }

    public static double[] PageWidths(string pdf)
    {
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.Pages.Cast<PdfSharp.Pdf.PdfPage>().Select(p => Math.Round(p.Width.Point)).ToArray();
    }

    public static int[] PageRotations(string pdf)
    {
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.Pages.Cast<PdfSharp.Pdf.PdfPage>().Select(p => p.Rotate).ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { /* ignoré */ }
    }
}
