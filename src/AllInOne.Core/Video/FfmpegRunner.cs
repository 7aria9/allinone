using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AllInOne.Core.Video;

/// <summary>Lance FFmpeg, suit sa progression (-progress pipe:1) et gère erreurs et annulation.</summary>
internal static partial class FfmpegRunner
{
    public static async Task RunAsync(string ffmpegPath, IReadOnlyList<string> arguments, string inputPath, string outputPath,
        IProgress<double>? progress, CancellationToken cancellationToken, TimeSpan? trimStart = null, TimeSpan? trimEnd = null)
    {
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
        foreach (var a in arguments) psi.ArgumentList.Add(a);

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
            {
                var full = TimeSpan.ParseExact(m.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture).TotalSeconds;
                duration = Trim.EffectiveSeconds(full, trimStart, trimEnd);
            }
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

    /// <summary>Dernières lignes utiles du journal FFmpeg, pour un message d'erreur lisible.</summary>
    internal static string Summarize(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("frame=", StringComparison.Ordinal) && !l.StartsWith("size=", StringComparison.Ordinal))
            .TakeLast(4);
        return string.Join(Environment.NewLine, lines);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [GeneratedRegex(@"Duration:\s*(\d{2}:\d{2}:\d{2}\.\d{2})")]
    private static partial Regex DurationRegex();
}
