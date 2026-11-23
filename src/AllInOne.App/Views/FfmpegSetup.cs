using AllInOne.Core.Video;
using Avalonia;
using Avalonia.Controls;
using AllInOne.Core.Localization;

namespace AllInOne.App.Views;

/// <summary>
/// Affichage de l'état de FFmpeg (partagé par les écrans vidéo et audio). Sous Windows, LocalConvert le télécharge à la
/// demande ; sous macOS et Linux, il indique comment l'installer (Homebrew, gestionnaire de paquets).
/// </summary>
internal static class FfmpegSetup
{
    public static void RefreshStatus(TextBlock status, Button downloadButton)
    {
        var path = FfmpegLocator.Find();
        if (path is not null)
        {
            status.Text = L.T("✓ FFmpeg prêt : {0}", path);
            downloadButton.IsVisible = false;
            return;
        }
        status.Text = FfmpegDownloader.IsSupportedPlatform
            ? L.T("FFmpeg n'est pas encore installé. Il sera téléchargé à la première conversion (environ 200 Mo, une seule fois).")
            : L.T("FFmpeg n'est pas installé. ") + FfmpegDownloader.InstallHint;
        downloadButton.IsVisible = FfmpegDownloader.IsSupportedPlatform;
    }

    public static async Task<bool> DownloadAsync(Visual view)
    {
        var window = MainWindow.Of(view);
        var ok = await window.RunBusyAsync(L.T("Téléchargement de FFmpeg (environ 200 Mo)…"),
            (p, ct) => FfmpegDownloader.DownloadAsync(p, ct), L.T("FFmpeg est installé."), cancellable: true);
        return ok && FfmpegLocator.Find() is not null;
    }

    /// <summary>Vérifie que FFmpeg est disponible, en proposant de le télécharger (Windows). Renvoie false si l'utilisateur renonce.</summary>
    public static async Task<bool> EnsureAsync(Visual view)
    {
        if (FfmpegLocator.Find() is not null) return true;
        var owner = MainWindow.Of(view);
        if (!FfmpegDownloader.IsSupportedPlatform)
        {
            await Dialog.Show(owner, L.T("La conversion utilise FFmpeg, un logiciel libre séparé, qui n'est pas encore installé.\n\n") +
                                     FfmpegDownloader.InstallHint, L.T("Installer FFmpeg"), MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var answer = await Dialog.Show(owner,
            L.T("La conversion utilise FFmpeg, un logiciel libre séparé.\n\nLe télécharger maintenant ? (environ 200 Mo, une seule fois)"),
            L.T("Installer FFmpeg"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        return answer == MessageBoxResult.Yes && await DownloadAsync(view);
    }
}
