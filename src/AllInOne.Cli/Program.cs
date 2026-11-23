using System.Globalization;
using AllInOne.Core;
using AllInOne.Core.Images;
using AllInOne.Core.Pdf;

namespace AllInOne.Cli;

public static class Program
{
    private const string Help = """
        AllInOne : outils PDF et images en ligne de commande.

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
          split      <entrée.pdf> <dossier>
          pdf2img    <entrée.pdf> <dossier> [--format png|jpg|bmp|gif|tiff|webp] [--dpi 150] [--pages 1-3] [--quality 90]
          convert    <dossier> <image...> --format png|jpg|bmp|gif|tiff|webp [--quality 90] [--max-width px] [--max-height px]
          info       <entrée.pdf>
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Help);
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
            Console.Error.WriteLine("Erreur : " + ex.Message);
            return 2;
        }
    }

    private static void Run(string command, Args a)
    {
        switch (command)
        {
            case "images2pdf":
            {
                a.Require(2, "images2pdf <sortie.pdf> <image...>");
                PdfTools.ImagesToPdf(a.Positional.Skip(1), a.Positional[0], ImageOptions(a));
                Done(a.Positional[0]);
                break;
            }
            case "merge":
            {
                a.Require(2, "merge <sortie.pdf> <fichier...>");
                PdfTools.Merge(a.Positional.Skip(1), a.Positional[0], ImageOptions(a));
                Done(a.Positional[0]);
                break;
            }
            case "delete":
            {
                a.Require(2, "delete <entrée.pdf> <pages>");
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.DeletePages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)));
                Done(output);
                break;
            }
            case "extract":
            {
                a.Require(2, "extract <entrée.pdf> <pages>");
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.ExtractPages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)));
                Done(output);
                break;
            }
            case "reorder":
            {
                a.Require(2, "reorder <entrée.pdf> <ordre>");
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.ReorderPages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)));
                Done(output);
                break;
            }
            case "move":
            {
                a.Require(3, "move <entrée.pdf> <de> <vers>");
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.MovePage(input, output, Int(a.Positional[1]) - 1, Int(a.Positional[2]) - 1);
                Done(output);
                break;
            }
            case "insert":
            {
                a.Require(3, "insert <entrée.pdf> <position> <fichier...>");
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.InsertPages(input, output, Int(a.Positional[1]) - 1, a.Positional.Skip(2), ImageOptions(a));
                Done(output);
                break;
            }
            case "rotate":
            {
                a.Require(3, "rotate <entrée.pdf> <pages> <degrés>");
                var input = a.Positional[0];
                var output = Output(a, input);
                PdfTools.RotatePages(input, output, PageRange.Parse(a.Positional[1], PdfInfo.GetPageCount(input)), Int(a.Positional[2]));
                Done(output);
                break;
            }
            case "split":
            {
                a.Require(2, "split <entrée.pdf> <dossier>");
                var files = PdfTools.SplitPages(a.Positional[0], a.Positional[1]);
                Console.WriteLine($"{files.Count} fichier(s) créé(s) dans {a.Positional[1]}");
                break;
            }
            case "pdf2img":
            {
                a.Require(2, "pdf2img <entrée.pdf> <dossier>");
                var input = a.Positional[0];
                var format = ImageFormats.Parse(a.Get("format") ?? "png");
                var pages = a.Get("pages") is { } range ? PageRange.Parse(range, PdfInfo.GetPageCount(input)) : null;
                var files = PdfRenderer.ToImages(input, a.Positional[1], format, Int(a.Get("dpi") ?? "150"), pages,
                    new ImageConversionOptions { Quality = Int(a.Get("quality") ?? "90") });
                Console.WriteLine($"{files.Count} image(s) créée(s) dans {a.Positional[1]}");
                break;
            }
            case "convert":
            {
                a.Require(2, "convert <dossier> <image...> --format <format>");
                var format = ImageFormats.Parse(a.Get("format") ?? throw new ArgumentException("--format est obligatoire."));
                var options = new ImageConversionOptions
                {
                    Quality = Int(a.Get("quality") ?? "90"),
                    MaxWidth = a.Get("max-width") is { } w ? Int(w) : null,
                    MaxHeight = a.Get("max-height") is { } h ? Int(h) : null,
                };
                var files = ImageConverter.ConvertMany(a.Positional.Skip(1), a.Positional[0], format, options);
                Console.WriteLine($"{files.Count} image(s) convertie(s) dans {a.Positional[0]}");
                break;
            }
            case "info":
            {
                a.Require(1, "info <entrée.pdf>");
                Console.WriteLine($"{a.Positional[0]} : {PdfInfo.GetPageCount(a.Positional[0])} page(s)");
                break;
            }
            default:
                throw new ArgumentException($"Commande inconnue : {command}. Tapez « allinone --help ».");
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
            var s => throw new ArgumentException($"Taille de page inconnue : {s}"),
        },
        Orientation = (a.Get("orientation") ?? "auto").ToLowerInvariant() switch
        {
            "auto" => PageOrientation.Auto,
            "portrait" => PageOrientation.Portrait,
            "landscape" or "paysage" => PageOrientation.Landscape,
            var s => throw new ArgumentException($"Orientation inconnue : {s}"),
        },
        MarginMm = a.Get("margin") is { } m ? double.Parse(m.Replace(',', '.'), CultureInfo.InvariantCulture) : 10,
        EnlargeSmallImages = !a.Flag("no-enlarge"),
    };

    private static string Output(Args a, string input) =>
        a.Get("output") ?? a.Get("o") ??
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, Path.GetFileNameWithoutExtension(input) + "_modifie.pdf");

    private static int Int(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException($"Nombre invalide : « {s} »");

    private static void Done(string path) => Console.WriteLine($"Fichier créé : {path}");
}

/// <summary>Arguments : valeurs positionnelles, options « --nom valeur » / « -o valeur » et drapeaux « --nom ».</summary>
internal sealed class Args
{
    private static readonly HashSet<string> Flags = new() { "no-enlarge" };
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
                if (!e.MoveNext()) throw new ArgumentException($"Valeur manquante pour {arg}.");
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
        if (Positional.Count < count) throw new ArgumentException("Usage : allinone " + usage);
    }
}
