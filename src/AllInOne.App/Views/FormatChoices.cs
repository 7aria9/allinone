using System.Windows.Controls;
using AllInOne.Core;

namespace AllInOne.App.Views;

internal static class FormatChoices
{
    public static void Fill(ComboBox combo, ImageFormatKind selected)
    {
        combo.Items.Clear();
        foreach (var (format, label) in new[]
                 {
                     (ImageFormatKind.Png, "PNG"),
                     (ImageFormatKind.Jpeg, "JPEG"),
                     (ImageFormatKind.Webp, "WebP"),
                     (ImageFormatKind.Bmp, "BMP"),
                     (ImageFormatKind.Gif, "GIF"),
                     (ImageFormatKind.Tiff, "TIFF"),
                 })
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = format });
        }
        combo.SelectedIndex = combo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (ImageFormatKind)i.Tag == selected);
    }

    public static ImageFormatKind Selected(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: ImageFormatKind f } ? f : ImageFormatKind.Png;
}
