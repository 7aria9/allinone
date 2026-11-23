using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Pdf;

public enum PdfCompressionLevel
{
    /// <summary>Images jusqu'à 200 ppp, JPEG qualité 80 : différence presque invisible.</summary>
    Light,
    /// <summary>Images jusqu'à 150 ppp, JPEG qualité 65 : bon compromis pour l'écran et l'impression courante.</summary>
    Medium,
    /// <summary>Images jusqu'à 100 ppp, JPEG qualité 50 : fichier le plus léger, pour l'envoi par e-mail.</summary>
    Strong,
}

public sealed record PdfCompressionResult(long OriginalBytes, long CompressedBytes, int ImagesRecompressed)
{
    /// <summary>Le PDF était déjà optimisé : la copie écrite est identique à l'original.</summary>
    public bool KeptOriginal => CompressedBytes >= OriginalBytes;

    /// <summary>Gain en proportion de la taille d'origine (0,75 = 75 % plus léger).</summary>
    public double Saving => OriginalBytes == 0 ? 0 : 1 - (double)CompressedBytes / OriginalBytes;
}

/// <summary>
/// Allège un PDF en réduisant et recompressant ses images (là où se trouve presque tout le poids d'un PDF scanné
/// ou plein de photos) et en compressant au maximum le reste. Le texte et les dessins vectoriels sont gardés tels quels.
/// </summary>
public static class PdfCompressor
{
    /// <summary>Côté le plus long d'une page A4 en pouces : sert à convertir une résolution (ppp) en pixels.</summary>
    private const double A4LongSideInches = 11.69;

    public static (int MaxPixels, int JpegQuality) Settings(PdfCompressionLevel level) => level switch
    {
        PdfCompressionLevel.Light => ((int)(200 * A4LongSideInches), 80),
        PdfCompressionLevel.Strong => ((int)(100 * A4LongSideInches), 50),
        _ => ((int)(150 * A4LongSideInches), 65),
    };

    public static PdfCompressionResult Compress(string inputPath, string outputPath, PdfCompressionLevel level = PdfCompressionLevel.Medium,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("Le fichier compressé doit être différent du fichier d'origine."));
        var original = new FileInfo(inputPath).Length;
        var (maxPixels, quality) = Settings(level);

        using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        var all = document.Internals.GetAllObjects();
        var masks = new HashSet<PdfObject>();
        var images = new List<PdfDictionary>();
        foreach (var obj in all)
        {
            if (obj is not PdfDictionary dict || dict.Stream is null || dict.Elements.GetName("/Subtype") != "/Image") continue;
            images.Add(dict);
            foreach (var key in new[] { "/SMask", "/Mask" })
                if (Deref(dict.Elements[key]) is PdfDictionary mask) masks.Add(mask);
        }

        var recompressed = 0;
        for (var i = 0; i < images.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Les masques de transparence restent sans perte : un JPEG y ferait des bavures.
            if (!masks.Contains(images[i]) && TryRecompress(images[i], maxPixels, quality)) recompressed++;
            progress?.Report(0.9 * (i + 1) / Math.Max(1, images.Count));
        }

        document.Options.CompressContentStreams = true;
        document.Options.NoCompression = false;
        document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;

