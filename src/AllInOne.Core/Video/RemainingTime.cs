using System.Globalization;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Video;

/// <summary>Estimation du temps restant d'une tâche à partir de sa progression.</summary>
public static class RemainingTime
{
    /// <summary>
    /// Temps restant estimé, en supposant une vitesse constante. Null tant que la mesure n'est pas fiable
    /// (moins de 2 % ou moins de 2 secondes écoulées).
    /// </summary>
    public static TimeSpan? Estimate(double progress, TimeSpan elapsed)
    {
        if (progress < 0.02 || elapsed < TimeSpan.FromSeconds(2)) return null;
        if (progress >= 1) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - progress) / progress);
    }

    /// <summary>« ≈ 45 s », « ≈ 2 min 10 s », « ≈ 1 h 05 min ».</summary>
    public static string Format(TimeSpan remaining)
    {
        var seconds = (long)Math.Ceiling(remaining.TotalSeconds);
        if (seconds < 5) return L.T("quelques secondes");
        if (seconds < 60) return L.T("≈ {0} s", (seconds + 4) / 5 * 5);
        if (seconds < 600)
        {
            var rounded = (seconds + 9) / 10 * 10;
            var s = rounded % 60;
            return s == 0 ? L.T("≈ {0} min", rounded / 60) : L.T("≈ {0} min {1:00} s", rounded / 60, s);
        }
        if (seconds < 3600) return L.T("≈ {0} min", (seconds + 59) / 60);
        var minutes = (seconds + 59) / 60;
        return L.T("≈ {0} h {1} min", minutes / 60, (minutes % 60).ToString("00", CultureInfo.InvariantCulture));
    }
}
