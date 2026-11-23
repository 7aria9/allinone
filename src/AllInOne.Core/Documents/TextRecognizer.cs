using RapidOcrNet;
using SkiaSharp;
using AllInOne.Core.Localization;

namespace AllInOne.Core.Documents;

/// <summary>Une ligne reconnue : son texte, sa position dans l'image (pixels) et la confiance moyenne (0 à 1).</summary>
public sealed record OcrLine(string Text, SKRectI Bounds, float Confidence);

/// <summary>
/// Reconnaissance de texte (OCR) hors ligne, avec RapidOcrNet (Apache 2.0) : ONNX Runtime (MIT) et les modèles
/// PaddleOCR PP-OCRv5 « latin » (Apache 2.0), qui couvrent le français, l'anglais, l'allemand, l'espagnol et les autres
/// langues à alphabet latin. Les modèles (≈ 14 Mo) sont livrés avec l'application dans « models/v5 ».
/// </summary>
public static class TextRecognizer
{
    private static readonly object Gate = new();
    private static RapidOcr? _engine;
    private static string? _unavailable;

    /// <summary>Côté le plus long de l'image analysée par le détecteur de lignes (les lignes sont lues en pleine résolution).</summary>
    private const int DetectionSize = 2048;

    /// <summary>True si l'OCR peut fonctionner sur cet ordinateur (modèles présents, ONNX Runtime chargeable).</summary>
    public static bool IsAvailable => TryGetEngine(out _) is not null;

    /// <summary>Raison pour laquelle l'OCR ne fonctionne pas, ou null s'il fonctionne.</summary>
    public static string? UnavailableReason
    {
        get
        {
            TryGetEngine(out var reason);
            return reason;
        }
    }

    /// <summary>Lit les lignes de texte d'une image. Lance InvalidOperationException si l'OCR n'est pas disponible.</summary>
    public static IReadOnlyList<OcrLine> Recognize(SKBitmap image, float minConfidence = 0.5f, CancellationToken cancellationToken = default)
    {
        var engine = TryGetEngine(out var reason) ?? throw new InvalidOperationException(reason);
        var options = RapidOcrOptions.Default with { ImgResize = DetectionSize, TextScore = minConfidence };
        OcrResult result;
        // Une seule reconnaissance à la fois : le moteur utilise déjà tous les cœurs du processeur.
        lock (Gate) result = engine.Detect(image, options, cancellationToken);

        var lines = new List<OcrLine>();
        foreach (var block in result.TextBlocks)
        {
            var text = PdfTextExtractor.Clean(block.Text ?? "");
            if (text.Length == 0 || block.BoxPoints is not { Length: > 0 } points) continue;
            var bounds = new SKRectI(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
            var confidence = block.CharScores is { Length: > 0 } scores ? scores.Average() : block.BoxScore;
            lines.Add(new OcrLine(text, bounds, confidence));
        }
        return lines;
    }

    private static RapidOcr? TryGetEngine(out string? reason)
    {
        lock (Gate)
        {
            reason = _unavailable;
            if (_engine is not null || _unavailable is not null) return _engine;
            var folder = Path.Combine(AppContext.BaseDirectory, "models", "v5");
            string File(string name) => Path.Combine(folder, name);
            var files = new[] { "ch_PP-OCRv5_mobile_det.onnx", "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                "latin_PP-OCRv5_rec_mobile_infer.onnx", "ppocrv5_latin_dict.txt" };
            if (files.Any(f => !System.IO.File.Exists(File(f))))
            {
                reason = _unavailable = L.T("Reconnaissance de texte (OCR) indisponible : les modèles sont introuvables dans {0}.", folder);
                return null;
            }
            RapidOcr? engine = null;
            try
            {
                engine = new RapidOcr();
                engine.InitModels(File(files[0]), File(files[1]), File(files[2]), File(files[3]), 0);
                _engine = engine;
                return engine;
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException
                                           or EntryPointNotFoundException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                engine?.Dispose();
                reason = _unavailable = L.T("Reconnaissance de texte (OCR) indisponible sur cet ordinateur : {0}",
                    ex.GetBaseException().Message.Split('\n')[0].Trim());
                return null;
            }
        }
    }
}
