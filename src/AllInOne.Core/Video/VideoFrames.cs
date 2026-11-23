using System.Globalization;

namespace AllInOne.Core.Video;

/// <summary>Options d'extraction des images d'une vidéo.</summary>
public sealed record FrameExtractionOptions
{
    /// <summary>PNG (sans perte) ou JPEG (plus léger).</summary>
    public ImageFormatKind Format { get; init; } = ImageFormatKind.Png;

    /// <summary>Images par seconde à garder (1 = une image chaque seconde, 0,2 = une toutes les 5 s). Null = toutes les images.</summary>
    public double? FramesPerSecond { get; init; }

    /// <summary>Qualité JPEG de 1 à 100.</summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>Hauteur maximale en pixels (null = taille d'origine). Jamais d'agrandissement.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Début du passage (null = depuis le début).</summary>
    public TimeSpan? Start { get; init; }

    /// <summary>Fin du passage (null = jusqu'à la fin).</summary>
    public TimeSpan? End { get; init; }
}

/// <summary>Extraction des images d'une vidéo avec FFmpeg : toutes, ou à intervalle régulier, sur tout ou partie de la vidéo.</summary>
public static class VideoFrames
{
    public static IReadOnlyList<string> Arguments(string input, string outputPattern, FrameExtractionOptions options)
    {
        if (options.Format is not (ImageFormatKind.Png or ImageFormatKind.Jpeg))
            throw new ArgumentException("Les images d'une vidéo s'enregistrent en PNG ou en JPEG.");
        if (options.FramesPerSecond is <= 0) throw new ArgumentException("La fréquence doit être supérieure à zéro.");

        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        args.AddRange(Trim.BeforeInput(options.Start, options.End));
        args.AddRange(new[] { "-i", input, "-map", "0:v:0" });
        args.AddRange(Trim.AfterInput(options.Start, options.End));

        var filters = new List<string>();
        if (options.FramesPerSecond is { } fps) filters.Add("fps=" + fps.ToString("0.######", CultureInfo.InvariantCulture));
        if (options.MaxHeight is int h and > 0) filters.Add($"scale=-2:'min({h.ToString(CultureInfo.InvariantCulture)},ih)'");
        if (filters.Count > 0) args.AddRange(new[] { "-vf", string.Join(",", filters) });
        // Sans filtre de fréquence : chaque image de la vidéo, sans doublon ni image sautée.
        else args.AddRange(new[] { "-fps_mode", "passthrough" });

        if (options.Format == ImageFormatKind.Jpeg)
        {
            // Échelle FFmpeg : 2 (meilleure) à 31. On convertit la qualité 1-100 habituelle.
            var q = (int)Math.Round(2 + (100 - Math.Clamp(options.JpegQuality, 1, 100)) * 29 / 99.0);
            args.AddRange(new[] { "-q:v", q.ToString(CultureInfo.InvariantCulture) });
        }
        args.AddRange(new[] { "-an", "-sn", "-progress", "pipe:1", "-nostats", outputPattern });
        return args;
    }

    /// <summary>
    /// Écrit les images dans <paramref name="outputDirectory"/> (« nom_00001.png »…) et renvoie les fichiers créés.
    /// En cas d'erreur ou d'annulation, les images déjà écrites sont supprimées.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ExtractAsync(string inputPath, string outputDirectory, FrameExtractionOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default, string? ffmpegPath = null)
    {
        options ??= new FrameExtractionOptions();
        ffmpegPath ??= FfmpegLocator.Find() ?? throw new FfmpegNotFoundException();
        if (!File.Exists(inputPath)) throw new FileNotFoundException($"Fichier introuvable : {inputPath}", inputPath);

        Directory.CreateDirectory(outputDirectory);
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = options.Format.Extension();
        var pattern = Path.Combine(outputDirectory, name.Replace("%", "%%") + "_%05d" + extension);
        var before = new HashSet<string>(Directory.EnumerateFiles(outputDirectory), StringComparer.OrdinalIgnoreCase);
        try
        {
            await FfmpegRunner.RunAsync(ffmpegPath, Arguments(inputPath, pattern, options), inputPath, pattern, progress,
                cancellationToken, options.Start, options.End);
        }
        catch
        {
            foreach (var file in NewFiles()) TryDelete(file);
            throw;
        }
        var files = NewFiles();
        if (files.Count == 0)
            throw new VideoConversionException(Path.GetFileName(inputPath),
                "Aucune image extraite : le passage choisi est peut-être après la fin de la vidéo.", "");
        return files;

        List<string> NewFiles() => Directory.EnumerateFiles(outputDirectory, name + "_*" + extension)
            .Where(f => !before.Contains(f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Nombre approximatif d'images produites, pour prévenir avant d'en écrire des milliers.</summary>
    public static long EstimateCount(MediaInfo info, FrameExtractionOptions options)
    {
        if (info.Duration is not { } d) return 0;
        var seconds = Trim.EffectiveSeconds(d.TotalSeconds, options.Start, options.End);
        var rate = options.FramesPerSecond ?? info.FrameRate ?? 25;
        return (long)Math.Ceiling(seconds * rate);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
