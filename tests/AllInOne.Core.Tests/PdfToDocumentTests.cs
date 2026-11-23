using System.IO.Compression;
using System.Xml.Linq;
using AllInOne.Core.Documents;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;

namespace AllInOne.Core.Tests;

public class PdfToDocumentTests
{
    /// <summary>
    /// PDF de deux pages : un titre (24 pt), un intertitre (15 pt), un paragraphe de trois lignes (11 pt) dont un mot
    /// coupé en fin de ligne, une ligne en gras, puis un paragraphe sur la seconde page. Le générateur de PDF de PdfPig
    /// n'encode bien que l'ASCII avec les polices standard : les textes de test sont donc sans accents.
    /// </summary>
    private static string SamplePdf(TestFiles files)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        var page = builder.AddPage(PageSize.A4);
        page.AddText("Rapport annuel", 24, new PdfPoint(72, 760), bold);
        page.AddText("Introduction", 15, new PdfPoint(72, 710), bold);
        page.AddText("Ce document sert de test pour la conversion des", 11, new PdfPoint(72, 680), regular);
        page.AddText("fichiers PDF en documents modifiables, avec un exem-", 11, new PdfPoint(72, 666), regular);
        page.AddText("ple de mot coupe en fin de ligne.", 11, new PdfPoint(72, 652), regular);
        page.AddText("Une remarque importante en gras.", 11, new PdfPoint(72, 610), bold);

        var second = builder.AddPage(PageSize.A4);
        second.AddText("Seconde page du document.", 11, new PdfPoint(72, 760), regular);

        var path = files.Path_("rapport.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public void ExtractsParagraphsHeadingsAndPages()
    {
        using var files = new TestFiles();
        var doc = PdfTextExtractor.Extract(SamplePdf(files));

        Assert.Equal(2, doc.Pages.Count);
        var first = doc.Pages[0].Paragraphs;
        Assert.Equal(new TextParagraph("Rapport annuel", 1), first[0]);
        Assert.Equal(new TextParagraph("Introduction", 2), first[1]);
        Assert.Equal("Ce document sert de test pour la conversion des fichiers PDF en documents modifiables, avec un exemple de mot coupe en fin de ligne.",
            first[2].Text);
        Assert.Equal(0, first[2].HeadingLevel);
        Assert.Equal(new TextParagraph("Une remarque importante en gras.", 0, Bold: true), first[3]);
        Assert.Equal("Seconde page du document.", Assert.Single(doc.Pages[1].Paragraphs).Text);
        Assert.Equal(595, Math.Round(doc.Pages[0].WidthPoints));
    }

    [Fact]
    public void WritesAWordDocument()
    {
        using var files = new TestFiles();
        var output = files.Path_("rapport.docx");
        var result = PdfToDocument.Convert(SamplePdf(files), output, TextDocumentFormat.Docx);
        Assert.Equal(2, result.Pages);
        Assert.Equal(5, result.Paragraphs);

        using var zip = ZipFile.OpenRead(output);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("word/styles.xml"));
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var xml = XDocument.Load(zip.GetEntry("word/document.xml")!.Open());
        var paragraphs = xml.Descendants(w + "p").ToList();
        Assert.Equal(5, paragraphs.Count);
        Assert.Equal("Heading1", paragraphs[0].Descendants(w + "pStyle").Single().Attribute(w + "val")!.Value);
        Assert.Equal("Rapport annuel", string.Concat(paragraphs[0].Descendants(w + "t").Select(t => t.Value)));
        Assert.Equal("Heading2", paragraphs[1].Descendants(w + "pStyle").Single().Attribute(w + "val")!.Value);
        Assert.Contains("coupe en fin de ligne", paragraphs[2].Value);
        Assert.NotEmpty(paragraphs[3].Descendants(w + "b"));
        Assert.NotEmpty(paragraphs[4].Descendants(w + "pageBreakBefore"));
        Assert.Equal("11900", xml.Descendants(w + "pgSz").Single().Attribute(w + "w")!.Value); // A4 : 21 cm
        foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(".xml") || e.Name.EndsWith(".rels")))
            XDocument.Load(e.Open()); // chaque partie est du XML bien formé
    }

    [Fact]
    public void WritesAnOpenDocumentText()
    {
        using var files = new TestFiles();
        var output = files.Path_("rapport.odt");
        PdfToDocument.Convert(SamplePdf(files), output, TextDocumentFormat.Odt);

        using var zip = ZipFile.OpenRead(output);
        var mimetype = zip.Entries[0];
        Assert.Equal("mimetype", mimetype.FullName);
        Assert.Equal(mimetype.Length, mimetype.CompressedLength); // non compressé, comme l'exige le format
        Assert.Equal("application/vnd.oasis.opendocument.text", new StreamReader(mimetype.Open()).ReadToEnd());

        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var content = XDocument.Load(zip.GetEntry("content.xml")!.Open());
        var headings = content.Descendants(text + "h").ToList();
        Assert.Equal(["Rapport annuel", "Introduction"], headings.Select(h => h.Value));
        Assert.Equal("2", headings[1].Attribute(text + "outline-level")!.Value);
        var paragraphs = content.Descendants(text + "p").ToList();
        Assert.Equal(3, paragraphs.Count);
        Assert.Equal("B", paragraphs[1].Attribute(text + "style-name")!.Value);
        Assert.Equal("PB", paragraphs[2].Attribute(text + "style-name")!.Value);
        XDocument.Load(zip.GetEntry("styles.xml")!.Open());
        XDocument.Load(zip.GetEntry("META-INF/manifest.xml")!.Open());
    }

    [Fact]
    public void WritesPlainText()
    {
        using var files = new TestFiles();
        var output = files.Path_("rapport.txt");
        PdfToDocument.Convert(SamplePdf(files), output, TextDocumentFormat.Txt);
        var lines = File.ReadAllText(output).Split("\n\n");
        Assert.Equal(5, lines.Length);
        Assert.Equal("Rapport annuel", lines[0]);
        Assert.Equal("Seconde page du document.\n", lines[4]);
    }

    [Fact]
    public void RefusesAPdfWithoutText()
    {
        using var files = new TestFiles();
        var scanned = files.Pdf("scan.pdf", 2); // pages sans texte, comme un scan
        var output = files.Path_("scan.docx");
        var ex = Assert.Throws<InvalidOperationException>(() => PdfToDocument.Convert(scanned, output, TextDocumentFormat.Docx));
        Assert.Contains("OCR", ex.Message);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("docx", TextDocumentFormat.Docx)]
    [InlineData("Word", TextDocumentFormat.Docx)]
    [InlineData(".odt", TextDocumentFormat.Odt)]
    [InlineData("odf", TextDocumentFormat.Odt)]
    [InlineData("TXT", TextDocumentFormat.Txt)]
    public void ParsesFormats(string text, TextDocumentFormat expected) => Assert.Equal(expected, TextDocumentFormats.Parse(text));

    [Fact]
    public void JoinsLinesAndCleansText()
    {
        Assert.Equal("un exemple de mot", PdfTextExtractor.JoinLines(["un exem-", "ple de mot"]));
        Assert.Equal("porte- Monnaie", PdfTextExtractor.JoinLines(["porte-", "Monnaie"]));
        Assert.Equal("a b", PdfTextExtractor.Clean("  a \u0001\t b "));
        Assert.Equal("ok 😀", PdfTextExtractor.Clean("ok 😀\ud83d"));
    }
}
