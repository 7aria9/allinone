using System.Globalization;

namespace AllInOne.Core.Video;

/// <summary>Extrait d'un son ou d'une vidéo : arguments FFmpeg et saisie des temps (« 1:30 », « 0:01:30,5 », « 90 »).</summary>
public static class Trim
{
    /// <summary>Vérifie que l'extrait a un sens (début positif, fin après le début).</summary>
    public static void Validate(TimeSpan? start, TimeSpan? end)
    {
        if (start is { } s && s < TimeSpan.Zero) throw new ArgumentException("Le début de l'extrait doit être positif.");
        if (end is { } e && e <= (start ?? TimeSpan.Zero))
            throw new ArgumentException("La fin de l'extrait doit être après son début.");
    }

    /// <summary>« -ss » placé avant « -i » : FFmpeg saute directement au début de l'extrait (rapide).</summary>
    internal static IEnumerable<string> BeforeInput(TimeSpan? start, TimeSpan? end)
    {
        Validate(start, end);
        return start is { } s && s > TimeSpan.Zero ? new[] { "-ss", Seconds(s) } : Array.Empty<string>();
    }

    /// <summary>« -t » : durée de l'extrait à écrire.</summary>
    internal static IEnumerable<string> AfterInput(TimeSpan? start, TimeSpan? end) =>
        end is { } e ? new[] { "-t", Seconds(e - (start ?? TimeSpan.Zero)) } : Array.Empty<string>();

    /// <summary>Durée réellement convertie, pour la barre de progression.</summary>
    public static double EffectiveSeconds(double fullSeconds, TimeSpan? start, TimeSpan? end)
    {
        var stop = end is { } e ? Math.Min(e.TotalSeconds, fullSeconds) : fullSeconds;
        var from = start?.TotalSeconds ?? 0;
        var length = stop - from;
        return length > 0 ? length : fullSeconds;
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Lit un temps saisi : « 90 » (secondes), « 1:30 », « 1:02:03 », avec décimales après un point ou une virgule.
    /// Une chaîne vide donne null.
    /// </summary>
    public static bool TryParseTime(string? text, out TimeSpan? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3) return false;
        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast = i == parts.Length - 1;
            if (!double.TryParse(parts[i], isLast ? NumberStyles.AllowDecimalPoint : NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;
            if (i > 0 && n >= 60) return false;
            total = total * 60 + n;
        }
        value = TimeSpan.FromSeconds(total);
        return true;
    }

    public static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
