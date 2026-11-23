using System.Diagnostics;
using AllInOne.Core.Video;

namespace AllInOne.Core.Tests;

public class FfmpegAudioArgumentsTests
{
    [Fact]
    public void Mp3_uses_lame_vbr_and_options()
    {
        var line = string.Join(' ', FfmpegAudioArguments.Build("in.wav", "out.mp3", AudioFormat.Mp3,
            new AudioConversionOptions { Quality = VideoQuality.Medium, SampleRate = 44100, Channels = 1 }));
        Assert.Contains("-c:a libmp3lame -q:a 2", line);
        Assert.Contains("-ar 44100", line);
        Assert.Contains("-ac 1", line);
        Assert.Contains("-vn", line);
    }

    [Theory]
    [InlineData("aif", AudioFormat.Aiff)]
    [InlineData(".FLAC", AudioFormat.Flac)]
    [InlineData("aac", AudioFormat.M4a)]
    public void Parse_formats(string text, AudioFormat expected) => Assert.Equal(expected, AudioFormats.Parse(text));

    [Fact]
    public void Video_files_are_accepted_as_audio_sources()
    {
        Assert.True(AudioFormats.IsSupportedInput("clip.mkv"));
        Assert.True(AudioFormats.IsSupportedInput("son.AIFF"));
        Assert.False(AudioFormats.IsSupportedInput("image.png"));
    }
}

/// <summary>Conversions réelles : ont besoin de FFmpeg (installé dans la CI Linux), ne font rien sans lui.</summary>
public class AudioConverterTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly string? _ffmpeg = FfmpegLocator.Find();
    public void Dispose() => _files.Dispose();

    private string Run(params string[] args)
    {
        var psi = new ProcessStartInfo(_ffmpeg!) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        _ = outTask.Result;
        return err;
    }

    private string MakeWav(string name = "tone.wav", int seconds = 2)
    {
        var path = _files.Path_(name);
        Run("-hide_banner", "-y", "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}:sample_rate=44100", "-ac", "2", path);
        return path;
    }

    [Theory]
    [InlineData(AudioFormat.Mp3, "mp3")]
    [InlineData(AudioFormat.Wav, "pcm_s16le")]
    [InlineData(AudioFormat.Aiff, "pcm_s16be")]
    [InlineData(AudioFormat.Flac, "flac")]
    [InlineData(AudioFormat.Ogg, "vorbis")]
    [InlineData(AudioFormat.Opus, "opus")]
    [InlineData(AudioFormat.M4a, "aac")]
    public async Task Converts_to_every_format(AudioFormat format, string codec)
    {
        if (_ffmpeg is null) return;
        var output = _files.Path_("out" + format.Extension());
        var reported = new List<double>();

        await AudioConverter.ConvertAsync(MakeWav(), output, format, new AudioConversionOptions { Channels = 1 },
            new LockedProgress(reported.Add));

        var info = Run("-hide_banner", "-i", output);
        Assert.Contains("Audio: " + codec, info);
        Assert.Contains("mono", info);
        Assert.Equal(1, reported[^1]);
    }

    [Fact]
    public async Task Extracts_audio_from_video()
    {
        if (_ffmpeg is null) return;
        var video = _files.Path_("clip.mp4");
        Run("-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc=size=160x120:duration=1", "-f", "lavfi", "-i", "sine=duration=1",
            "-shortest", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
        var output = _files.Path_("clip.flac");

        await AudioConverter.ConvertAsync(video, output, AudioFormat.Flac);

        var info = Run("-hide_banner", "-i", output);
        Assert.Contains("Audio: flac", info);
        Assert.DoesNotContain("Video:", info);
    }

    [Fact]
    public async Task ConvertMany_writes_one_file_per_input()
    {
        if (_ffmpeg is null) return;
        var inputs = new[] { MakeWav("a.wav", 1), MakeWav("b.wav", 1) };
        var files = await AudioConverter.ConvertManyAsync(inputs, _files.Path_("out"), AudioFormat.Mp3);
        Assert.Equal(new[] { "a.mp3", "b.mp3" }, files.Select(Path.GetFileName));
    }

    private sealed class LockedProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) { lock (this) report(value); }
    }
}
