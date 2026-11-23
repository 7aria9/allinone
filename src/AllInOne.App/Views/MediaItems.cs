using System.Diagnostics;
using System.IO;
using AllInOne.Core.Video;

namespace AllInOne.App.Views;

/// <summary>Écran listant des sons ou des vidéos (pour le test de démarrage).</summary>
internal interface IMediaListView
{
    void AddSmokeTestSamples();
}

public enum MediaState { Idle, Waiting, Running, Done, Failed, Cancelled }

/// <summary>Un son ou une vidéo de la liste : ses caractéristiques et l'avancement de sa conversion.</summary>
public sealed class MediaItem : Observable
{
    private string _info = "Lecture des informations…";
    private MediaState _state;
    private double _progress;
    private string _statusText = "";
    private string? _error;

    public MediaItem(string path, bool isVideo) : this(path, isVideo ? "🎬" : "🎵") => IsVideo = isVideo;

    /// <summary>Ligne d'un autre type de fichier (PDF…), avec son icône.</summary>
    public MediaItem(string path, string icon)
    {
        Path = path;
        Icon = icon;
    }

    public string Path { get; }
    public bool IsVideo { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Icon { get; }

    /// <summary>Caractéristiques lues avec FFmpeg, ou null si pas encore lues.</summary>
    public MediaInfo? Media { get; private set; }

    public string Info { get => _info; private set => Set(ref _info, value); }

    public MediaState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            foreach (var name in new[] { nameof(ShowStatus), nameof(ShowBar), nameof(IsRunning), nameof(IsDone), nameof(IsFailed),
                         nameof(StatusColumn), nameof(StatusColumnSpan), nameof(StatusMargin) })
                Raise(name);
        }
    }

    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string? Error { get => _error; set => Set(ref _error, value); }

    public bool ShowStatus => State != MediaState.Idle;
    public bool ShowBar => State is MediaState.Waiting or MediaState.Running;
    public bool IsRunning => State == MediaState.Running;
    public bool IsDone => State == MediaState.Done;
    public bool IsFailed => State == MediaState.Failed;

    // Sans barre (terminé, échec, arrêté), le texte d'état prend toute la largeur.
    public int StatusColumn => ShowBar ? 1 : 0;
    public int StatusColumnSpan => ShowBar ? 1 : 2;
    public Avalonia.Thickness StatusMargin => ShowBar ? new(10, 0, 0, 0) : default;

    public void SetMedia(MediaInfo media)
    {
        Media = media;
        Info = IsVideo ? media.VideoSummary() : media.AudioSummary();
    }

    public void SetInfoText(string text) => Info = text;

    public void Reset(MediaState state)
    {
        State = state;
        Progress = 0;
        Error = null;
        StatusText = state == MediaState.Waiting ? "En attente" : "";
    }
}

/// <summary>Lecture des caractéristiques (en arrière-plan) et conversion fichier par fichier, partagées par les écrans audio et vidéo.</summary>
internal static class MediaItems
{
    private static readonly SemaphoreSlim ProbeSlots = new(3);

    /// <summary>Lit les caractéristiques des fichiers qui ne les ont pas encore. Sans FFmpeg, affiche seulement la taille.</summary>
    public static void LoadInfo(IEnumerable<MediaItem> items)
    {
        var ffmpeg = FfmpegLocator.Find();
        foreach (var item in items.Where(i => i.Media?.Duration is null).ToList())
        {
            if (ffmpeg is null)
            {
                var size = TryGetSize(item.Path);
                item.SetInfoText((size is { } s ? MediaInfo.FormatSize(s) + " · " : "") + "détails disponibles une fois FFmpeg installé");
                continue;
            }
            _ = LoadOneAsync(item, ffmpeg);
        }
    }

    private static async Task LoadOneAsync(MediaItem item, string ffmpeg)
    {
        await ProbeSlots.WaitAsync();
        try
        {
            var info = await Task.Run(() => MediaProbe.ProbeAsync(item.Path, ffmpeg));
            item.SetMedia(info);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Lecture des informations de " + item.Path);
            item.SetInfoText("Informations illisibles");
        }
        finally
        {
            ProbeSlots.Release();
        }
    }

    /// <summary>Lit les champs Début / Fin d'un extrait. Affiche un message et renvoie false si la saisie est invalide.</summary>
    public static async Task<(bool Ok, TimeSpan? From, TimeSpan? To)> ReadTrimAsync(
        Avalonia.Controls.TextBox start, Avalonia.Controls.TextBox end, Avalonia.Controls.Window? owner)
    {
        if (!Trim.TryParseTime(start.Text, out var from) || !Trim.TryParseTime(end.Text, out var to))
        {
            await Dialog.Show(owner, "Temps d'extrait invalide. Écris par exemple 1:30, 0:01:30 ou 90 (secondes).", "AllInOne");
            return (false, null, null);
        }
        try
        {
            Trim.Validate(from, to);
            return (true, from, to);
        }
        catch (ArgumentException ex)
        {
            await Dialog.Show(owner, ex.Message, "AllInOne");
            return (false, null, null);
        }
    }

