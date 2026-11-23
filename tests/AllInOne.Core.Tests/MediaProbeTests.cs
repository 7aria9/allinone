using System.Diagnostics;
using AllInOne.Core.Video;

namespace AllInOne.Core.Tests;

public class MediaInfoFormattingTests
{
    [Fact]
    public void Video_summary_lists_duration_resolution_codecs_and_size()
    {
        var info = new MediaInfo
        {
            Duration = TimeSpan.FromSeconds(205.4), Width = 1920, Height = 1080, VideoCodec = "h264", FrameRate = 29.97,
            BitRate = 5_200_000, AudioCodec = "aac", SampleRate = 48000, Channels = 2, SizeBytes = 13_000_000,
        };
        Assert.Equal("3:25 · 1920×1080 · H.264 · 29,97 i/s · 5,2 Mb/s · AAC 48 kHz stéréo · 12,4 Mo", info.VideoSummary());
    }

    [Fact]
    public void Audio_summary_lists_codec_bitrate_rate_and_channels()
    {
        var info = new MediaInfo
        {
            Duration = TimeSpan.FromSeconds(3725), AudioCodec = "mp3", AudioBitRate = 320_000, SampleRate = 44100, Channels = 1,
            SizeBytes = 900,
        };
        Assert.Equal("1:02:05 · MP3 · 320 kb/s · 44,1 kHz · mono · 900 o", info.AudioSummary());
    }

    [Theory]
    [InlineData("pcm_s16le", "PCM 16 bits")]
    [InlineData("hevc", "H.265")]
    [InlineData("vorbis", "Vorbis")]
    [InlineData("something", "SOMETHING")]
    public void Codec_names_are_readable(string codec, string expected) => Assert.Equal(expected, MediaInfo.CodecName(codec));

    [Fact]
    public void Ffmpeg_log_is_parsed_when_ffprobe_is_missing()
    {
        const string log = """
            Input #0, mp3, from 'song.mp3':
              Duration: 00:03:25.07, start: 0.025057, bitrate: 320 kb/s
              Stream #0:0: Audio: mp3 (mp3float), 44100 Hz, stereo, fltp, 320 kb/s
              Stream #0:1: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 500x500, 90k tbr, 90k tbn (attached pic)
            """;
        var info = MediaProbe.ParseFfmpegLog(log, 1000);
        Assert.Equal(205, (int)info.Duration!.Value.TotalSeconds);
        Assert.Equal("mp3", info.AudioCodec);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(320_000, info.AudioBitRate);
        Assert.False(info.HasVideo); // la pochette n'est pas une vidéo
    }

    [Fact]
    public void Ffmpeg_log_video_stream_gives_resolution_and_fps()
    {
        const string log = """
              Duration: 00:00:05.00, start: 0.000000, bitrate: 1205 kb/s
              Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1280x720 [SAR 1:1 DAR 16:9], 1000 kb/s, 25 fps, 25 tbr, 12800 tbn (default)
              Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, 5.1, fltp, 128 kb/s (default)
            """;
        var info = MediaProbe.ParseFfmpegLog(log, null);
        Assert.Equal((1280, 720), (info.Width, info.Height));
        Assert.Equal(25, info.FrameRate);
        Assert.Equal(6, info.Channels);
        Assert.Equal(1_205_000, info.BitRate);
    }
}

public class RemainingTimeTests
{
    [Fact]
    public void Estimate_waits_for_reliable_progress()
    {
        Assert.Null(RemainingTime.Estimate(0.01, TimeSpan.FromSeconds(10)));
        Assert.Null(RemainingTime.Estimate(0.5, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(30), RemainingTime.Estimate(0.25, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(3, "quelques secondes")]
    [InlineData(42, "≈ 45 s")]
    [InlineData(125, "≈ 2 min 10 s")]
    [InlineData(120, "≈ 2 min")]
    [InlineData(1500, "≈ 25 min")]
    [InlineData(3900, "≈ 1 h 05 min")]
    public void Format_rounds_to_a_readable_value(int seconds, string expected) =>
        Assert.Equal(expected, RemainingTime.Format(TimeSpan.FromSeconds(seconds)));
}

public sealed class MediaProbeTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly string? _ffmpeg = FfmpegLocator.Find();
    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task Probes_a_real_video()
    {
        if (_ffmpeg is null) return;
        var path = _files.Path_("clip.mp4");
        Ffmpeg("-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2", "-f", "lavfi", "-i", "sine=duration=2:sample_rate=48000",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-ac", "2", "-shortest", path);

        var info = await MediaProbe.ProbeAsync(path);

        Assert.Equal((320, 240), (info.Width, info.Height));
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal(25, info.FrameRate!.Value, 1);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.9, 2.2);
        Assert.Equal(new FileInfo(path).Length, info.SizeBytes);
    }

    [Fact]
    public async Task Probes_a_real_sound()
    {
        if (_ffmpeg is null) return;
        var path = _files.Path_("tone.wav");
        Ffmpeg("-f", "lavfi", "-i", "sine=duration=1:sample_rate=22050", "-ac", "1", path);

        var info = await MediaProbe.ProbeAsync(path);

        Assert.False(info.HasVideo);
        Assert.Equal("pcm_s16le", info.AudioCodec);
        Assert.Equal(22050, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.StartsWith("0:01 · PCM 16 bits", info.AudioSummary());
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
}
