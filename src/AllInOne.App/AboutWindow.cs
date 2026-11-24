using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AllInOne.Core.Localization;

namespace AllInOne.App;

/// <summary>Fenêtre « À propos » : nom, version, icône, licence MIT, lien vers le projet et bibliothèques utilisées.</summary>
internal sealed class AboutWindow : Window
{
    public const string ProjectUrl = "https://github.com/7aria9/localconvert-offline-file-converter";

    /// <summary>Bibliothèques livrées avec LocalConvert et leur licence (voir aussi le README).</summary>
    private static readonly (string Name, string License)[] Libraries =
    [
        ("Avalonia", "MIT"),
        ("PDFsharp 6.2.4", "MIT"),
        ("PDFtoImage · PDFium", "MIT · BSD-3 / Apache 2.0"),
        ("SkiaSharp", "MIT"),
        ("Magick.NET · ImageMagick", "Apache 2.0"),
        ("PdfPig", "Apache 2.0"),
        ("RapidOcrNet · PaddleOCR", "Apache 2.0"),
        ("ONNX Runtime", "MIT"),
    ];

    public AboutWindow()
    {
        Title = L.T("À propos de LocalConvert");
        Icon = ThemeBrush.Icon();
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeBrush.Get("Surface");

        var content = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };

        // Icône, nom et version
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new TextBlock { Text = "LOCAL", FontSize = 30, FontWeight = FontWeight.Black });
        name.Children.Add(new TextBlock { Text = "CONVERT", FontSize = 30, FontWeight = FontWeight.Black, Foreground = ThemeBrush.Get("AccentGradient") });
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(name);
        identity.Children.Add(new TextBlock
        {
            Text = L.T("Version {0}", MainWindow.AppVersion), FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrush.Get("Accent"),
        });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var logo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://LocalConvert/Assets/localconvert-128.png"))),
            Width = 96, Height = 96, Margin = new Thickness(-8, -4, 12, 0),
        };
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        header.Children.Add(identity);
        content.Children.Add(header);

        content.Children.Add(new TextBlock
        {
            Text = L.T("Tes PDF, images, vidéos et sons, sans rien envoyer sur Internet"), FontSize = 14, TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = L.T("Toutes les conversions se font sur ton ordinateur : aucun fichier n'est envoyé en ligne, aucun compte n'est nécessaire."),
            Classes = { "note" }, FontSize = 13, Margin = new Thickness(0, 6, 0, 16),
        });

        // Licence et auteur
        content.Children.Add(new TextBlock { Text = L.T("LICENCE"), Classes = { "section" } });
        content.Children.Add(new TextBlock
        {
            Text = L.T("Logiciel libre distribué sous licence MIT. © 2026 7aria9."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        });
        content.Children.Add(new TextBlock
        {
            Text = L.T("Tu peux l'utiliser, le copier, le modifier et le redistribuer librement, y compris pour un usage commercial."),
            Classes = { "note" }, Margin = new Thickness(0, 0, 0, 16),
        });

        // Bibliothèques
        content.Children.Add(new TextBlock { Text = L.T("BIBLIOTHÈQUES UTILISÉES"), Classes = { "section" } });
        var libraries = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        for (var i = 0; i < Libraries.Length; i++)
        {
            libraries.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var lib = new TextBlock { Text = Libraries[i].Name, FontSize = 12, Margin = new Thickness(0, 1) };
            var lic = new TextBlock { Text = Libraries[i].License, FontSize = 12, Foreground = ThemeBrush.Get("TextDim"), Margin = new Thickness(12, 1, 0, 1) };
            Grid.SetRow(lib, i);
            Grid.SetRow(lic, i);
            Grid.SetColumn(lic, 1);
            libraries.Children.Add(lib);
            libraries.Children.Add(lic);
        }
        content.Children.Add(libraries);
        content.Children.Add(new TextBlock
        {
            Text = L.T("FFmpeg (vidéos et sons) est un programme séparé, téléchargé à part sous licence GPL : il n'est pas inclus dans LocalConvert."),
            Classes = { "note" }, Margin = new Thickness(0, 0, 0, 14),
        });

        content.Children.Add(new TextBlock
        {
            Text = $".NET {Environment.Version} · {RuntimeInformation.OSDescription.Trim()} · {RuntimeInformation.ProcessArchitecture}",
            FontSize = 11, Foreground = ThemeBrush.Get("TextDim"), TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // Boutons
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var github = new Button { Content = L.T("🌐  Page du projet (GitHub)") };
        github.Click += async (_, _) => await OpenProjectPage();
        var license = new Button { Content = L.T("📜  Lire la licence") };
        license.Click += async (_, _) => await Dialog.Show(this, LicenseText(), L.T("Licence MIT"));
        var close = new Button { Content = L.T("Fermer"), IsDefault = true, IsCancel = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "primary" } };
        close.Click += (_, _) => Close();
        bar.Children.Add(github);
        bar.Children.Add(license);
        bar.Children.Add(close);
        content.Children.Add(bar);

        var frame = new DockPanel();
        var stripe = new Border { Height = 4, Background = ThemeBrush.Get("AccentGradient") };
        DockPanel.SetDock(stripe, Dock.Top);
        frame.Children.Add(stripe);
        frame.Children.Add(content);
        Content = frame;
    }

    private async Task OpenProjectPage()
    {
        try
        {
            if (await Launcher.LaunchUriAsync(new Uri(ProjectUrl))) return;
        }
        catch
        {
            // pas de navigateur disponible : l'adresse est affichée ci-dessous
        }
        await Dialog.Show(this, L.T("Impossible d'ouvrir le navigateur. Adresse du projet :\n{0}", ProjectUrl), "LocalConvert");
    }

    /// <summary>Texte complet de la licence (fichier LICENSE du dépôt, intégré à l'application).</summary>
    internal static string LicenseText()
    {
        try
        {
            using var reader = new StreamReader(AssetLoader.Open(new Uri("avares://LocalConvert/Assets/LICENSE.txt")));
            return reader.ReadToEnd().Trim();
        }
        catch
        {
            return "MIT License\n\nCopyright (c) 2026 7aria9\n\n" + ProjectUrl;
        }
    }
}
