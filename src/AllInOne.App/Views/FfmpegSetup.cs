using System.Windows;
using System.Windows.Controls;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

/// <summary>Affichage de l'état de FFmpeg et téléchargement à la demande (partagé par les écrans vidéo et audio).</summary>
internal static class FfmpegSetup
{
    public static void RefreshStatus(TextBlock status, Button downloadButton)
    {
        var path = FfmpegLocator.Find();
        if (path is not null)
        {
            status.Text = $"✓ FFmpeg prêt : {path}";
            downloadButton.Visibility = Visibility.Collapsed;
            return;
        }
        status.Text = FfmpegDownloader.IsSupportedPlatform
            ? "FFmpeg n'est pas encore installé. Il sera téléchargé à la première conversion (environ 200 Mo, une seule fois)."
            : "FFmpeg est introuvable : installez-le et ajoutez-le au PATH.";
        downloadButton.Visibility = FfmpegDownloader.IsSupportedPlatform ? Visibility.Visible : Visibility.Collapsed;
    }

    public static async Task<bool> DownloadAsync(DependencyObject view)
    {
        var window = MainWindow.Of(view);
        var ok = await window.RunBusyAsync("Téléchargement de FFmpeg (environ 200 Mo)…",
            (p, ct) => FfmpegDownloader.DownloadAsync(p, ct), "FFmpeg est installé.", cancellable: true);
        return ok && FfmpegLocator.Find() is not null;
    }

    /// <summary>Vérifie que FFmpeg est disponible, en proposant de le télécharger. Renvoie false si l'utilisateur renonce.</summary>
    public static async Task<bool> EnsureAsync(DependencyObject view)
    {
        if (FfmpegLocator.Find() is not null) return true;
        var owner = Window.GetWindow(view);
        if (!FfmpegDownloader.IsSupportedPlatform)
        {
            Dialog.Show(owner, "FFmpeg est introuvable. Installez-le puis réessayez.", "AllInOne",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var answer = Dialog.Show(owner,
            "La conversion utilise FFmpeg, un logiciel libre séparé.\n\nLe télécharger maintenant ? (environ 200 Mo, une seule fois)",
            "Installer FFmpeg", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        return answer == MessageBoxResult.Yes && await DownloadAsync(view);
    }
}
