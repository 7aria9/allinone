using System.Text.RegularExpressions;
using System.Xml.Linq;
using AllInOne.Core.Localization;
using AllInOne.Core.Video;

namespace AllInOne.Core.Tests;

/// <summary>Ces tests changent la langue (état global) : ils ne tournent pas en même temps que les autres.</summary>
[CollectionDefinition("Langue", DisableParallelization = true)]
public sealed class LanguageCollection;

[Collection("Langue")]
public class LocalizationTests
{
    private static readonly string[] Translated = ["en", "de", "es"];
    private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}");

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AllInOne.Core"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Dossier src introuvable."), "src");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(SourceRoot(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Textes passés à L.T dans le code (chaînes C# simples, échappements décodés).</summary>
    private static IEnumerable<string> CodeKeys() =>
        SourceFiles("*.cs").SelectMany(f => Regex.Matches(File.ReadAllText(f), @"L\.T\(\s*""((?:[^""\\\n]|\\.)*)""")
            .Select(m => Regex.Unescape(m.Groups[1].Value)));

    /// <summary>Textes écrits en dur dans les .axaml (ceux qui contiennent des mots, pas seulement des sigles).</summary>
    private static IEnumerable<string> XamlKeys()
    {
        string[] attributes = ["Text", "Content", "ToolTip.Tip", "Watermark", "Title"];
        foreach (var file in SourceFiles("*.axaml"))
        {
            foreach (var el in XDocument.Load(file).Descendants())
            {
                foreach (var a in el.Attributes().Where(a => attributes.Contains(a.Name.LocalName)))
                {
                    var v = a.Value;
                    if (v.StartsWith('{') || v is "ALL" or "·IN·ONE" or "AllInOne") continue;
                    if (Regex.IsMatch(v, "[a-zà-ÿ]") && Regex.IsMatch(v, "[A-Za-zÀ-ÿ]{3,}")) yield return v;
                }
            }
        }
    }

    [Fact]
    public void EveryTextIsTranslatedInEveryLanguage()
    {
        var keys = CodeKeys().Concat(XamlKeys()).Distinct().ToList();
        Assert.True(keys.Count > 300, $"Seulement {keys.Count} textes trouvés : l'analyse des sources ne fonctionne plus ?");
        foreach (var code in Translated)
        {
            var catalog = L.Catalog(code);
            var missing = keys.Where(k => !catalog.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{code} : {missing.Count} texte(s) sans traduction :\n" + string.Join("\n", missing));
        }
    }

    [Fact]
    public void CatalogsHaveTheSameKeysAndPlaceholders()
    {
        var reference = L.Catalog("en");
        foreach (var code in Translated)
        {
            var catalog = L.Catalog(code);
            Assert.Equal(reference.Keys.OrderBy(k => k, StringComparer.Ordinal), catalog.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (french, text) in catalog)
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code} : traduction vide pour « {french} »");
                var expected = Placeholder.Matches(french).Select(m => m.Value).Order();
                var actual = Placeholder.Matches(text).Select(m => m.Value).Order();
                Assert.True(expected.SequenceEqual(actual), $"{code} : marqueurs différents pour « {french} » → « {text} »");
            }
        }
    }

    [Fact]
    public void SwitchingLanguageTranslatesTextsAndNumbers()
    {
        try
        {
            L.SetLanguage("en");
            Assert.Equal("Add images", L.T("Ajouter des images"));
            Assert.Equal("Page 3 does not exist (the document has 2 page(s)).", L.T("La page {0} n'existe pas (le document a {1} page(s)).", 3, 2));
            Assert.Equal("12.4 MB", MediaInfo.FormatSize(13_000_000));
            Assert.Equal("Texte inconnu", L.T("Texte inconnu"));

            L.SetLanguage("de");
            Assert.Equal("12,4 MB", MediaInfo.FormatSize(13_000_000));
            Assert.Equal("Bilder hinzufügen", L.T("Ajouter des images"));

            L.Initialize("pt-BR"); // langue non proposée : réglage enregistré ou langue du système, sinon anglais
            Assert.Contains(L.Language, L.Languages.Select(l => l.Code));
        }
        finally
        {
            L.SetLanguage("fr");
        }
        Assert.Equal("12,4 Mo", MediaInfo.FormatSize(13_000_000));
    }
}