        var full = Path.GetFullPath(outputPath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            document.Save(temp);
            var compressed = new FileInfo(temp).Length;
            if (compressed >= original)
            {
                // Rien à gagner : on livre une copie de l'original plutôt qu'un fichier plus lourd.
                File.Copy(inputPath, full, overwrite: true);
                compressed = original;
            }
            else
            {
                File.Move(temp, full, overwrite: true);
            }
            progress?.Report(1);
            return new PdfCompressionResult(original, compressed, recompressed);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Réduit et réencode une image en JPEG si cela la rend nettement plus légère. Les cas particuliers
    /// (CMYK, masques, tableaux /Decode, palettes, 16 bits, filtres multiples) sont laissés intacts.
    /// </summary>
    internal static bool TryRecompress(PdfDictionary image, int maxPixels, int quality)
    {
        var e = image.Elements;
        if (e.GetBoolean("/ImageMask") || e.ContainsKey("/Decode")) return false;
        var width = e.GetInteger("/Width");
        var height = e.GetInteger("/Height");
        if (width < 64 || height < 64) return false;

        var components = Components(Deref(e["/ColorSpace"]));
        if (components is not (1 or 3)) return false;

        var filter = Deref(e["/Filter"]) switch
        {
            PdfName n => n.Value,
            PdfArray { Elements.Count: 1 } a => (Deref(a.Elements[0]) as PdfName)?.Value,
            null => "",
            _ => null,
        };

        var raw = image.Stream.Value;
        MagickImage decoded;
        try
        {
            if (filter == "/DCTDecode")
            {
                decoded = new MagickImage(raw);
            }
            else if (filter is "/FlateDecode" or "")
            {
                if (e.GetInteger("/BitsPerComponent") != 8) return false;
                var pixels = filter == "" ? raw : image.Stream.UnfilteredValue;
                if (pixels is null || pixels.Length < width * height * components) return false;
                var settings = new MagickReadSettings
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    Format = components == 1 ? MagickFormat.Gray : MagickFormat.Rgb,
                    Depth = 8,
                };
                decoded = new MagickImage(pixels.AsSpan(0, width * height * components).ToArray(), settings);
            }
            else
            {
                return false;
            }
        }
        catch (MagickException)
        {
            return false;
        }

        using (decoded)
        {
            if (decoded.Width > maxPixels || decoded.Height > maxPixels)
                decoded.Resize(new MagickGeometry((uint)maxPixels, (uint)maxPixels) { IgnoreAspectRatio = false });
            decoded.Strip();
            if (components == 1)
            {
                decoded.ColorType = ColorType.Grayscale;
            }
            else
            {
                decoded.ColorSpace = ColorSpace.sRGB;
                decoded.ColorType = ColorType.TrueColor;
            }
            decoded.Quality = (uint)quality;
            var jpeg = decoded.ToByteArray(MagickFormat.Jpeg);

            // Seulement si le gain vaut la peine (sinon on garderait une perte de qualité pour rien).
            if (jpeg.Length > raw.Length * 0.9) return false;

            image.Stream.Value = jpeg;
            e.SetName("/Filter", "/DCTDecode");
            e.Remove("/DecodeParms");
            e.SetInteger("/Width", (int)decoded.Width);
            e.SetInteger("/Height", (int)decoded.Height);
            e.SetInteger("/BitsPerComponent", 8);
            e.SetName("/ColorSpace", components == 1 ? "/DeviceGray" : "/DeviceRGB");
            return true;
        }
    }

    /// <summary>Nombre de composantes d'un espace de couleur ; 0 si on ne sait pas le traiter (CMYK, palette, Lab…).</summary>
    private static int Components(PdfItem? colorSpace) => colorSpace switch
    {
        PdfName { Value: "/DeviceRGB" or "/CalRGB" } => 3,
        PdfName { Value: "/DeviceGray" or "/CalGray" } => 1,
        PdfArray a when a.Elements.Count >= 2 && Deref(a.Elements[0]) is PdfName { Value: "/ICCBased" }
                        && Deref(a.Elements[1]) is PdfDictionary icc => icc.Elements.GetInteger("/N") switch
        {
            3 => 3,
            1 => 1,
            _ => 0,
        },
        PdfArray a when a.Elements.Count >= 1 && Deref(a.Elements[0]) is PdfName { Value: "/CalRGB" } => 3,
        PdfArray a when a.Elements.Count >= 1 && Deref(a.Elements[0]) is PdfName { Value: "/CalGray" } => 1,
        _ => 0,
    };

    private static PdfItem? Deref(PdfItem? item) => item is PdfReference r ? r.Value : item;
}
