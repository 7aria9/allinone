namespace AllInOne.Core;

public static class FileNames
{
    /// <summary>Renvoie <paramref name="path"/> s'il n'existe pas, sinon « nom (2).ext », « nom (3).ext »…</summary>
    public static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Renvoie <paramref name="path"/> si aucun fichier ni dossier ne porte ce nom, sinon « nom (2) », « nom (3) »…</summary>
    public static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        for (var i = 2; ; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
    }
}
