using System.Diagnostics;
using AllInOne.Core.Images;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;
using ImageMagick;

namespace AllInOne.Core.Tests;

public class TrimTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("1:02:03", 3723)]
    [InlineData("0:05,5", 5.5)]
    [InlineData(" 2.25 ", 2.25)]
    public void Parses_times(string text, double seconds)
    {
        Assert.True(Trim.TryParseTime(text, out var t));
        Assert.Equal(seconds, t!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("1:75")]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    public void Rejects_bad_times(string text) => Assert.False(Trim.TryParseTime(text, out _));

    [Fact]
    public void Empty_time_means_no_limit()
    {
        Assert.True(Trim.TryParseTime("  ", out var t));
        Assert.Null(t);
    }

    [Fact]
    public void Video_trim_seeks_before_input_and_limits_length()
    {
        var args = FfmpegArguments.Build("in.mp4", "out.mp4", VideoFormat.Mp4,
            new VideoConversionOptions { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(25.5) });
        var line = string.Join(' ', args);
        Assert.Contains("-ss 10 -i in.mp4", line);
        Assert.Contains("-t 15.5", line);
    }

    [Fact]
    public void Audio_trim_with_only_an_end()
    {
        var line = string.Join(' ', FfmpegAudioArguments.Build("in.wav", "out.mp3", AudioFormat.Mp3,
            new AudioConversionOptions { End = TimeSpan.FromSeconds(30) }));
        Assert.DoesNotContain("-ss", line);
        Assert.Contains("-t 30", line);
    }

    [Fact]
    public void End_before_start_is_refused() =>
        Assert.Throws<ArgumentException>(() => FfmpegArguments.Build("a", "b.mp4", VideoFormat.Mp4,
            new VideoConversionOptions { Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(2) }));

    [Fact]
    public void Progress_uses_the_length_of_the_extract()
    {
        Assert.Equal(15, Trim.EffectiveSeconds(100, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25)));
        Assert.Equal(90, Trim.EffectiveSeconds(100, TimeSpan.FromSeconds(10), null));
        Assert.Equal(20, Trim.EffectiveSeconds(30, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Gif_uses_a_palette_and_no_audio()
    {
        var line = string.Join(' ', FfmpegArguments.Build("in.mp4", "out.gif", VideoFormat.Gif, new VideoConversionOptions()));
        Assert.Contains("palettegen", line);
        Assert.Contains("paletteuse", line);
        Assert.Contains("fps=12", line);
        Assert.Contains("-an", line);
        Assert.Contains("-loop 0", line);
    }
}

public sealed class NewToolsTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly string? _ffmpeg = FfmpegLocator.Find();
    public void Dispose() => _files.Dispose();

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    // ---------- HEIC / AVIF

    [Fact]
    public void Heic_photos_convert_to_jpeg()
    {
        var output = _files.Path_("photo.jpg");
        ImageConverter.Convert(Sample("red-64x48.heic"), output, ImageFormatKind.Jpeg);
        using var image = new MagickImage(output);
        Assert.Equal((64u, 48u), (image.Width, image.Height));
        var pixel = image.GetPixels().GetPixel(32, 24).ToColor()!;
        Assert.True(pixel.R > 200 && pixel.G < 60 && pixel.B < 60, $"rouge attendu, obtenu {pixel}");
    }

    [Fact]
    public void Heic_photos_stay_jpeg_inside_a_pdf()
    {
        var pdf = _files.Path_("heic.pdf");
        PdfTools.ImagesToPdf(new[] { Sample("red-64x48.heic") }, pdf);
        Assert.Equal(1, PdfInfo.GetPageCount(pdf));
        Assert.Contains("/DCTDecode", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf)));
    }

    [Fact]
    public void Images_convert_to_avif()
    {
        var input = _files.Image("in.png", 80, 60, "#3366CC");
        var output = _files.Path_("out.avif");
        ImageConverter.Convert(input, output, ImageFormatKind.Avif, new ImageConversionOptions { Quality = 60 });
        using var image = new MagickImage(output);
        Assert.Equal(MagickFormat.Avif, image.Format);
        Assert.Equal(80u, image.Width);
    }

    // ---------- Compression PDF

    private string PhotoPdf(string name, MagickFormat format)
    {
        var photo = _files.Path_(name + (format == MagickFormat.Png ? ".png" : ".jpg"));
        using (var image = new MagickImage("plasma:fractal", new MagickReadSettings { Width = 3000, Height = 2200 }))
        {
            image.Quality = 95;
            image.Write(photo, format);
        }
        var pdf = _files.Path_(name + ".pdf");
        PdfTools.ImagesToPdf(new[] { photo, photo }, pdf, new ImagePageOptions { JpegQuality = 95 });
        return pdf;
    }

    [Theory]
    [InlineData(MagickFormat.Jpeg)]
    [InlineData(MagickFormat.Png)]
    public void Compression_shrinks_photo_pdfs_and_keeps_pages(MagickFormat format)
    {
        var input = PhotoPdf("photos-" + format, format);
        var output = _files.Path_("compressed-" + format + ".pdf");

        var result = PdfCompressor.Compress(input, output, PdfCompressionLevel.Medium);

        Assert.True(result.ImagesRecompressed >= 1);
        Assert.True(result.CompressedBytes < result.OriginalBytes / 2, $"{result.OriginalBytes} → {result.CompressedBytes}");
        Assert.Equal(new FileInfo(output).Length, result.CompressedBytes);
        Assert.Equal(2, PdfInfo.GetPageCount(output));
        var png = PdfRenderer.RenderThumbnailPng(output, 0, 50);
        Assert.True(png.Length > 100);
    }

    [Fact]
    public void Stronger_levels_give_smaller_files()
    {
        var input = PhotoPdf("levels", MagickFormat.Jpeg);
        var light = PdfCompressor.Compress(input, _files.Path_("light.pdf"), PdfCompressionLevel.Light);
        var strong = PdfCompressor.Compress(input, _files.Path_("strong.pdf"), PdfCompressionLevel.Strong);
        Assert.True(strong.CompressedBytes < light.CompressedBytes);
    }

    [Fact]
    public void Already_small_pdf_is_copied_unchanged()
    {
        var input = _files.Pdf("text.pdf", 3);
        var output = _files.Path_("text-compressed.pdf");
        var result = PdfCompressor.Compress(input, output);
        Assert.True(result.CompressedBytes <= result.OriginalBytes);
        Assert.Equal(3, PdfInfo.GetPageCount(output));
    }

    // ---------- Découpage PDF

    [Fact]
    public void Split_every_two_pages()
    {
        var input = _files.Pdf("five.pdf", 5);
        var files = PdfTools.SplitEvery(input, _files.Path_("every"), 2);
        Assert.Equal(new[] { "five_pages01-02.pdf", "five_pages03-04.pdf", "five_page05.pdf" }, files.Select(Path.GetFileName));
        Assert.Equal(new[] { 2, 2, 1 }, files.Select(PdfInfo.GetPageCount));
    }

    [Fact]
    public void Split_by_ranges_makes_one_file_per_range()
    {
        var input = _files.Pdf("six.pdf", 6);
        var files = PdfTools.SplitByRanges(input, _files.Path_("ranges"), "1-2; 4-");
        Assert.Equal(new[] { "six_pages01-02.pdf", "six_pages04-06.pdf" }, files.Select(Path.GetFileName));
        Assert.Equal(new[] { 2, 3 }, files.Select(PdfInfo.GetPageCount));
    }

    [Fact]
    public void Split_by_ranges_rejects_missing_pages() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfTools.SplitByRanges(_files.Pdf("two.pdf", 2), _files.Path_("bad"), "1, 5"));

    // ---------- GIF et extraits (FFmpeg réel)

    private string Clip(int seconds)
    {
        var path = _files.Path_($"clip{seconds}.mp4");
        Ffmpeg("-f", "lavfi", "-i", $"testsrc=size=320x240:rate=25:duration={seconds}", "-f", "lavfi", "-i", $"sine=duration={seconds}",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", path);
        return path;
    }

    [Fact]
    public async Task Video_to_animated_gif()
    {
        if (_ffmpeg is null) return;
        var output = _files.Path_("anim.gif");
        await VideoConverter.ConvertAsync(Clip(2), output, VideoFormat.Gif, new VideoConversionOptions { Quality = VideoQuality.Low });
        using var frames = new MagickImageCollection(output);
        Assert.True(frames.Count > 10, $"{frames.Count} images");
        Assert.Equal(320u, frames[0].Width);
    }

    [Fact]
    public async Task Video_extract_has_the_requested_length()
    {
        if (_ffmpeg is null) return;
        var output = _files.Path_("extract.mp4");
        var reported = new List<double>();
        await VideoConverter.ConvertAsync(Clip(6), output, VideoFormat.Mp4,
            new VideoConversionOptions { Quality = VideoQuality.Low, Start = TimeSpan.FromSeconds(2), End = TimeSpan.FromSeconds(4) },
            new SyncProgress(reported.Add));
        var info = await MediaProbe.ProbeAsync(output);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.8, 2.3);
        Assert.Equal(1, reported[^1]);
    }

    [Fact]
    public async Task Audio_extract_has_the_requested_length()
    {
        if (_ffmpeg is null) return;
        var output = _files.Path_("extract.mp3");
        await AudioConverter.ConvertAsync(Clip(5), output, AudioFormat.Mp3,
            new AudioConversionOptions { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2.5) });
        var info = await MediaProbe.ProbeAsync(output);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.3, 1.7);
    }

    [Fact]
    public async Task Gif_converts_back_to_mp4_even_with_odd_size()
    {
        if (_ffmpeg is null) return;
        var gif = _files.Path_("odd.gif");
        Ffmpeg("-f", "lavfi", "-i", "testsrc=size=161x99:rate=10:duration=1", gif);
        var output = _files.Path_("odd.mp4");
        await VideoConverter.ConvertAsync(gif, output, VideoFormat.Mp4, new VideoConversionOptions { Quality = VideoQuality.Low });
        var info = await MediaProbe.ProbeAsync(output);
        Assert.Equal((160, 98), (info.Width, info.Height));
    }

    private void Ffmpeg(params string[] args)
    {
        var psi = new ProcessStartInfo(_ffmpeg!) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-hide_banner", "-y" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException(err);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) { lock (this) report(value); }
    }
}
