using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Documents;

public enum TextDocumentFormat
{
    /// <summary>Microsoft Word (.docx, Office Open XML).</summary>
    Docx,
    /// <summary>OpenDocument Texte (.odt : LibreOffice, OpenOffice…).</summary>
    Odt,
    /// <summary>Texte brut (.txt, UTF-8).</summary>
    Txt,
}

public static class TextDocumentFormats
{
    public static string Extension(TextDocumentFormat format) => format switch
    {
        TextDocumentFormat.Docx => ".docx",
        TextDocumentFormat.Odt => ".odt",
        _ => ".txt",
    };

    public static TextDocumentFormat Parse(string text) => text.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "docx" or "word" or "doc" => TextDocumentFormat.Docx,
        "odt" or "odf" or "opendocument" => TextDocumentFormat.Odt,
        "txt" or "text" or "texte" => TextDocumentFormat.Txt,
        var other => throw new ArgumentException(L.T("Format de document inconnu : {0} (docx, odt ou txt)", other)),
    };
}

/// <summary>
/// Écrit un texte extrait en .docx, .odt ou .txt. Les fichiers Word et OpenDocument sont produits directement
/// (ce sont des archives zip de XML) : aucune bibliothèque ni logiciel de bureautique n'est nécessaire.
/// Chaque page du PDF commence une nouvelle page du document, au même format de papier.
/// </summary>
public static class TextDocumentWriter
{
    public static void Write(ExtractedDocument document, string outputPath, TextDocumentFormat format)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Écrit d'abord dans un fichier temporaire : pas de document à moitié écrit en cas d'erreur.
        var temp = outputPath + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                switch (format)
                {
                    case TextDocumentFormat.Docx: WriteDocx(document, stream); break;
                    case TextDocumentFormat.Odt: WriteOdt(document, stream); break;
                    default: WriteTxt(document, stream); break;
                }
            }
            File.Move(temp, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // ---------- Texte brut ----------

    private static void WriteTxt(ExtractedDocument document, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        var first = true;
        foreach (var page in document.Pages)
        {
            foreach (var p in page.Paragraphs)
            {
                if (!first) writer.Write("\n\n");
                writer.Write(p.Text);
                first = false;
            }
        }
        if (!first) writer.Write('\n');
    }

    // ---------- Word (.docx) ----------

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static void WriteDocx(ExtractedDocument document, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Entry(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """);
        Entry(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        Entry(zip, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        Entry(zip, "word/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Calibri" w:cs="Calibri"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:rPrDefault>
                <w:pPrDefault><w:pPr><w:spacing w:after="160" w:line="264" w:lineRule="auto"/></w:pPr></w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
                <w:pPr><w:keepNext/><w:spacing w:before="240" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr>
                <w:rPr><w:b/><w:sz w:val="36"/><w:szCs w:val="36"/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
                <w:pPr><w:keepNext/><w:spacing w:before="200" w:after="100"/><w:outlineLvl w:val="1"/></w:pPr>
                <w:rPr><w:b/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr></w:style>
            </w:styles>
            """);

        WriteXml(zip, "word/document.xml", x =>
        {
            x.WriteStartElement("w", "document", W);
            x.WriteStartElement("w", "body", W);
            for (var i = 0; i < document.Pages.Count; i++)
            {
                var page = document.Pages[i];
                var paragraphs = page.Paragraphs.Count > 0 ? page.Paragraphs : [new TextParagraph("")];
                for (var j = 0; j < paragraphs.Count; j++)
                {
                    var p = paragraphs[j];
                    x.WriteStartElement("w", "p", W);
                    var style = p.HeadingLevel switch { 1 => "Heading1", 2 => "Heading2", _ => null };
                    var newPage = i > 0 && j == 0;
                    if (style is not null || newPage)
                    {
                        x.WriteStartElement("w", "pPr", W);
                        if (style is not null) Val(x, "pStyle", style);
                        if (newPage) x.WriteElementString("w", "pageBreakBefore", W, "");
                        x.WriteEndElement();
                    }
                    if (p.Text.Length > 0)
                    {
                        x.WriteStartElement("w", "r", W);
                        if (p.Bold)
                        {
                            x.WriteStartElement("w", "rPr", W);
                            x.WriteElementString("w", "b", W, "");
                            x.WriteEndElement();
                        }
                        x.WriteStartElement("w", "t", W);
                        x.WriteAttributeString("xml", "space", null, "preserve");
                        x.WriteString(p.Text);
                        x.WriteEndElement();
                        x.WriteEndElement();
                    }
                    x.WriteEndElement();
                }
            }

            // Format du papier : celui de la première page du PDF (A4 par défaut), marges de 2 cm.
            var (width, height) = document.Pages.Count > 0 ? (document.Pages[0].WidthPoints, document.Pages[0].HeightPoints) : (595.3, 841.9);
            if (width < 72 || height < 72) (width, height) = (595.3, 841.9);
            x.WriteStartElement("w", "sectPr", W);
            x.WriteStartElement("w", "pgSz", W);
            x.WriteAttributeString("w", "w", W, Twips(width));
            x.WriteAttributeString("w", "h", W, Twips(height));
            if (width > height) x.WriteAttributeString("w", "orient", W, "landscape");
            x.WriteEndElement();
            x.WriteStartElement("w", "pgMar", W);
            foreach (var side in new[] { "top", "right", "bottom", "left" }) x.WriteAttributeString("w", side, W, "1134");
            foreach (var side in new[] { "header", "footer", "gutter" }) x.WriteAttributeString("w", side, W, side == "gutter" ? "0" : "567");
            x.WriteEndElement();
            x.WriteEndElement();

            x.WriteEndElement();
            x.WriteEndElement();
        });
    }

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement("w", name, W);
        x.WriteAttributeString("w", "val", W, value);
        x.WriteEndElement();
    }

    private static string Twips(double points) => ((int)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

    // ---------- OpenDocument (.odt) ----------

    private const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    private static void WriteOdt(ExtractedDocument document, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        // Le type MIME doit être le premier fichier de l'archive, sans compression.
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mime.Open()) s.Write("application/vnd.oasis.opendocument.text"u8);

        Entry(zip, "META-INF/manifest.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.2">
              <manifest:file-entry manifest:full-path="/" manifest:version="1.2" manifest:media-type="application/vnd.oasis.opendocument.text"/>
              <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
              <manifest:file-entry manifest:full-path="styles.xml" manifest:media-type="text/xml"/>
            </manifest:manifest>
            """);

        var (width, height) = document.Pages.Count > 0 ? (document.Pages[0].WidthPoints, document.Pages[0].HeightPoints) : (595.3, 841.9);
        if (width < 72 || height < 72) (width, height) = (595.3, 841.9);
        var cm = (double points) => (points / 72 * 2.54).ToString("0.###", CultureInfo.InvariantCulture) + "cm";
        Entry(zip, "styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-styles xmlns:office="{Office}" xmlns:style="{Style}" xmlns:text="{Text}" xmlns:fo="{Fo}" office:version="1.2">
              <office:styles>
                <style:default-style style:family="paragraph">
                  <style:paragraph-properties fo:margin-top="0cm" fo:margin-bottom="0.28cm" fo:line-height="110%"/>
                  <style:text-properties style:font-name="Liberation Sans" fo:font-family="Calibri, Carlito, 'Liberation Sans', Arial, sans-serif" fo:font-size="11pt"/>
                </style:default-style>
                <style:style style:name="Standard" style:family="paragraph" style:class="text"/>
                <style:style style:name="Heading_20_1" style:display-name="Heading 1" style:family="paragraph" style:parent-style-name="Standard" style:next-style-name="Standard" style:default-outline-level="1" style:class="text">
                  <style:paragraph-properties fo:margin-top="0.42cm" fo:margin-bottom="0.21cm" fo:keep-with-next="always"/>
                  <style:text-properties fo:font-size="18pt" fo:font-weight="bold"/>
                </style:style>
                <style:style style:name="Heading_20_2" style:display-name="Heading 2" style:family="paragraph" style:parent-style-name="Standard" style:next-style-name="Standard" style:default-outline-level="2" style:class="text">
                  <style:paragraph-properties fo:margin-top="0.35cm" fo:margin-bottom="0.18cm" fo:keep-with-next="always"/>
                  <style:text-properties fo:font-size="14pt" fo:font-weight="bold"/>
                </style:style>
              </office:styles>
              <office:automatic-styles>
                <style:page-layout style:name="pm1">
                  <style:page-layout-properties fo:page-width="{cm(width)}" fo:page-height="{cm(height)}" style:print-orientation="{(width > height ? "landscape" : "portrait")}"
                    fo:margin-top="2cm" fo:margin-bottom="2cm" fo:margin-left="2cm" fo:margin-right="2cm"/>
                </style:page-layout>
              </office:automatic-styles>
              <office:master-styles>
                <style:master-page style:name="Standard" style:page-layout-name="pm1"/>
              </office:master-styles>
            </office:document-styles>
            """);

        WriteXml(zip, "content.xml", x =>
        {
            x.WriteStartElement("office", "document-content", Office);
            x.WriteAttributeString("xmlns", "style", null, Style);
            x.WriteAttributeString("xmlns", "text", null, Text);
            x.WriteAttributeString("xmlns", "fo", null, Fo);
            x.WriteAttributeString("office", "version", Office, "1.2");

            // Styles automatiques : saut de page avant, gras, et leurs combinaisons avec les titres.
            x.WriteStartElement("office", "automatic-styles", Office);
            foreach (var (name, parent, pageBreak, bold) in new[]
                     {
                         ("PB", "Standard", true, false), ("PBB", "Standard", true, true), ("B", "Standard", false, true),
                         ("PBH1", "Heading_20_1", true, false), ("PBH2", "Heading_20_2", true, false),
                     })
            {
                x.WriteStartElement("style", "style", Style);
                x.WriteAttributeString("style", "name", Style, name);
                x.WriteAttributeString("style", "family", Style, "paragraph");
                x.WriteAttributeString("style", "parent-style-name", Style, parent);
                if (pageBreak)
                {
                    x.WriteStartElement("style", "paragraph-properties", Style);
                    x.WriteAttributeString("fo", "break-before", Fo, "page");
                    x.WriteEndElement();
                }
                if (bold)
                {
                    x.WriteStartElement("style", "text-properties", Style);
                    x.WriteAttributeString("fo", "font-weight", Fo, "bold");
                    x.WriteEndElement();
                }
                x.WriteEndElement();
            }
            x.WriteEndElement();

            x.WriteStartElement("office", "body", Office);
            x.WriteStartElement("office", "text", Office);
            for (var i = 0; i < document.Pages.Count; i++)
            {
                var paragraphs = document.Pages[i].Paragraphs.Count > 0 ? document.Pages[i].Paragraphs : [new TextParagraph("")];
                for (var j = 0; j < paragraphs.Count; j++)
                {
                    var p = paragraphs[j];
                    var newPage = i > 0 && j == 0;
                    if (p.HeadingLevel is 1 or 2)
                    {
                        x.WriteStartElement("text", "h", Text);
                        x.WriteAttributeString("text", "style-name", Text,
                            newPage ? $"PBH{p.HeadingLevel}" : $"Heading_20_{p.HeadingLevel}");
                        x.WriteAttributeString("text", "outline-level", Text, p.HeadingLevel.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        x.WriteStartElement("text", "p", Text);
                        x.WriteAttributeString("text", "style-name", Text,
                            (newPage, p.Bold) switch { (true, true) => "PBB", (true, false) => "PB", (false, true) => "B", _ => "Standard" });
                    }
                    x.WriteString(p.Text);
                    x.WriteEndElement();
                }
            }
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndElement();
        });
    }

    // ---------- Archive ----------

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static void WriteXml(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var x = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        x.WriteStartDocument(true);
        write(x);
        x.WriteEndDocument();
    }
}
