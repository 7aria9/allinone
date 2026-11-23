using AllInOne.Core.Images;
using AllInOne.Core.Pdf;
using ImageMagick;

namespace AllInOne.Core.Tests;

public class RenderingAndConversionTests : IDisposable
{
    private readonly TestFiles _files = new();
    public void Dispose() => _files.Dispose();

    [Theory]
    [InlineData(ImageFormatKind.Png)]
    [InlineData(ImageFormatKind.Jpeg)]
    [InlineData(ImageFormatKind.Bmp)]
    [InlineData(ImageFormatKind.Gif)]
    [InlineData(ImageFormatKind.Tiff)]
    [InlineData(ImageFormatKind.Webp)]
    public void PdfToImages_renders_selected_pages(ImageFormatKind format)
    {
        var pdf = _files.Pdf("doc.pdf", 3);

        var files = PdfRenderer.ToImages(pdf, _files.Path_("out"), format, dpi: 72, pageIndexes: new[] { 0, 2 });

        Assert.Equal(2, files.Count);
        Assert.EndsWith("doc_page01" + format.Extension(), files[0]);
        Assert.EndsWith("doc_page03" + format.Extension(), files[1]);
        using var img = new MagickImage(files[1]);
        Assert.Equal(120u, img.Width); // 120 points à 72 dpi = 120 px
        Assert.Equal(200u, img.Height);
    }

    [Fact]
    public void Image_drawn_into_pdf_is_visible_when_rendered()
    {
        var image = _files.Image("red.png", 200, 100, "red");
        var pdf = _files.Path_("red.pdf");
        PdfTools.ImagesToPdf(new[] { image }, pdf, new ImagePageOptions { PageSize = PageSizeMode.FitImage, MarginMm = 0 });

        var png = PdfRenderer.RenderThumbnailPng(pdf, 0, 100);
        using var rendered = new MagickImage(png);
        var pixel = rendered.GetPixels().GetPixel(50, 25)!.ToColor()!;
        Assert.True(pixel.R > 200 && pixel.G < 50 && pixel.B < 50, $"Pixel inattendu : {pixel}");
    }

    [Fact]
    public void Thumbnail_has_requested_width()
    {
        var pdf = _files.Pdf("doc.pdf", 1);
        var png = PdfRenderer.RenderThumbnailPng(pdf, 0, 50);
        using var img = new MagickImage(png);
        Assert.Equal(50u, img.Width);
        Assert.Equal(100u, img.Height);
    }

    [Theory]
    [InlineData(ImageFormatKind.Jpeg, MagickFormat.Jpeg)]
    [InlineData(ImageFormatKind.Webp, MagickFormat.WebP)]
    [InlineData(ImageFormatKind.Bmp, MagickFormat.Bmp)]
    [InlineData(ImageFormatKind.Tiff, MagickFormat.Tiff)]
    [InlineData(ImageFormatKind.Gif, MagickFormat.Gif)]
    [InlineData(ImageFormatKind.Png, MagickFormat.Png)]
    public void ConvertMany_writes_target_format(ImageFormatKind format, MagickFormat expected)
    {
        var inputs = new[]
        {
            _files.Image("a.png", 40, 30, "red"),
            _files.Image("b.jpg", 40, 30, "blue", MagickFormat.Jpeg),
        };

        var outputs = ImageConverter.ConvertMany(inputs, _files.Path_("out"), format, new ImageConversionOptions { Quality = 80 });

        Assert.Equal(2, outputs.Count);
        foreach (var output in outputs)
        {
            var info = new MagickImageInfo(output);
            Assert.True(info.Format == expected || (expected == MagickFormat.Tiff && info.Format == MagickFormat.Tif),
                $"{info.Format} au lieu de {expected}");
            Assert.Equal(40u, info.Width);
        }
    }

    [Fact]
    public void Convert_resizes_within_bounds_and_keeps_aspect_ratio()
    {
        var input = _files.Image("a.png", 400, 200, "red");
        var output = ImageConverter.Convert(input, _files.Path_("a.jpg"), ImageFormatKind.Jpeg,
            new ImageConversionOptions { MaxWidth = 100 });
        var info = new MagickImageInfo(output);
        Assert.Equal(100u, info.Width);
        Assert.Equal(50u, info.Height);
    }

    [Fact]
    public void ConvertMany_does_not_overwrite_existing_files()
    {
        var input = _files.Image("a.png", 10, 10, "red");
        var dir = _files.Path_("out");
        var first = ImageConverter.ConvertMany(new[] { input }, dir, ImageFormatKind.Jpeg)[0];
        var second = ImageConverter.ConvertMany(new[] { input }, dir, ImageFormatKind.Jpeg)[0];
        Assert.NotEqual(first, second);
        Assert.EndsWith("a (2).jpg", second);
    }
}
