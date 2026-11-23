using System.Globalization;

namespace AllInOne.Core.Video;

/// <summary>Construit la ligne de commande FFmpeg correspondant à un format et à des options.</summary>
public static class FfmpegArguments
{
    public static IReadOnlyList<string> Build(string input, string output, VideoFormat format, VideoConversionOptions options)
    {
        if (format == VideoFormat.Gif) return BuildGif(input, output, options);

        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        args.AddRange(Trim.BeforeInput(options.Start, options.End));
        args.AddRange(new[] { "-i", input, "-map", "0:v:0?" });
        args.AddRange(Trim.AfterInput(options.Start, options.End));
        if (!options.RemoveAudio) args.AddRange(new[] { "-map", "0:a:0?" });

        if (options.CopyStreams)
        {
            args.AddRange(new[] { "-c", "copy" });
        }
        else
        {
            args.AddRange(VideoCodec(format, options.Quality));
            // Réduit seulement si la vidéo est plus haute ; largeur et hauteur paires (exigées par H.264, VP9…).
            args.AddRange(new[]
            {
                "-vf", options.MaxHeight is int h and > 0
                    ? $"scale=-2:'trunc(min({h.ToString(CultureInfo.InvariantCulture)},ih)/2)*2'"
                    : "scale='trunc(iw/2)*2':'trunc(ih/2)*2'",
            });
            if (!options.RemoveAudio) args.AddRange(AudioCodec(format, options.Quality));
        }

        if (options.RemoveAudio) args.Add("-an");
        if (format is VideoFormat.Mp4 or VideoFormat.Mov) args.AddRange(new[] { "-movflags", "+faststart" });
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", output });
        return args;
    }

    /// <summary>
    /// GIF animé : images/s et largeur selon la qualité, palette calculée sur la vidéo (bien plus net que la palette
    /// par défaut), boucle infinie.
    /// </summary>
    private static IReadOnlyList<string> BuildGif(string input, string output, VideoConversionOptions options)
    {
        var (fps, width) = options.Quality switch
        {
            VideoQuality.High => (15, 640),
            VideoQuality.Low => (10, 320),
            _ => (12, 480),
        };
        var height = options.MaxHeight is int h and > 0 ? $"min({h.ToString(CultureInfo.InvariantCulture)},ih)" : "ih";
        var filter = $"fps={fps},scale='min({width},iw)':'{height}':force_original_aspect_ratio=decrease:flags=lanczos," +
                     "split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5";

        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        args.AddRange(Trim.BeforeInput(options.Start, options.End));
        args.AddRange(new[] { "-i", input, "-map", "0:v:0" });
        args.AddRange(Trim.AfterInput(options.Start, options.End));
        args.AddRange(new[] { "-vf", filter, "-an", "-loop", "0", "-progress", "pipe:1", "-nostats", output });
        return args;
    }

    private static IEnumerable<string> VideoCodec(VideoFormat format, VideoQuality q) => format switch
    {
        VideoFormat.Mp4 or VideoFormat.Mkv or VideoFormat.Mov => new[]
        {
            "-c:v", "libx264", "-preset", "medium", "-crf", Pick(q, "18", "23", "28"), "-pix_fmt", "yuv420p",
        },
        VideoFormat.Webm => new[]
        {
            "-c:v", "libvpx-vp9", "-crf", Pick(q, "24", "32", "40"), "-b:v", "0", "-row-mt", "1", "-deadline", "good",
            "-cpu-used", "2", "-pix_fmt", "yuv420p",
        },
        VideoFormat.Avi => new[] { "-c:v", "mpeg4", "-q:v", Pick(q, "2", "5", "10"), "-vtag", "xvid" },
        VideoFormat.Ogg => new[] { "-c:v", "libtheora", "-q:v", Pick(q, "9", "7", "5") },
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static IEnumerable<string> AudioCodec(VideoFormat format, VideoQuality q) => format switch
    {
        VideoFormat.Mp4 or VideoFormat.Mkv or VideoFormat.Mov => new[] { "-c:a", "aac", "-b:a", Pick(q, "256k", "192k", "128k") },
        VideoFormat.Webm => new[] { "-c:a", "libopus", "-b:a", Pick(q, "160k", "128k", "96k") },
        VideoFormat.Avi => new[] { "-c:a", "libmp3lame", "-b:a", Pick(q, "256k", "192k", "128k") },
        VideoFormat.Ogg => new[] { "-c:a", "libvorbis", "-q:a", Pick(q, "7", "5", "3") },
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static string Pick(VideoQuality q, string high, string medium, string low) => q switch
    {
        VideoQuality.High => high,
        VideoQuality.Low => low,
        _ => medium,
    };
}
