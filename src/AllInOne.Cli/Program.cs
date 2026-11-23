using System.Globalization;
using AllInOne.Core;
using AllInOne.Core.Images;
using AllInOne.Core.Pdf;
using AllInOne.Core.Video;
using AllInOne.Core.Localization;

namespace AllInOne.Cli;

public static class Program
{
    private const string Help = """
        LocalConvert : outils PDF et images en ligne de commande.

        Les pages sont numérotées à partir de 1. Plages : « 1-3,5,8- ».
        Si -o/--output n'est pas indiqué, les commandes de modification écrivent « <nom>_modifie.pdf ».

          images2pdf <sortie.pdf> <image...> [--size A4|A3|A5|Letter|Legal|fit] [--orientation auto|portrait|landscape]
                                             [--margin <mm>] [--no-enlarge]
          merge      <sortie.pdf> <fichier.pdf|image...>
          delete     <entrée.pdf> <pages>              [-o sortie.pdf]
          extract    <entrée.pdf> <pages>              [-o sortie.pdf]
          reorder    <entrée.pdf> <ordre>              [-o sortie.pdf]   ex. ordre « 3,1,2 » ou « 5-1 »
          move       <entrée.pdf> <de> <vers>          [-o sortie.pdf]
          insert     <entrée.pdf> <position> <fichier...> [-o sortie.pdf] insère avant la page <position>
          rotate     <entrée.pdf> <pages> <90|180|270|-90> [-o sortie.pdf]
          split      <entrée.pdf> <dossier> [--every N | --ranges "1-3,4-7,8-"]   (par défaut : une page par fichier)
          compress   <entrée.pdf> <sortie.pdf> [--level legere|moyenne|forte]
          pdf2img    <entrée.pdf> <dossier> [--format png|jpg|bmp|gif|tiff|webp] [--dpi 150] [--pages 1-3] [--quality 90]
          convert    <dossier> <image...> --format png|jpg|bmp|gif|tiff|webp|avif [--quality 90] [--max-width px] [--max-height px]
          video      <dossier> <vidéo...> --format mp4|mkv|webm|avi|ogg|mov|gif [--quality haute|moyenne|faible]
                     [--max-height 720] [--no-audio] [--copy] [--start 1:30] [--end 2:45]   (nécessite FFmpeg dans le PATH)
          frames     <dossier> <vidéo...> [--format png|jpg] [--fps 1] [--start 0:10] [--end 0:20] [--quality 90]
                     [--max-height 720]   extrait les images (toutes par défaut) dans <dossier>/<nom>_images
          audio      <dossier> <son|vidéo...> --format mp3|wav|aiff|flac|ogg|opus|m4a [--quality haute|moyenne|faible]
                     [--rate 44100] [--channels 1|2] [--start 0:30] [--end 1:00]   (nécessite FFmpeg dans le PATH)
          info       <entrée.pdf>

        Option commune : --lang fr|en|de|es (langue des messages).
        """;

    public static int Main(string[] args)
    {
        // Langue : « --lang en|de|es|fr », sinon la variable LOCALCONVERT_LANG, celle choisie dans l'application, ou celle du système.
        var lang = Array.FindIndex(args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        if (lang >= 0 && lang + 1 < args.Length)
        {
            L.Initialize(args[lang + 1]);
            args = args.Take(lang).Concat(args.Skip(lang + 2)).ToArray();
        }
        else
        {
            L.Initialize(Environment.GetEnvironmentVariable("LOCALCONVERT_LANG"));
        }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(L.T(Help));
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var parsed = Args.Parse(args.Skip(1));
            Run(args[0].ToLowerInvariant(), parsed);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine(L.T("Erreur : ") + ex.Message);
            return 2;
        }
    }

