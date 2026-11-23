namespace AllInOne.Core.Video;

/// <summary>Formats audio de sortie.</summary>
public enum AudioFormat
{
    Mp3,
    Wav,
    Aiff,
    Flac,
    Ogg,
    Opus,
    M4a,
}

public static class AudioFormats
{
    /// <summary>Extensions audio reconnues en entrée (les vidéos sont aussi acceptées : on en extrait le son).</summary>
    public static readonly IReadOnlyList<string> InputExtensions = new[]
    {
        ".mp3", ".wav", ".aif", ".aiff", ".aifc", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".aac", ".wma", ".alac",
        ".ape", ".wv", ".ac3", ".amr", ".mka", ".caf", ".au", ".mp2",
    };

    public static bool IsSupportedAudio(string path) =>
        InputExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Fichier audio ou vidéo (dont on peut extraire le son).</summary>
    public static bool IsSupportedInput(string path) => IsSupportedAudio(path) || VideoFormats.IsSupportedVideo(path);

    public static string Extension(this AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => ".mp3",
        AudioFormat.Wav => ".wav",
        AudioFormat.Aiff => ".aiff",
        AudioFormat.Flac => ".flac",
        AudioFormat.Ogg => ".ogg",
        AudioFormat.Opus => ".opus",
        AudioFormat.M4a => ".m4a",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Formats sans perte : le réglage de qualité n'a pas d'effet.</summary>
    public static bool IsLossless(this AudioFormat format) => format is AudioFormat.Wav or AudioFormat.Aiff or AudioFormat.Flac;

    public static AudioFormat Parse(string value) => value.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "mp3" => AudioFormat.Mp3,
        "wav" or "wave" => AudioFormat.Wav,
        "aif" or "aiff" => AudioFormat.Aiff,
        "flac" => AudioFormat.Flac,
        "ogg" or "oga" or "vorbis" => AudioFormat.Ogg,
        "opus" => AudioFormat.Opus,
        "m4a" or "aac" => AudioFormat.M4a,
        _ => throw new ArgumentException($"Format audio inconnu : {value}"),
    };
}

/// <summary>Options de conversion audio.</summary>
public sealed record AudioConversionOptions
{
    /// <summary>Qualité des formats avec perte (MP3, OGG, Opus, M4A).</summary>
    public VideoQuality Quality { get; init; } = VideoQuality.High;

    /// <summary>Fréquence d'échantillonnage en Hz (44100, 48000…). Null = celle d'origine.</summary>
    public int? SampleRate { get; init; }

    /// <summary>Nombre de canaux (1 = mono, 2 = stéréo). Null = comme l'original.</summary>
    public int? Channels { get; init; }

    /// <summary>Début de l'extrait à garder (null = depuis le début).</summary>
    public TimeSpan? Start { get; init; }

    /// <summary>Fin de l'extrait à garder (null = jusqu'à la fin).</summary>
    public TimeSpan? End { get; init; }
}

/// <summary>Construit la ligne de commande FFmpeg pour une conversion audio.</summary>
public static class FfmpegAudioArguments
{
    public static IReadOnlyList<string> Build(string input, string output, AudioFormat format, AudioConversionOptions options)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        args.AddRange(Trim.BeforeInput(options.Start, options.End));
        args.AddRange(new[] { "-i", input, "-map", "0:a:0", "-vn", "-sn", "-dn" });
        args.AddRange(Trim.AfterInput(options.Start, options.End));
        if (options.SampleRate is int rate and > 0) args.AddRange(new[] { "-ar", rate.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        if (options.Channels is int ch and > 0) args.AddRange(new[] { "-ac", ch.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        var q = options.Quality;
        string Pick(string high, string medium, string low) => q switch
        {
            VideoQuality.High => high,
            VideoQuality.Low => low,
            _ => medium,
        };

        args.AddRange(format switch
        {
            // VBR LAME : V0 ≈ 245 kb/s, V2 ≈ 190 kb/s, V5 ≈ 130 kb/s.
            AudioFormat.Mp3 => new[] { "-c:a", "libmp3lame", "-q:a", Pick("0", "2", "5") },
            AudioFormat.Wav => new[] { "-c:a", "pcm_s16le" },
            AudioFormat.Aiff => new[] { "-c:a", "pcm_s16be" },
            AudioFormat.Flac => new[] { "-c:a", "flac", "-compression_level", "5" },
            AudioFormat.Ogg => new[] { "-c:a", "libvorbis", "-q:a", Pick("8", "6", "4") },
            AudioFormat.Opus => new[] { "-c:a", "libopus", "-b:a", Pick("192k", "128k", "96k") },
            AudioFormat.M4a => new[] { "-c:a", "aac", "-b:a", Pick("256k", "192k", "128k"), "-movflags", "+faststart" },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        });

        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", output });
        return args;
    }
}

/// <summary>Conversion de fichiers audio (et extraction du son des vidéos) avec FFmpeg.</summary>
public static class AudioConverter
{
    public static async Task ConvertAsync(string inputPath, string outputPath, AudioFormat format, AudioConversionOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default, string? ffmpegPath = null)
    {
        options ??= new AudioConversionOptions();
        ffmpegPath ??= FfmpegLocator.Find() ?? throw new FfmpegNotFoundException();
        if (!File.Exists(inputPath)) throw new FileNotFoundException($"Fichier introuvable : {inputPath}", inputPath);
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le fichier de sortie doit être différent du fichier d'origine.");

        var args = FfmpegAudioArguments.Build(inputPath, outputPath, format, options);
        await FfmpegRunner.RunAsync(ffmpegPath, args, inputPath, outputPath, progress, cancellationToken, options.Start, options.End);
    }

    public static async Task<IReadOnlyList<string>> ConvertManyAsync(IReadOnlyList<string> inputs, string outputDirectory,
        AudioFormat format, AudioConversionOptions? options = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default, IProgress<string>? status = null)
    {
        var ffmpeg = FfmpegLocator.Find() ?? throw new FfmpegNotFoundException();
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var index = i;
            status?.Report($"Conversion {i + 1}/{inputs.Count} : {Path.GetFileName(inputs[i])}…");
            var target = FileNames.Unique(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputs[i]) + format.Extension()));
            var fileProgress = new VideoConverter.SyncProgress(v => progress?.Report((index + v) / inputs.Count));
            await ConvertAsync(inputs[i], target, format, options, fileProgress, cancellationToken, ffmpeg);
            written.Add(target);
        }
        return written;
    }
}
