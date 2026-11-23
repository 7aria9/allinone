using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AllInOne.Core.Localization;

/// <summary>
/// Traduction des textes affichés. Le texte français sert de clé : <c>L.T("Ajouter des images")</c> renvoie sa traduction
/// dans la langue choisie (fichiers <c>en.json</c>, <c>de.json</c>, <c>es.json</c> intégrés à la bibliothèque), ou le texte
/// français s'il n'est pas traduit. Les textes avec des valeurs utilisent des marqueurs <c>{0}</c>, <c>{1}</c>…
/// </summary>
public static class L
{
    /// <summary>Langues proposées, dans l'ordre du sélecteur : code et nom dans la langue elle-même.</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } =
    [
        ("fr", "Français"),
        ("en", "English"),
        ("de", "Deutsch"),
        ("es", "Español"),
    ];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new();
    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();

    /// <summary>Code de la langue en cours (fr, en, de ou es).</summary>
    public static string Language { get; private set; } = "fr";

    /// <summary>Culture de la langue en cours : séparateur décimal, dates…</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Choisit la langue : celle demandée si elle est proposée, sinon celle enregistrée dans les réglages, sinon celle du
    /// système (anglais pour une langue non proposée).
    /// </summary>
    public static void Initialize(string? requested = null)
    {
        var code = Normalize(requested) ?? Normalize(Settings.Language) ?? SystemLanguage();
        SetLanguage(code);
    }

    /// <summary>Langue du système si elle est proposée, sinon l'anglais.</summary>
    public static string SystemLanguage() => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? "en";

    public static void SetLanguage(string code)
    {
        Language = Normalize(code) ?? "en";
        Culture = CultureInfo.GetCultureInfo(Language switch
        {
            "fr" => "fr-FR",
            "de" => "de-DE",
            "es" => "es-ES",
            _ => "en-US",
        });
        _current = Language == "fr" ? new Dictionary<string, string>() : Catalog(Language);
    }

    /// <summary>Traduction d'un texte français.</summary>
    public static string T(string french) => _current.TryGetValue(french, out var t) ? t : french;

    /// <summary>Traduction d'un texte français à marqueurs {0}, {1}…, rempli avec les valeurs données.</summary>
    public static string T(string french, params object?[] args) => string.Format(Culture, T(french), args);

    /// <summary>Le catalogue d'une langue (clé : texte français). Vide pour le français.</summary>
    public static IReadOnlyDictionary<string, string> Catalog(string code)
    {
        lock (Catalogs)
        {
            if (Catalogs.TryGetValue(code, out var c)) return c;
            var name = $"AllInOne.Core.Localization.{code}.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            var loaded = stream is null
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
            Catalogs[code] = loaded;
            return loaded;
        }
    }

    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var two = code.Trim().ToLowerInvariant();
        if (two.Length > 2) two = two[..2];
        return Languages.Any(l => l.Code == two) ? two : null;
    }
}

/// <summary>Réglages de l'utilisateur, partagés par l'application et la ligne de commande (settings.json).</summary>
public static class Settings
{
    public static string File =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllInOne", "settings.json");

    /// <summary>Langue choisie dans l'application, ou null pour suivre le système.</summary>
    public static string? Language
    {
        get => Read().TryGetValue("language", out var v) ? v : null;
        set
        {
            var all = Read();
            if (value is null) all.Remove("language"); else all["language"] = value;
            Write(all);
        }
    }

    private static Dictionary<string, string> Read()
    {
        try
        {
            return System.IO.File.Exists(File)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(File)) ?? new()
                : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    private static void Write(Dictionary<string, string> values)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Réglage non enregistré : la langue reviendra à celle du système au prochain lancement.
        }
    }
}
