namespace AllInOne.Core.Video;

/// <summary>Formats vidéo de sortie.</summary>
public enum VideoFormat
{
    /// <summary>MP4 : H.264 + AAC, le plus compatible (téléphones, navigateurs, TV).</summary>
    Mp4,
    /// <summary>MKV : H.264 + AAC dans un conteneur Matroska.</summary>
    Mkv,
    /// <summary>WebM : VP9 + Opus, idéal pour le web.</summary>
    Webm,
    /// <summary>AVI : MPEG-4 Part 2 + MP3, pour les anciens lecteurs.</summary>
    Avi,
    /// <summary>OGG : Theora + Vorbis, formats entièrement libres.</summary>
    Ogg,
    /// <summary>MOV : H.264 + AAC (QuickTime).</summary>
    Mov,
    /// <summary>GIF animé (sans son), palette optimisée pour chaque vidéo.</summary>
    Gif,
}

public enum VideoQuality
{
    High,
    Medium,
    Low,
}

public static class VideoFormats
{
    public static readonly IReadOnlyList<string> InputExtensions = new[]
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".ogg", ".ogv", ".mov", ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".mts",
        ".m2ts", ".3gp", ".vob", ".divx", ".asf", ".gif",
    };

    public static bool IsSupportedVideo(string path) =>
        InputExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string Extension(this VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => ".mp4",
        VideoFormat.Mkv => ".mkv",
        VideoFormat.Webm => ".webm",
        VideoFormat.Avi => ".avi",
        VideoFormat.Ogg => ".ogg",
        VideoFormat.Mov => ".mov",
        VideoFormat.Gif => ".gif",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static VideoFormat Parse(string value) => value.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "mp4" or "m4v" => VideoFormat.Mp4,
        "mkv" => VideoFormat.Mkv,
        "webm" => VideoFormat.Webm,
        "avi" => VideoFormat.Avi,
        "ogg" or "ogv" => VideoFormat.Ogg,
        "mov" => VideoFormat.Mov,
        "gif" => VideoFormat.Gif,
        _ => throw new ArgumentException($"Format vidéo inconnu : {value}"),
    };

    public static VideoQuality ParseQuality(string value) => value.Trim().ToLowerInvariant() switch
    {
        "high" or "haute" or "h" => VideoQuality.High,
        "medium" or "moyenne" or "m" => VideoQuality.Medium,
        "low" or "faible" or "basse" or "l" => VideoQuality.Low,
        _ => throw new ArgumentException($"Qualité inconnue : {value} (haute, moyenne ou faible)"),
    };
}

/// <summary>Options de conversion vidéo.</summary>
public sealed record VideoConversionOptions
{
    public VideoQuality Quality { get; init; } = VideoQuality.Medium;

    /// <summary>Hauteur maximale en pixels (ex. 720). Null = résolution d'origine. Jamais d'agrandissement.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Supprimer la piste audio.</summary>
    public bool RemoveAudio { get; init; }

    /// <summary>
    /// Copier les flux sans les réencoder : très rapide et sans perte, mais seulement si les codecs d'origine
    /// sont acceptés par le conteneur cible (ex. MP4 → MKV). Ignore la qualité et la résolution.
    /// </summary>
    public bool CopyStreams { get; init; }

    /// <summary>Début de l'extrait à garder (null = depuis le début).</summary>
    public TimeSpan? Start { get; init; }

    /// <summary>Fin de l'extrait à garder (null = jusqu'à la fin).</summary>
    public TimeSpan? End { get; init; }
}
