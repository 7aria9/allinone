using System.IO;
using System.Text;
using AllInOne.Core.Localization;

namespace AllInOne.App;

/// <summary>Journal des erreurs dans %LOCALAPPDATA%\AllInOne\logs (un fichier par jour).</summary>
public static class CrashLog
{
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllInOne", "logs");

    public static string CurrentFile => Path.Combine(Directory, $"allinone-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Écrit l'erreur complète (type, message, pile, exceptions internes). Ne lève jamais d'exception.</summary>
    public static string? Write(Exception exception, string context)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var sb = new StringBuilder()
                .AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {context} =====")
                .AppendLine($"Version : {typeof(CrashLog).Assembly.GetName().Version} · .NET {Environment.Version} · {Environment.OSVersion} · {(Environment.Is64BitProcess ? "x64" : "x86")}")
                .AppendLine($"Dossier : {AppContext.BaseDirectory}")
                .AppendLine(exception.ToString())
                .AppendLine();
            File.AppendAllText(CurrentFile, sb.ToString());
            return CurrentFile;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Message principal + celui de l'exception la plus interne (souvent la vraie cause pour les erreurs XAML).</summary>
    public static string Describe(Exception exception)
    {
        var root = exception;
        while (root.InnerException is not null) root = root.InnerException;
        return root == exception ? exception.Message : L.T("{0}\n\nCause : {1}", exception.Message, root.Message);
    }
}
