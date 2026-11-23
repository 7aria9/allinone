using System.Globalization;

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
        if (seconds < 5) return "quelques secondes";
        if (seconds < 60) return $"≈ {(seconds + 4) / 5 * 5} s";
        if (seconds < 600)
        {
            var rounded = (seconds + 9) / 10 * 10;
            var s = rounded % 60;
            return s == 0 ? $"≈ {rounded / 60} min" : $"≈ {rounded / 60} min {s:00} s";
        }
        if (seconds < 3600) return $"≈ {(seconds + 59) / 60} min";
        var minutes = (seconds + 59) / 60;
        return $"≈ {minutes / 60} h {(minutes % 60).ToString("00", CultureInfo.InvariantCulture)} min";
    }
}
