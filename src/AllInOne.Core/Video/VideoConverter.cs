namespace AllInOne.Core.Video;

/// <summary>Conversion de vidéos avec FFmpeg (processus externe).</summary>
public static class VideoConverter
{
    /// <summary>
    /// Convertit une vidéo. <paramref name="progress"/> reçoit une valeur entre 0 et 1.
    /// En cas d'annulation ou d'erreur, le fichier de sortie partiel est supprimé.
    /// </summary>
    public static async Task ConvertAsync(string inputPath, string outputPath, VideoFormat format, VideoConversionOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default, string? ffmpegPath = null)
    {
        options ??= new VideoConversionOptions();
        ffmpegPath ??= FfmpegLocator.Find() ?? throw new FfmpegNotFoundException();
        if (!File.Exists(inputPath)) throw new FileNotFoundException($"Fichier introuvable : {inputPath}", inputPath);
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le fichier de sortie doit être différent du fichier d'origine.");

        var args = FfmpegArguments.Build(inputPath, outputPath, format, options);
        await FfmpegRunner.RunAsync(ffmpegPath, args, inputPath, outputPath, progress, cancellationToken);
    }

    /// <summary>Convertit plusieurs vidéos dans un dossier ; renvoie les fichiers créés.</summary>
    public static async Task<IReadOnlyList<string>> ConvertManyAsync(IReadOnlyList<string> inputs, string outputDirectory,
        VideoFormat format, VideoConversionOptions? options = null, IProgress<double>? progress = null,
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
            var fileProgress = new SyncProgress(v => progress?.Report((index + v) / inputs.Count));
            await ConvertAsync(inputs[i], target, format, options, fileProgress, cancellationToken, ffmpeg);
            written.Add(target);
        }
        return written;
    }

    /// <summary>IProgress qui appelle directement le délégué (pas de passage par le contexte de synchronisation).</summary>
    internal sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

public sealed class FfmpegNotFoundException : InvalidOperationException
{
    public FfmpegNotFoundException()
        : base("FFmpeg est introuvable. Installez-le (ou laissez AllInOne le télécharger) puis réessayez.") { }
}

public sealed class VideoConversionException(string file, string summary, string fullLog)
    : InvalidOperationException($"La conversion de {file} a échoué :{Environment.NewLine}{summary}")
{
    public string FullLog { get; } = fullLog;
}
