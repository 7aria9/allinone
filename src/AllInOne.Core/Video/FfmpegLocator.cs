namespace AllInOne.Core.Video;

/// <summary>
/// Trouve l'exécutable FFmpeg. FFmpeg n'est pas inclus dans l'application (taille et licence) : il est cherché
/// à côté du programme, puis dans le dossier où AllInOne le télécharge, puis dans le PATH.
/// </summary>
public static class FfmpegLocator
{
    public static string ExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Dossier où <see cref="FfmpegDownloader"/> installe FFmpeg (%LOCALAPPDATA%\AllInOne\ffmpeg).</summary>
    public static string ManagedDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllInOne", "ffmpeg");

    /// <summary>Chemin forcé (variable d'environnement ALLINONE_FFMPEG), utile pour les tests ou une installation personnalisée.</summary>
    public const string EnvironmentVariable = "ALLINONE_FFMPEG";

    public static string? Find()
    {
        var forced = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(forced) && File.Exists(forced)) return forced;

        foreach (var dir in new[] { AppContext.BaseDirectory, ManagedDirectory })
        {
            var candidate = Path.Combine(dir, ExecutableName);
            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), ExecutableName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Entrée de PATH invalide : ignorée.
            }
        }
        return null;
    }
}
