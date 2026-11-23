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

    /// <summary>
    /// ffprobe livré avec FFmpeg : cherché à côté de <paramref name="ffmpegPath"/>, puis dans le PATH.
    /// Null s'il est absent (on se rabat alors sur « ffmpeg -i »).
    /// </summary>
    public static string? FindProbe(string? ffmpegPath = null)
    {
        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        ffmpegPath ??= Find();
        var dir = ffmpegPath is null ? null : Path.GetDirectoryName(ffmpegPath);
        if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
        return SearchPath(name);
    }

    public static string? Find()
    {
        var forced = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(forced) && File.Exists(forced)) return forced;

        foreach (var dir in new[] { AppContext.BaseDirectory, ManagedDirectory })
        {
            var candidate = Path.Combine(dir, ExecutableName);
            if (File.Exists(candidate)) return candidate;
        }

        return SearchPath(ExecutableName);
    }

    /// <summary>
    /// Dossiers d'installation habituels sur macOS et Linux (Homebrew, MacPorts, paquets, Snap) : une application lancée
    /// depuis le Finder ou un menu ne reçoit pas le PATH du terminal.
    /// </summary>
    private static readonly string[] UnixDirectories =
        { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", "/usr/bin", "/snap/bin", "/var/lib/flatpak/exports/bin" };

    private static string? SearchPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).AsEnumerable();
        if (!OperatingSystem.IsWindows()) dirs = dirs.Concat(UnixDirectories);
        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
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