    private static void Run(string command, Args a)
    {
        switch (command)
        {
            case "images2pdf":
            {
                a.Require(2, L.T("images2pdf <sortie.pdf> <image...>"));
                PdfTools.ImagesToPdf(a.Positional.Skip(1), a.Positional[0], ImageOptions(a));
                Done(a.Positional[0]);
                break;
            }
            case "merge":
            {
                a.Require(2, L.T("merge <sortie.pdf> <fichier...>"));
                PdfTools.Merge(a.Positional.Skip(1), a.Positional[0], ImageOptions(a));
                Done(a.Positional[0]);
                break;
            }
            case "delete":
            {
                a.Require(2, L.T("delete <entrée.pdf> <pages>"));
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.DeletePages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)));
                Done(output);
                break;
            }
            case "extract":
            {
                a.Require(2, L.T("extract <entrée.pdf> <pages>"));
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.ExtractPages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)));
                Done(output);
                break;
            }
            case "reorder":
            {
                a.Require(2, L.T("reorder <entrée.pdf> <ordre>"));
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.ReorderPages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)));
                Done(output);
                break;
            }
            case "move":
            {
                a.Require(3, L.T("move <entrée.pdf> <de> <vers>"));
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.MovePage(input, output, Int(a.Positional[1]) - 1, Int(a.Positional[2]) - 1);
                Done(output);
                break;
            }
            case "insert":
            {
                a.Require(3, L.T("insert <entrée.pdf> <position> <fichier...>"));
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.InsertPages(input, output, Int(a.Positional[1]) - 1, a.Positional.Skip(2), ImageOptions(a));
                Done(output);
                break;
            }
            case "rotate":
            {
                a.Require(3, L.T("rotate <entrée.pdf> <pages> <degrés>"));
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.RotatePages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)), Int(a.Positional[2]));
                Done(output);
                break;
            }
            case "split":
            {
                a.Require(2, L.T("split <entrée.pdf> <dossier>"));
                var files = a.Get("ranges") is { } ranges ? PdfTools.SplitByRanges(a.Positional[0], a.Positional[1], ranges)
                    : a.Get("every") is { } every ? PdfTools.SplitEvery(a.Positional[0], a.Positional[1], Int(every))
                    : PdfTools.SplitPages(a.Positional[0], a.Positional[1]);
                Console.WriteLine(L.T("{0} fichier(s) créé(s) dans {1}", files.Count, a.Positional[1]));
                break;
            }
            case "compress":
            {
                a.Require(2, L.T("compress <entrée.pdf> <sortie.pdf>"));
                var level = (a.Get("level") ?? "moyenne").Trim().ToLowerInvariant() switch
                {
                    "legere" or "légère" or "light" => PdfCompressionLevel.Light,
                    "forte" or "strong" => PdfCompressionLevel.Strong,
                    "moyenne" or "medium" => PdfCompressionLevel.Medium,
                    var other => throw new ArgumentException(L.T("Niveau inconnu : {0} (legere, moyenne ou forte)", other)),
                };
                var r = PdfCompressor.Compress(a.Positional[0], a.Positional[1], level);
                Console.WriteLine(r.KeptOriginal
                    ? L.T("Déjà optimisé : copie identique écrite dans {0}", a.Positional[1])
                    : L.T("{0} → {1} (−{2:0 %}), {3} image(s) recompressée(s) : {4}", MediaInfo.FormatSize(r.OriginalBytes), MediaInfo.FormatSize(r.CompressedBytes), r.Saving, r.ImagesRecompressed, a.Positional[1]));
                break;
            }
            case "pdf2img":
            {
                a.Require(2, L.T("pdf2img <entrée.pdf> <dossier>"));
                var input = a.Positional[0];
                var format = ImageFormats.Parse(a.Get("format") ?? "png");
                var pages = a.Get("pages") is { } range ? PageRange.Parse(range, PdfInfo.GetPageCount(input)) : null;
                var files = PdfRenderer.ToImages(input, a.Positional[1], format, Int(a.Get("dpi") ?? "150"), pages,
                    new ImageConversionOptions { Quality = Int(a.Get("quality") ?? "90") });
                Console.WriteLine(L.T("{0} image(s) créée(s) dans {1}", files.Count, a.Positional[1]));
                break;
            }
            case "convert":
            {
                a.Require(2, L.T("convert <dossier> <image...> --format <format>"));
                var format = ImageFormats.Parse(a.Get("format") ?? throw new ArgumentException("--format est obligatoire."));
                var options = new ImageConversionOptions
                {
                    Quality = Int(a.Get("quality") ?? "90"),
                    MaxWidth = a.Get("max-width") is { } w ? Int(w) : null,
                    MaxHeight = a.Get("max-height") is { } h ? Int(h) : null,
                };
                var files = ImageConverter.ConvertMany(a.Positional.Skip(1), a.Positional[0], format, options);
                Console.WriteLine(L.T("{0} image(s) convertie(s) dans {1}", files.Count, a.Positional[0]));
                break;
            }
            case "video":
            {
                a.Require(2, L.T("video <dossier> <vidéo...> --format <format>"));
                var format = VideoFormats.Parse(a.Get("format") ?? throw new ArgumentException("--format est obligatoire."));
                var options = new VideoConversionOptions
                {
                    Quality = VideoFormats.ParseQuality(a.Get("quality") ?? "moyenne"),
                    MaxHeight = a.Get("max-height") is { } h ? Int(h) : null,
                    RemoveAudio = a.Flag("no-audio"),
                    CopyStreams = a.Flag("copy"),
                    Start = Time(a.Get("start")),
                    End = Time(a.Get("end")),
                };
                var lastPercent = -1;
                var progress = new Progress<double>(v =>
                {
                    var percent = (int)(v * 100);
                    if (percent != lastPercent) { lastPercent = percent; Console.Write($"\r{percent,3} %"); }
                });
                var files = VideoConverter.ConvertManyAsync(a.Positional.Skip(1).ToList(), a.Positional[0], format, options, progress,
                    status: new Progress<string>(m => Console.Write("\r" + m + Environment.NewLine))).GetAwaiter().GetResult();
                Console.WriteLine(L.T("\r{0} vidéo(s) convertie(s) dans {1}", files.Count, a.Positional[0]));
                break;
            }
            case "frames":
            {
                a.Require(2, L.T("frames <dossier> <vidéo...>"));
                var options = new FrameExtractionOptions
                {
                    Format = ImageFormats.Parse(a.Get("format") ?? "png"),
                    FramesPerSecond = a.Get("fps") is { } f ? double.Parse(f.Replace(',', '.'), CultureInfo.InvariantCulture) : null,
                    JpegQuality = Int(a.Get("quality") ?? "90"),
                    MaxHeight = a.Get("max-height") is { } h ? Int(h) : null,
                    Start = Time(a.Get("start")),
                    End = Time(a.Get("end")),
                };
                foreach (var video in a.Positional.Skip(1))
                {
                    var folder = FileNames.UniqueDirectory(Path.Combine(a.Positional[0], Path.GetFileNameWithoutExtension(video) + "_images"));
                    var files = VideoFrames.ExtractAsync(video, folder, options).GetAwaiter().GetResult();
                    Console.WriteLine(L.T("{0} image(s) extraite(s) dans {1}", files.Count, folder));
                }
                break;
            }
            case "audio":
            {
                a.Require(2, L.T("audio <dossier> <fichier...> --format <format>"));
                var format = AudioFormats.Parse(a.Get("format") ?? throw new ArgumentException("--format est obligatoire."));
                var options = new AudioConversionOptions
                {
                    Quality = VideoFormats.ParseQuality(a.Get("quality") ?? "haute"),
                    SampleRate = a.Get("rate") is { } r ? Int(r) : null,
                    Channels = a.Get("channels") is { } c ? Int(c) : null,
                    Start = Time(a.Get("start")),
                    End = Time(a.Get("end")),
                };
                var files = AudioConverter.ConvertManyAsync(a.Positional.Skip(1).ToList(), a.Positional[0], format, options,
                    status: new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult();
                Console.WriteLine(L.T("{0} fichier(s) converti(s) dans {1}", files.Count, a.Positional[0]));
                break;
            }
            case "info":
            {
                a.Require(1, L.T("info <entrée.pdf>"));
                Console.WriteLine(L.T("{0} : {1} page(s)", a.Positional[0], PdfInfo.GetPageCount(a.Positional[0])));
                break;
            }
            default:
                throw new ArgumentException(L.T("Commande inconnue : {0}. Tapez « localconvert-cli --help ».", command));
        }
    }

    private static ImagePageOptions ImageOptions(Args a) => new()
    {
        PageSize = (a.Get("size") ?? "A4").ToLowerInvariant() switch
        {
            "a4" => PageSizeMode.A4,
            "a3" => PageSizeMode.A3,
            "a5" => PageSizeMode.A5,
            "letter" => PageSizeMode.Letter,
            "legal" => PageSizeMode.Legal,
            "fit" or "image" => PageSizeMode.FitImage,
            var s => throw new ArgumentException(L.T("Taille de page inconnue : {0}", s)),
        },
        Orientation = (a.Get("orientation") ?? "auto").ToLowerInvariant() switch
        {
            "auto" => PageOrientation.Auto,
            "portrait" => PageOrientation.Portrait,
            "landscape" or "paysage" => PageOrientation.Landscape,
            var s => throw new ArgumentException(L.T("Orientation inconnue : {0}", s)),
        },
        MarginMm = a.Get("margin") is { } m ? double.Parse(m.Replace(',', '.'), CultureInfo.InvariantCulture) : 10,
        EnlargeSmallImages = !a.Flag("no-enlarge"),
    };

    private static string Output(Args a, string input) =>
        a.Get("output") ?? a.Get("o") ??
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, Path.GetFileNameWithoutExtension(input) + "_modifie.pdf");

    private static TimeSpan? Time(string? text) =>
        Trim.TryParseTime(text, out var t) ? t : throw new FormatException(L.T("Temps invalide : « {0} » (exemples : 90, 1:30, 0:01:30)", text));

    private static int Int(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException(L.T("Nombre invalide : « {0} »", s));

    private static void Done(string path) => Console.WriteLine(L.T("Fichier créé : {0}", path));
}

/// <summary>Arguments : valeurs positionnelles, options « --nom valeur » / « -o valeur » et drapeaux « --nom ».</summary>
internal sealed class Args
{
    private static readonly HashSet<string> Flags = new() { "no-enlarge", "no-audio", "copy" };
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; } = new();

    public static Args Parse(IEnumerable<string> args)
    {
        var result = new Args();
        using var e = args.GetEnumerator();
        while (e.MoveNext())
        {
            var arg = e.Current;
            if (arg.StartsWith('-') && arg.Length > 1 && !char.IsDigit(arg[1]))
            {
                var name = arg.TrimStart('-');
                if (Flags.Contains(name)) { result._options[name] = null; continue; }
                if (!e.MoveNext()) throw new ArgumentException(L.T("Valeur manquante pour {0}.", arg));
                result._options[name] = e.Current;
            }
            else
            {
                result.Positional.Add(arg);
            }
        }
        return result;
    }

    public string? Get(string name) => _options.TryGetValue(name, out var v) ? v : null;
    public bool Flag(string name) => _options.ContainsKey(name);

    public void Require(int count, string usage)
    {
        if (Positional.Count < count) throw new ArgumentException(L.T("Usage : localconvert-cli ") + usage);
    }
}
