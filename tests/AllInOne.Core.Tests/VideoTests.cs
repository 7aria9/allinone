using System.Diagnostics;
using AllInOne.Core.Video;

namespace AllInOne.Core.Tests;

public class FfmpegArgumentsTests
{
    [Fact]
    public void Mp4_uses_h264_aac_and_faststart()
    {
        var args = FfmpegArguments.Build("in.avi", "out.mp4", VideoFormat.Mp4, new VideoConversionOptions { Quality = VideoQuality.High });
        var line = string.Join(' ', args);
        Assert.Contains("-c:v libx264", line);
        Assert.Contains("-crf 18", line);
        Assert.Contains("-c:a aac", line);
        Assert.Contains("-movflags +faststart", line);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Copy_mode_does_not_reencode()
    {
        var line = string.Join(' ', FfmpegArguments.Build("a.mp4", "b.mkv", VideoFormat.Mkv,
            new VideoConversionOptions { CopyStreams = true, MaxHeight = 720 }));
        Assert.Contains("-c copy", line);
        Assert.DoesNotContain("libx264", line);
        Assert.DoesNotContain("scale", line);
    }

    [Fact]
    public void Max_height_only_downscales_and_remove_audio_drops_audio()
    {
        var line = string.Join(' ', FfmpegArguments.Build("a.mp4", "b.webm", VideoFormat.Webm,
            new VideoConversionOptions { MaxHeight = 480, RemoveAudio = true }));
        Assert.Contains("scale=-2:'trunc(min(480,ih)/2)*2'", line);
        Assert.Contains("-an", line);
        Assert.DoesNotContain("libopus", line);
        Assert.DoesNotContain("0:a:0", line);
    }

    [Theory]
    [InlineData("MP4", VideoFormat.Mp4)]
    [InlineData(".ogv", VideoFormat.Ogg)]
    [InlineData("webm", VideoFormat.Webm)]
    public void Parse_formats(string text, VideoFormat expected) => Assert.Equal(expected, VideoFormats.Parse(text));
}

/// <summary>Conversions réelles : ces tests ont besoin de FFmpeg (installé dans la CI Linux) et ne font rien sans lui.</summary>
public class VideoConverterTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly string? _ffmpeg = FfmpegLocator.Find();
    public void Dispose() => _files.Dispose();

    private string MakeSample(string name = "sample.mkv", int seconds = 2)
    {
        var path = _files.Path_(name);
        Run(_ffmpeg!, "-hide_banner", "-y", "-f", "lavfi", "-i", $"testsrc=size=320x240:rate=25:duration={seconds}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}", "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-shortest", path);
        return path;
    }

    [Theory]
    [InlineData(VideoFormat.Mp4)]
    [InlineData(VideoFormat.Mkv)]
    [InlineData(VideoFormat.Webm)]
    [InlineData(VideoFormat.Avi)]
    [InlineData(VideoFormat.Ogg)]
    [InlineData(VideoFormat.Mov)]
    public async Task Converts_to_every_format_with_progress(VideoFormat format)
    {
        if (_ffmpeg is null) return;
        var input = MakeSample();
        var output = _files.Path_("out" + format.Extension());
        var reported = new List<double>();

        await VideoConverter.ConvertAsync(input, output, format,
            new VideoConversionOptions { Quality = VideoQuality.Low, MaxHeight = 120 }, new SyncProgress(reported.Add));

        Assert.True(new FileInfo(output).Length > 1000);
        Assert.Equal(1, reported[^1]);
        var info = Probe(output);
        Assert.Contains("Video:", info);
        Assert.Contains("x120", info);
        Assert.Contains("Audio:", info);
    }

    [Fact]
    public async Task Copy_mode_remuxes_mkv_to_mp4()
    {
        if (_ffmpeg is null) return;
        var output = _files.Path_("copy.mp4");
        await VideoConverter.ConvertAsync(MakeSample(), output, VideoFormat.Mp4, new VideoConversionOptions { CopyStreams = true });
        Assert.Contains("h264", Probe(output));
    }

    [Fact]
    public async Task Failure_reports_ffmpeg_message_and_leaves_no_file()
    {
        if (_ffmpeg is null) return;
        var bogus = _files.Path_("notavideo.mp4");
        await File.WriteAllTextAsync(bogus, "ceci n'est pas une vidéo");
        var output = _files.Path_("out.webm");

        var ex = await Assert.ThrowsAsync<VideoConversionException>(() =>
            VideoConverter.ConvertAsync(bogus, output, VideoFormat.Webm));

        Assert.Contains("notavideo.mp4", ex.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Cancellation_stops_ffmpeg_and_deletes_partial_output()
    {
        if (_ffmpeg is null) return;
        var input = MakeSample(seconds: 30);
        var output = _files.Path_("cancelled.webm");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VideoConverter.ConvertAsync(input, output, VideoFormat.Webm,
                new VideoConversionOptions { Quality = VideoQuality.High }, cancellationToken: cts.Token));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ConvertMany_writes_one_file_per_input()
    {
        if (_ffmpeg is null) return;
        var inputs = new[] { MakeSample("a.mkv", 1), MakeSample("b.mkv", 1) };
        var files = await VideoConverter.ConvertManyAsync(inputs, _files.Path_("out"), VideoFormat.Avi,
            new VideoConversionOptions { Quality = VideoQuality.Low });
        Assert.Equal(new[] { "a.avi", "b.avi" }, files.Select(Path.GetFileName));
    }

    private string Probe(string path) => Run(_ffmpeg!, "-hide_banner", "-i", path, true);

    private static string Run(string exe, params object[] args)
    {
        var allowFailure = args.Length > 0 && args[^1] is bool b && b;
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args.OfType<string>()) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        _ = stdoutTask.Result;
        if (p.ExitCode != 0 && !allowFailure) throw new InvalidOperationException(err);
        return err;
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) { lock (this) report(value); }
    }
}
