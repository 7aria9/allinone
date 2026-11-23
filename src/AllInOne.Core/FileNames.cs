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
}
