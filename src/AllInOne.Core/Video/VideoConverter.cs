using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AllInOne.Core.Video;

/// <summary>Conversion de vidéos avec FFmpeg (processus externe).</summary>
public static partial class VideoConverter
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

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var psi = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in FfmpegArguments.Build(inputPath, outputPath, format, options)) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var errorLog = new StringBuilder();
        double? duration = null;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (errorLog)
            {
                if (errorLog.Length < 200_000) errorLog.AppendLine(e.Data);
            }
            if (duration is null && DurationRegex().Match(e.Data) is { Success: true } m)
                duration = TimeSpan.ParseExact(m.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture).TotalSeconds;
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null || duration is not > 0) return;
            if (e.Data.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(e.Data.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
            {
                progress?.Report(Math.Clamp(us / 1_000_000.0 / duration.Value, 0, 0.99));
            }
        };

        if (!process.Start()) throw new InvalidOperationException("Impossible de lancer FFmpeg.");
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            TryDelete(outputPath);
            throw;
        }

        if (process.ExitCode != 0)
        {
            TryDelete(outputPath);
            string log;
            lock (errorLog) log = errorLog.ToString();
            throw new VideoConversionException(Path.GetFileName(inputPath), Summarize(log), log);
        }
        progress?.Report(1);
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

    /// <summary>Dernières lignes utiles du journal FFmpeg, pour un message d'erreur lisible.</summary>
    internal static string Summarize(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("frame=", StringComparison.Ordinal))
            .TakeLast(4);
        return string.Join(Environment.NewLine, lines);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [GeneratedRegex(@"Duration:\s*(\d{2}:\d{2}:\d{2}\.\d{2})")]
    private static partial Regex DurationRegex();

    /// <summary>IProgress qui appelle directement le délégué (pas de passage par le contexte de synchronisation).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
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
