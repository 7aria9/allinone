using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AllInOne.App;

/// <summary>
/// Remplace MessageBox.Show : même usage, mais une fenêtre aux couleurs du programme
/// (fond sombre, bandeau dégradé rose-violet, boutons arrondis).
/// </summary>
public static class Dialog
{
    public static MessageBoxResult Show(string text, string title = "AllInOne",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(null, text, title, buttons, image, defaultResult);

    public static MessageBoxResult Show(Window? owner, string text, string title = "AllInOne",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var res = Application.Current.Resources;
        Brush B(string key) => (Brush)res[key];

        var result = buttons is MessageBoxButton.OK ? MessageBoxResult.OK
            : buttons is MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;

        var w = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = owner is null,
            Topmost = owner is null,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = B("Text"),
        };
        owner ??= Application.Current.MainWindow is { IsLoaded: true } mw ? mw : null;
        if (owner is not null) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var (glyph, color) = image switch
        {
            MessageBoxImage.Error => ("✕", B("Accent")),
            MessageBoxImage.Warning => ("!", (Brush)new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))),
            MessageBoxImage.Question => ("?", B("Accent2")),
            MessageBoxImage.Information => ("i", B("Accent2")),
            _ => ((string?)null, (Brush?)null),
        };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (glyph is not null)
        {
            var badge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = color,
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = glyph, FontWeight = FontWeights.Black, FontSize = 16, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(badge, Dock.Left);
            header.Children.Add(badge);
        }
        header.Children.Add(new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
        });

        var body = new ScrollViewer
        {
            MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = B("Text"), LineHeight = 20 },
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        void Add(string label, MessageBoxResult r, bool cancel = false)
        {
            var b = new Button { Content = label, MinWidth = 96, IsCancel = cancel };
            b.Click += (_, _) => { result = r; w.Close(); };
            if (r == (defaultResult != MessageBoxResult.None ? defaultResult : FirstOf(buttons))) b.IsDefault = true;
            bar.Children.Add(b);
        }
        switch (buttons)
        {
            case MessageBoxButton.OK: Add("OK", MessageBoxResult.OK, cancel: true); break;
            case MessageBoxButton.OKCancel: Add("OK", MessageBoxResult.OK); Add("Annuler", MessageBoxResult.Cancel, true); break;
            case MessageBoxButton.YesNo: Add("Oui", MessageBoxResult.Yes); Add("Non", MessageBoxResult.No, true); break;
            default: Add("Oui", MessageBoxResult.Yes); Add("Non", MessageBoxResult.No); Add("Annuler", MessageBoxResult.Cancel, true); break;
        }

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 18), MinWidth = 340, MaxWidth = 560 };
        content.Children.Add(header);
        content.Children.Add(body);
        content.Children.Add(bar);

        var frame = new Grid();
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        frame.RowDefinitions.Add(new RowDefinition());
        var stripe = new Border { Background = B("AccentGradient"), CornerRadius = new CornerRadius(12, 12, 0, 0) };
        Grid.SetRow(content, 1);
        frame.Children.Add(stripe);
        frame.Children.Add(content);

        w.Content = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(12),
            Background = B("Surface"), BorderBrush = B("Line"), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.55, Color = Colors.Black },
            Child = frame,
        };
        w.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) w.DragMove(); };
        w.ShowDialog();
        return result;
    }

    private static MessageBoxResult FirstOf(MessageBoxButton b) =>
        b is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
}
