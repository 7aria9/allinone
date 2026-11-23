using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Video;

/// <summary>
/// Lit les caractéristiques d'un fichier audio ou vidéo. Utilise ffprobe (livré avec FFmpeg) s'il est présent,
/// sinon analyse la sortie de « ffmpeg -i ».
/// </summary>
public static partial class MediaProbe
{
    public static async Task<MediaInfo> ProbeAsync(string path, string? ffmpegPath = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(L.T("Fichier introuvable : {0}", path), path);
        var size = new FileInfo(path).Length;
        ffmpegPath ??= FfmpegLocator.Find() ?? throw new FfmpegNotFoundException();

        var ffprobe = FfmpegLocator.FindProbe(ffmpegPath);
        if (ffprobe is not null)
        {
            var (code, output, _) = await RunAsync(ffprobe,
                ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], cancellationToken);
            if (code == 0 && TryParseFfprobeJson(output, size) is { } info) return info;
        }

        // « ffmpeg -i » sans sortie se termine toujours en erreur, mais décrit le fichier sur stderr.
        var (_, _, log) = await RunAsync(ffmpegPath, ["-hide_banner", "-nostdin", "-i", path], cancellationToken);
        return ParseFfmpegLog(log, size);
    }

    internal static MediaInfo? TryParseFfprobeJson(string json, long? size)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement? video = null, audio = null;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var type = Str(s, "codec_type");
                    // Une pochette d'album (MP3, M4A) apparaît comme une piste vidéo : on l'ignore.
                    var cover = s.TryGetProperty("disposition", out var disp) && Num(disp, "attached_pic") == 1;
                    if (type == "video" && !cover && video is null) video = s;
                    else if (type == "audio" && audio is null) audio = s;
                }
            }
            root.TryGetProperty("format", out var format);
            var duration = format.ValueKind == JsonValueKind.Object ? Num(format, "duration") : null;
            duration ??= video is { } v0 ? Num(v0, "duration") : null;
            duration ??= audio is { } a0 ? Num(audio.Value, "duration") : null;

            return new MediaInfo
            {
                SizeBytes = size,
                Duration = duration is > 0 ? TimeSpan.FromSeconds(duration.Value) : null,
                BitRate = format.ValueKind == JsonValueKind.Object ? (long?)Num(format, "bit_rate") : null,
                VideoCodec = video is { } v ? Str(v, "codec_name") : null,
                Width = video is { } vw ? (int?)Num(vw, "width") : null,
                Height = video is { } vh ? (int?)Num(vh, "height") : null,
                FrameRate = video is { } vf ? ParseRate(Str(vf, "avg_frame_rate")) ?? ParseRate(Str(vf, "r_frame_rate")) : null,
                AudioCodec = audio is { } a ? Str(a, "codec_name") : null,
                SampleRate = audio is { } asr ? (int?)Num(asr, "sample_rate") : null,
                Channels = audio is { } ac ? (int?)Num(ac, "channels") : null,
                AudioBitRate = audio is { } ab ? (long?)Num(ab, "bit_rate") : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static MediaInfo ParseFfmpegLog(string log, long? size)
    {
        TimeSpan? duration = null;
        long? bitRate = null;
        if (DurationLine().Match(log) is { Success: true } d)
        {
            if (TimeSpan.TryParseExact(d.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var t)) duration = t;
            if (long.TryParse(d.Groups[2].Value, out var kb)) bitRate = kb * 1000;
        }

        string? videoCodec = null, audioCodec = null;
        int? width = null, height = null, sampleRate = null, channels = null;
        double? fps = null;
        long? audioBitRate = null;
        foreach (Match m in StreamLine().Matches(log))
        {
            var kind = m.Groups[1].Value;
            var details = m.Groups[2].Value;
            var codec = details.Split([' ', ','], 2)[0];
            if (kind == "Video" && videoCodec is null && !details.Contains("(attached pic)", StringComparison.Ordinal))
            {
                videoCodec = codec;
                if (Resolution().Match(details) is { Success: true } r)
                {
                    width = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                if (Fps().Match(details) is { Success: true } f &&
                    double.TryParse(f.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)) fps = rate;
            }
            else if (kind == "Audio" && audioCodec is null)
            {
                audioCodec = codec;
                if (Hertz().Match(details) is { Success: true } hz) sampleRate = int.Parse(hz.Groups[1].Value, CultureInfo.InvariantCulture);
                channels = ChannelCount(details);
                if (KiloBits().Match(details) is { Success: true } kb) audioBitRate = long.Parse(kb.Groups[1].Value, CultureInfo.InvariantCulture) * 1000;
            }
        }

        return new MediaInfo
        {
            SizeBytes = size,
            Duration = duration,
            BitRate = bitRate,
            VideoCodec = videoCodec,
            Width = width,
            Height = height,
            FrameRate = fps,
            AudioCodec = audioCodec,
            SampleRate = sampleRate,
            Channels = channels,
            AudioBitRate = audioBitRate,
        };
    }

    private static int? ChannelCount(string details)
    {
        var fields = details.Split(',', StringSplitOptions.TrimEntries);
        foreach (var f in fields)
        {
            switch (f)
            {
                case "mono": return 1;
                case "stereo": return 2;
                case "quad": return 4;
            }
            if (f.StartsWith("5.1", StringComparison.Ordinal)) return 6;
            if (f.StartsWith("7.1", StringComparison.Ordinal)) return 8;
            if (ChannelsField().Match(f) is { Success: true } c) return int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>ffprobe écrit certains nombres comme des chaînes (« 44100 », « 3.250000 ») : on accepte les deux.</summary>
    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static double? ParseRate(string? rate)
    {
        if (string.IsNullOrEmpty(rate)) return null;
        var parts = rate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && n > 0)
            return n / d;
        return null;
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de lancer {exe}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    [GeneratedRegex(@"Duration:\s*(\d{2}:\d{2}:\d{2}\.\d{2})(?:.*?bitrate:\s*(\d+)\s*kb/s)?")]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"Stream #\d+:\d+.*?: (Video|Audio): (.+)")]
    private static partial Regex StreamLine();

    [GeneratedRegex(@"\b(\d{2,5})x(\d{2,5})\b")]
    private static partial Regex Resolution();

    [GeneratedRegex(@"([\d.]+) fps")]
    private static partial Regex Fps();

    [GeneratedRegex(@"(\d+) Hz")]
    private static partial Regex Hertz();

    [GeneratedRegex(@"(\d+) kb/s")]
    private static partial Regex KiloBits();

    [GeneratedRegex(@"^(\d+) channels")]
    private static partial Regex ChannelsField();
}
