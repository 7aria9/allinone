using AllInOne.Core.Pdf;
using ImageMagick;

namespace AllInOne.Core.Tests;

public class PdfToolsTests : IDisposable
{
    private readonly TestFiles _files = new();
    public void Dispose() => _files.Dispose();

    [Fact]
    public void ImagesToPdf_creates_one_page_per_image()
    {
        var images = new[]
        {
            _files.Image("a.png", 300, 200, "red"),
            _files.Image("b.jpg", 200, 300, "blue", MagickFormat.Jpeg),
            _files.Image("c.bmp", 50, 50, "green", MagickFormat.Bmp),
        };
        var output = _files.Path_("out.pdf");

        PdfTools.ImagesToPdf(images, output, new ImagePageOptions { PageSize = PageSizeMode.A4 });

        Assert.Equal(3, PdfInfo.GetPageCount(output));
        // a.png est en paysage (auto), b.jpg en portrait.
        Assert.Equal(new double[] { 842, 595, 595 }, TestFiles.PageWidths(output));
    }

    [Fact]
    public void ImagesToPdf_fit_image_uses_image_size()
    {
        var image = _files.Image("a.png", 960, 480, "red");
        var output = _files.Path_("out.pdf");

        PdfTools.ImagesToPdf(new[] { image }, output,
            new ImagePageOptions { PageSize = PageSizeMode.FitImage, MarginMm = 0, DefaultDpi = 96 });

        // 960 px à 96 dpi = 10 pouces = 720 points (si l'image ne déclare pas d'autre résolution).
        var width = TestFiles.PageWidths(output)[0];
        Assert.InRange(width, 700, 740);
    }

    [Fact]
    public void ImagesToPdf_includes_every_tiff_frame()
    {
        var tiff = _files.Path_("multi.tif");
        using (var collection = new MagickImageCollection())
        {
            collection.Add(new MagickImage(MagickColors.Red, 40, 40));
            collection.Add(new MagickImage(MagickColors.Blue, 40, 40));
            collection.Write(tiff, MagickFormat.Tiff);
        }
        var output = _files.Path_("out.pdf");

        PdfTools.ImagesToPdf(new[] { tiff }, output);

        Assert.Equal(2, PdfInfo.GetPageCount(output));
    }

    [Fact]
    public void Merge_keeps_order_of_files_and_pages()
    {
        var a = _files.Pdf("a.pdf", 2);   // 100, 110
        var b = _files.Pdf("b.pdf", 3);   // 100, 110, 120
        var output = _files.Path_("merged.pdf");

        PdfTools.Merge(new[] { b, a }, output);

        Assert.Equal(new double[] { 100, 110, 120, 100, 110 }, TestFiles.PageWidths(output));
    }

    [Fact]
    public void DeletePages_removes_selected_pages()
    {
        var pdf = _files.Pdf("a.pdf", 4);
        var output = _files.Path_("out.pdf");

        PdfTools.DeletePages(pdf, output, new[] { 0, 2 });

        Assert.Equal(new double[] { 110, 130 }, TestFiles.PageWidths(output));
    }

    [Fact]
    public void DeletePages_refuses_to_delete_every_page()
    {
        var pdf = _files.Pdf("a.pdf", 2);
        Assert.Throws<InvalidOperationException>(() => PdfTools.DeletePages(pdf, _files.Path_("o.pdf"), new[] { 0, 1 }));
    }

    [Fact]
    public void ReorderPages_applies_new_order_and_can_overwrite_input()
    {
        var pdf = _files.Pdf("a.pdf", 3);

        PdfTools.ReorderPages(pdf, pdf, new[] { 2, 0, 1 });

        Assert.Equal(new double[] { 120, 100, 110 }, TestFiles.PageWidths(pdf));
    }

    [Fact]
    public void MovePage_moves_one_page()
    {
        var pdf = _files.Pdf("a.pdf", 4);
        var output = _files.Path_("out.pdf");

        PdfTools.MovePage(pdf, output, 0, 3);

        Assert.Equal(new double[] { 110, 120, 130, 100 }, TestFiles.PageWidths(output));
    }

    [Fact]
    public void InsertPages_inserts_pdf_and_image_pages_at_position()
    {
        var pdf = _files.Pdf("a.pdf", 2);
        var other = _files.Pdf("b.pdf", 1);
        var image = _files.Image("img.png", 100, 100, "red");
        var output = _files.Path_("out.pdf");

        PdfTools.InsertPages(pdf, output, 1, new[] { other, image },
            new ImagePageOptions { PageSize = PageSizeMode.Letter });

        Assert.Equal(new double[] { 100, 100, 612, 110 }, TestFiles.PageWidths(output));
    }

    [Fact]
    public void RotatePages_rotates_only_selected_pages()
    {
        var pdf = _files.Pdf("a.pdf", 3);
        var output = _files.Path_("out.pdf");

        PdfTools.RotatePages(pdf, output, new[] { 1 }, 90);
        PdfTools.RotatePages(output, output, new[] { 1, 2 }, -90);

        Assert.Equal(new[] { 0, 0, 270 }, TestFiles.PageRotations(output));
    }

    [Fact]
    public void Composer_builds_mixed_document()
    {
        var pdf = _files.Pdf("a.pdf", 3);
        var image = _files.Image("img.png", 100, 100, "red");
        var output = _files.Path_("out.pdf");

        PdfComposer.Build(new[]
        {
            PageSource.FromPdf(pdf, 2),
            PageSource.FromImage(image, rotation: 90),
            PageSource.FromPdf(pdf, 0, rotation: 180),
        }, output);

        Assert.Equal(new double[] { 120, 595, 100 }, TestFiles.PageWidths(output));
        Assert.Equal(new[] { 0, 90, 180 }, TestFiles.PageRotations(output));
    }

    [Fact]
    public void SplitPages_writes_one_file_per_page()
    {
        var pdf = _files.Pdf("doc.pdf", 3);
        var files = PdfTools.SplitPages(pdf, _files.Path_("split"));

        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.Equal(1, PdfInfo.GetPageCount(f)));
        Assert.EndsWith("doc_page01.pdf", files[0]);
    }
}
