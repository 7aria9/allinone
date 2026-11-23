using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using AllInOne.Core.Localization;

namespace AllInOne.App;

/// <summary>
/// Traduit les textes écrits en dur dans les fichiers .axaml (en français) : textes, contenus de boutons et de listes,
/// info-bulles, filigranes. Les textes liés aux données ne correspondent à aucune clé et restent intacts.
/// </summary>
internal static class Translator
{
    public static void Apply(ILogical root)
    {
        if (L.Language == "fr") return;
        Visit(root);
    }

    private static void Visit(ILogical node)
    {
        if (node is AvaloniaObject o)
        {
            switch (o)
            {
                case TextBlock tb:
                    if (tb.Inlines is { Count: > 0 } inlines)
                    {
                        foreach (var run in inlines.OfType<Run>()) run.Text = Tr(run.Text);
                    }
                    else
                    {
                        Set(tb, TextBlock.TextProperty, tb.Text);
                    }
                    break;
                case TextBox box:
                    Set(box, TextBox.WatermarkProperty, box.Watermark);
                    break;
            }
            if (o is ContentControl { Content: string content } cc) Set(cc, ContentControl.ContentProperty, content);
            if (o is Window { Title: { } title } w) w.Title = Tr(title);
            if (o is Control c && ToolTip.GetTip(c) is string tip) ToolTip.SetTip(c, Tr(tip));
        }
        foreach (var child in node.LogicalChildren.ToList()) Visit(child);
    }

    private static void Set<T>(AvaloniaObject o, AvaloniaProperty<T> property, string? value) where T : class?
    {
        if (string.IsNullOrEmpty(value)) return;
        var translated = Tr(value);
        if (!ReferenceEquals(translated, value) && translated != value) o.SetValue(property, (T)(object)translated);
    }

    private static string? Tr(string? text) => text is null ? null : L.T(text);
}
