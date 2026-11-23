using System.IO.Compression;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Video;

/// <summary>
/// Télécharge FFmpeg pour Windows (build GPL de BtbN/FFmpeg-Builds, environ 200 Mo) dans
/// <see cref="FfmpegLocator.ManagedDirectory"/>. FFmpeg reste un programme séparé, lancé comme processus externe.
/// </summary>
public static class FfmpegDownloader
{
    public const string WindowsDownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static bool IsSupportedPlatform => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;

    /// <summary>Comment installer FFmpeg là où AllInOne ne le télécharge pas lui-même (macOS, Linux).</summary>
    public static string InstallHint =>
        OperatingSystem.IsMacOS()
            ? L.T("Installe-le avec Homebrew (https://brew.sh) : ouvre le Terminal et tape « brew install ffmpeg », puis reviens ici.")
            : OperatingSystem.IsLinux()
                ? L.T("Installe-le avec le gestionnaire de paquets, par exemple « sudo apt install ffmpeg » (Debian, Ubuntu, Mint), « sudo dnf install ffmpeg » (Fedora) ou « sudo pacman -S ffmpeg » (Arch), puis reviens ici.")
                : L.T("Installe-le puis ajoute-le au PATH.");

    /// <summary>Télécharge et installe FFmpeg. Renvoie le chemin de ffmpeg.exe.</summary>
    public static async Task<string> DownloadAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        string url = WindowsDownloadUrl, string? targetDirectory = null)
    {
        targetDirectory ??= FfmpegLocator.ManagedDirectory;
        Directory.CreateDirectory(targetDirectory);
        var zipPath = Path.Combine(targetDirectory, "ffmpeg-download.zip");

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AllInOne");
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(zipPath);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                    read += n;
                    if (total > 0) progress?.Report(read / (double)total.Value * 0.95);
                }
            }

            var exeName = FfmpegLocator.ExecutableName;
            string? installed = null;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    // On ne garde que les exécutables du dossier bin/ (ffmpeg et ffprobe) et la licence.
                    var name = entry.Name;
                    var inBin = entry.FullName.Replace('\\', '/').Contains("/bin/");
                    var isLicense = name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(name) || !(inBin || isLicense)) continue;
                    var destination = Path.Combine(targetDirectory, name);
                    entry.ExtractToFile(destination, overwrite: true);
                    if (name.Equals(exeName, StringComparison.OrdinalIgnoreCase)) installed = destination;
                }
            }

            progress?.Report(1);
            return installed ?? throw new InvalidOperationException(L.T("L'archive téléchargée ne contient pas ffmpeg."));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }
}
