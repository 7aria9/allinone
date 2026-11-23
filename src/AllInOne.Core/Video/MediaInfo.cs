using System.Globalization;

namespace AllInOne.Core.Video;

/// <summary>Caractéristiques d'un fichier audio ou vidéo, lues avec ffprobe (ou ffmpeg à défaut).</summary>
public sealed class MediaInfo
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public long? SizeBytes { get; init; }
    public TimeSpan? Duration { get; init; }
    /// <summary>Débit global en bits par seconde.</summary>
    public long? BitRate { get; init; }

    public string? VideoCodec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }

    public string? AudioCodec { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    /// <summary>Débit de la piste audio en bits par seconde.</summary>
    public long? AudioBitRate { get; init; }

    public bool HasVideo => VideoCodec is not null;
    public bool HasAudio => AudioCodec is not null;

    /// <summary>Résumé sur une ligne, par ex. « 3:25 · 1920×1080 · H.264 · 30 i/s · AAC 48 kHz stéréo · 12,4 Mo ».</summary>
    public string VideoSummary()
    {
        var parts = new List<string>();
        if (Duration is { } d) parts.Add(FormatDuration(d));
        if (Width is { } w && Height is { } h) parts.Add($"{w}×{h}");
        if (VideoCodec is not null) parts.Add(CodecName(VideoCodec));
        if (FrameRate is > 0 and var fps) parts.Add($"{Math.Round(fps, 2).ToString("0.##", French)} i/s");
        if (BitRate is > 0 and var br) parts.Add(FormatBitRate(br));
        if (AudioCodec is not null)
        {
            var audio = CodecName(AudioCodec);
            if (SampleRate is > 0 and var sr) audio += " " + FormatSampleRate(sr);
            if (Channels is > 0 and var ch) audio += " " + ChannelName(ch);
            parts.Add(audio);
        }
        else if (HasVideo) parts.Add("sans son");
        if (SizeBytes is { } size) parts.Add(FormatSize(size));
        return string.Join(" · ", parts);
    }

    /// <summary>Résumé sur une ligne, par ex. « 3:25 · MP3 · 320 kb/s · 44,1 kHz · stéréo · 8,2 Mo ».</summary>
    public string AudioSummary()
    {
        var parts = new List<string>();
        if (Duration is { } d) parts.Add(FormatDuration(d));
        if (AudioCodec is not null) parts.Add(CodecName(AudioCodec));
        else parts.Add("aucune piste audio");
        if ((AudioBitRate ?? (HasVideo ? null : BitRate)) is > 0 and var br) parts.Add(FormatBitRate(br));
        if (SampleRate is > 0 and var sr) parts.Add(FormatSampleRate(sr));
        if (Channels is > 0 and var ch) parts.Add(ChannelName(ch));
        if (HasVideo) parts.Add("vidéo");
        if (SizeBytes is { } size) parts.Add(FormatSize(size));
        return string.Join(" · ", parts);
    }

    public static string FormatDuration(TimeSpan d)
    {
        var total = (long)Math.Round(d.TotalSeconds);
        var (h, m, s) = (total / 3600, total / 60 % 60, total % 60);
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(format, French)} {units[unit]}";
    }

    public static string FormatBitRate(long bitsPerSecond) =>
        bitsPerSecond >= 10_000_000
            ? $"{(bitsPerSecond / 1_000_000.0).ToString("0", French)} Mb/s"
            : bitsPerSecond >= 1_000_000
                ? $"{(bitsPerSecond / 1_000_000.0).ToString("0.#", French)} Mb/s"
                : $"{Math.Round(bitsPerSecond / 1000.0).ToString("0", French)} kb/s";

    public static string FormatSampleRate(int hertz) => $"{(hertz / 1000.0).ToString("0.###", French)} kHz";

    public static string ChannelName(int channels) => channels switch
    {
        1 => "mono",
        2 => "stéréo",
        6 => "5.1",
        8 => "7.1",
        _ => $"{channels} canaux",
    };

    /// <summary>Nom lisible d'un codec FFmpeg (h264 → H.264, pcm_s16le → PCM 16 bits…).</summary>
    public static string CodecName(string codec)
    {
        var c = codec.ToLowerInvariant();
        if (c.StartsWith("pcm_", StringComparison.Ordinal))
        {
            var bits = new string(c.Where(char.IsDigit).ToArray());
            return bits.Length > 0 ? $"PCM {bits} bits" : "PCM";
        }
        return c switch
        {
            "h264" => "H.264",
            "hevc" => "H.265",
            "av1" => "AV1",
            "vp8" => "VP8",
            "vp9" => "VP9",
            "mpeg4" => "MPEG-4",
            "mpeg2video" => "MPEG-2",
            "mpeg1video" => "MPEG-1",
            "theora" => "Theora",
            "prores" => "ProRes",
            "mjpeg" => "MJPEG",
            "wmv1" or "wmv2" or "wmv3" => "WMV",
            "msmpeg4v3" => "DivX 3",
            "aac" => "AAC",
            "mp3" or "mp3float" => "MP3",
            "mp2" => "MP2",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "flac" => "FLAC",
            "alac" => "ALAC",
            "ac3" => "AC-3",
            "eac3" => "E-AC-3",
            "dts" => "DTS",
            "truehd" => "TrueHD",
            "wmav1" or "wmav2" or "wmapro" => "WMA",
            _ => codec.ToUpperInvariant(),
        };
    }
}