    /// <summary>Lignes d'exemple, une par état, sans lancer FFmpeg.</summary>
    public static IEnumerable<MediaItem> SmokeTestSamples(bool isVideo)
    {
        var path = Environment.ProcessPath ?? "exemple";
        var info = new MediaInfo
        {
            Duration = TimeSpan.FromSeconds(205), Width = 1920, Height = 1080, VideoCodec = "h264", FrameRate = 30,
            AudioCodec = "aac", SampleRate = 48000, Channels = 2, AudioBitRate = 128_000, SizeBytes = 13_000_000,
        };
        foreach (var state in Enum.GetValues<MediaState>())
        {
            var item = new MediaItem(path, isVideo);
            item.SetMedia(info);
            item.Reset(state);
            item.Progress = 0.42;
            item.StatusText = state.ToString();
            if (state == MediaState.Failed) item.Error = "Erreur d'exemple";
            yield return item;
        }
    }

    private static long? TryGetSize(string path)
    {
        try { return new FileInfo(path).Length; } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
    }

    public sealed record Result(IReadOnlyList<string> Written, IReadOnlyList<(MediaItem Item, string Message)> Failures);

    /// <summary>
    /// Convertit les fichiers un par un. Chaque ligne montre sa progression et son temps restant ; la barre globale et
    /// le texte d'état montrent l'ensemble, pondéré par la durée de chaque fichier. Une erreur sur un fichier
    /// n'arrête pas les suivants. Doit être appelé depuis le fil de l'interface.
    /// </summary>
    public static Task<Result> ConvertAsync(IReadOnlyList<MediaItem> items, string outputDirectory, string extension,
        Func<string, string, IProgress<double>, CancellationToken, Task> convert,
        IProgress<double> overall, Action<string> status, Action<MediaItem> onStart, CancellationToken cancellationToken,
        TimeSpan? trimStart = null, TimeSpan? trimEnd = null) =>
        ConvertAsync(items, outputDirectory, extension,
            async (i, o, p, ct) => { await convert(i, o, p, ct); return null; },
            overall, status, onStart, cancellationToken, trimStart, trimEnd);

    /// <summary>Variante où la conversion renvoie le texte à afficher sur la ligne une fois terminée (null = « ✓ Terminé en … »).</summary>
    public static async Task<Result> ConvertAsync(IReadOnlyList<MediaItem> items, string outputDirectory, string extension,
        Func<string, string, IProgress<double>, CancellationToken, Task<string?>> convert,
        IProgress<double> overall, Action<string> status, Action<MediaItem> onStart, CancellationToken cancellationToken,
        TimeSpan? trimStart = null, TimeSpan? trimEnd = null)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var item in items) item.Reset(MediaState.Waiting);

        // Poids de chaque fichier = sa durée (à défaut, la durée moyenne des autres).
        double? Seconds(MediaItem i) => i.Media?.Duration?.TotalSeconds is > 0 and var d ? Trim.EffectiveSeconds(d, trimStart, trimEnd) : null;
        var known = items.Select(Seconds).OfType<double>().ToList();
        var fallback = known.Count > 0 ? known.Average() : 1;
        var weights = items.Select(i => Seconds(i) ?? fallback).ToArray();
        var total = weights.Sum();

        var written = new List<string>();
        var failures = new List<(MediaItem, string)>();
        var batchClock = Stopwatch.StartNew();
        var doneWeight = 0.0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var weight = weights[i];
            var position = $"{i + 1}/{items.Count}";
            item.State = MediaState.Running;
            item.StatusText = "Démarrage…";
            onStart(item);
            status($"Conversion {position} : {item.Name}…");

            var fileClock = Stopwatch.StartNew();
            var progress = new Progress<double>(v =>
            {
                if (item.State != MediaState.Running) return;
                item.Progress = v;
                var left = RemainingTime.Estimate(v, fileClock.Elapsed);
                item.StatusText = $"{v:0 %}" + (left is { } l ? " · " + RemainingTime.Format(l) : "");

                var all = (doneWeight + v * weight) / total;
                overall.Report(all);
                var allLeft = RemainingTime.Estimate(all, batchClock.Elapsed);
                status($"Conversion {position} : {item.Name}" + (allLeft is { } a ? $" · reste {RemainingTime.Format(a)} au total" : "…"));
            });

            var target = AllInOne.Core.FileNames.Unique(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(item.Path) + extension));
            try
            {
                var done = await convert(item.Path, target, progress, cancellationToken);
                item.State = MediaState.Done;
                item.Progress = 1;
                item.StatusText = done ?? $"✓ Terminé en {MediaInfo.FormatDuration(fileClock.Elapsed)}";
                written.Add(target);
            }
            catch (OperationCanceledException)
            {
                item.State = MediaState.Cancelled;
                item.StatusText = "Arrêté";
                foreach (var rest in items.Skip(i + 1)) rest.Reset(MediaState.Idle);
                throw;
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "Conversion de " + item.Path);
                var message = ex is VideoConversionException ? ex.Message : $"{item.Name} : {ex.Message}";
                item.State = MediaState.Failed;
                var reason = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                item.StatusText = "✗ Échec" + (reason is null ? "" : " : " + reason);
                item.Error = message;
                failures.Add((item, message));
            }
            doneWeight += weight;
            overall.Report(doneWeight / total);
        }
        return new Result(written, failures);
    }
}
