using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using AllInOne.Core.Localization;

namespace AllInOne.App;

/// <summary>Pinceaux du thème (Theme.axaml), pour les fenêtres construites en code.</summary>
internal static class ThemeBrush
{
    public static IBrush Get(string key)
    {
        var app = Application.Current!;
        return app.Resources.TryGetResource(key, null, out var v) && v is IBrush b ? b
            : app.Styles.TryGetResource(key, null, out var s) && s is IBrush sb ? sb : Brushes.Gray;
    }

    public static WindowIcon Icon() => new(AssetLoader.Open(new Uri("avares://LocalConvert/Assets/localconvert.ico")));
}

/// <summary>
/// Fenêtre d'aide : la liste des rubriques à gauche (une par écran, plus les premiers pas et les questions fréquentes),
/// et à droite les étapes à suivre et les astuces de la rubrique choisie. Une seule fenêtre d'aide est ouverte à la fois.
/// </summary>
internal sealed class HelpWindow : Window
{
    private readonly IReadOnlyList<HelpTopic> _topics = HelpContent.Topics();
    private readonly List<RadioButton> _buttons = [];
    private readonly StackPanel _content = new() { Margin = new Thickness(28, 22, 28, 24), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer _scroll;

    public HelpWindow()
    {
        Title = L.T("Aide de LocalConvert");
        Icon = ThemeBrush.Icon();
        Width = 1000;
        Height = 660;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var nav = new StackPanel();
        nav.Children.Add(new TextBlock { Text = L.T("AIDE"), Classes = { "section" }, Margin = new Thickness(14, 0, 0, 10) });
        for (var i = 0; i < _topics.Count; i++)
        {
            var index = i;
            var button = new RadioButton { Classes = { "nav" }, GroupName = "help", Content = _topics[i].Label };
            button.IsCheckedChanged += (_, _) => { if (button.IsChecked == true) ShowTopic(index); };
            _buttons.Add(button);
            nav.Children.Add(button);
        }
        var navBorder = new Border
        {
            Width = 280, Background = ThemeBrush.Get("Surface"), BorderBrush = ThemeBrush.Get("Line"),
            BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 18),
            Child = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
        };
        DockPanel.SetDock(navBorder, Dock.Left);

        var stripe = new Border { Height = 4, Background = ThemeBrush.Get("AccentGradient") };
        DockPanel.SetDock(stripe, Dock.Top);

        _scroll = new ScrollViewer { Content = _content };
        var root = new DockPanel();
        root.Children.Add(stripe);
        root.Children.Add(navBorder);
        root.Children.Add(_scroll);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SelectTopic(0);
    }

    /// <summary>Affiche la rubrique demandée (et coche son bouton dans la liste).</summary>
    public void SelectTopic(int index)
    {
        index = Math.Clamp(index, 0, _topics.Count - 1);
        if (_buttons[index].IsChecked == true) ShowTopic(index);
        else _buttons[index].IsChecked = true;
    }

    public int TopicCount => _topics.Count;

    private void ShowTopic(int index)
    {
        var topic = _topics[index];
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = $"{topic.Icon}  {topic.Title}", Classes = { "pageTitle" }, TextWrapping = TextWrapping.Wrap });
        _content.Children.Add(new TextBlock { Text = topic.Intro, Classes = { "hint" }, FontSize = 14, Margin = new Thickness(0, 4, 0, 20) });

        if (topic.Steps.Count > 0)
        {
            _content.Children.Add(new TextBlock { Text = L.T("COMMENT FAIRE"), Classes = { "section" } });
            for (var i = 0; i < topic.Steps.Count; i++)
                _content.Children.Add(Row(NumberBadge(i + 1), topic.Steps[i]));
            _content.Children.Add(new Border { Height = 14 });
        }

        if (topic.Tips.Count > 0)
        {
            _content.Children.Add(new TextBlock { Text = topic.Steps.Count > 0 ? L.T("BON À SAVOIR") : L.T("QUESTIONS ET RÉPONSES"), Classes = { "section" } });
            var card = new StackPanel();
            foreach (var tip in topic.Tips) card.Children.Add(Row(Bullet(), tip));
            _content.Children.Add(new Border { Classes = { "card" }, Child = card });
        }
        _scroll.Offset = default;
    }

    private static Control Row(Control marker, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(marker, Dock.Left);
        row.Children.Add(marker);
        row.Children.Add(new SelectableTextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21, VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static Control NumberBadge(int number) => new Border
    {
        Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = ThemeBrush.Get("AccentGradient"),
        Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock
        {
            Text = number.ToString(L.Culture), FontWeight = FontWeight.Bold, FontSize = 13, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Control Bullet() => new Border
    {
        Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = ThemeBrush.Get("Accent"),
        Margin = new Thickness(4, 7, 14, 0), VerticalAlignment = VerticalAlignment.Top,
    };
}
