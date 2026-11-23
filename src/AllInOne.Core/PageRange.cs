namespace AllInOne.Core;

/// <summary>
/// Analyse des plages de pages saisies par l'utilisateur, numérotées à partir de 1,
/// par exemple « 1-3, 5, 8- » (8 jusqu'à la fin) ou « -2 » (1 et 2).
/// </summary>
public static class PageRange
{
    /// <summary>Renvoie les index de pages (à partir de 0), dans l'ordre saisi, sans doublon.</summary>
    public static IReadOnlyList<int> Parse(string? text, int pageCount)
    {
        if (pageCount < 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*")
            return Enumerable.Range(0, pageCount).ToList();

        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var rawPart in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            int start, end;
            var dash = part.IndexOf('-');
            if (dash < 0)
            {
                start = end = ParseNumber(part, pageCount);
            }
            else
            {
                var left = part[..dash].Trim();
                var right = part[(dash + 1)..].Trim();
                start = left.Length == 0 ? 1 : ParseNumber(left, pageCount);
                end = right.Length == 0 ? pageCount : ParseNumber(right, pageCount);
            }

            var step = start <= end ? 1 : -1;
            for (var p = start; p != end + step; p += step)
            {
                if (seen.Add(p - 1)) result.Add(p - 1);
            }
        }
        return result;
    }

    private static int ParseNumber(string s, int pageCount)
    {
        if (!int.TryParse(s, out var n))
            throw new FormatException($"Numéro de page invalide : « {s} »");
        if (n < 1 || n > pageCount)
            throw new ArgumentOutOfRangeException(null, $"La page {n} n'existe pas (le document a {pageCount} page(s)).");
        return n;
    }
}
