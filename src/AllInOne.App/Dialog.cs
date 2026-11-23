using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AllInOne.App;

public enum MessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }
public enum MessageBoxImage { None, Information, Warning, Error, Question }
public enum MessageBoxResult { None, OK, Cancel, Yes, No }

/// <summary>
/// Boîte de message aux couleurs du programme (fond sombre, bandeau dégradé rose-violet, boutons arrondis),
/// identique sous Windows, macOS et Linux.
/// </summary>
public static class Dialog
{
    public static async Task<MessageBoxResult> Show(Window? owner, string text, string title = "AllInOne",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var res = Application.Current!.Resources;
        IBrush B(string key) => res.TryGetResource(key, null, out var v) && v is IBrush b ? b
            : Application.Current.Styles.TryGetResource(key, null, out var s) && s is IBrush sb ? sb : Brushes.Gray;

        var result = buttons is MessageBoxButton.OK ? MessageBoxResult.OK
            : buttons is MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;

        owner ??= (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { IsVisible: true } mw ? mw : null;

        var w = new Window
        {
            Title = title,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = owner is null,
            Topmost = owner is null,
            Background = B("Surface"),
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };

        var (glyph, color) = image switch
        {
            MessageBoxImage.Error => ("✕", B("Accent")),
            MessageBoxImage.Warning => ("!", B("Warning")),
            MessageBoxImage.Question => ("?", B("Accent2")),
            MessageBoxImage.Information => ("i", B("Accent2")),
            _ => ((string?)null, (IBrush?)null),
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
                    Text = glyph, FontWeight = FontWeight.Black, FontSize = 16, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(badge, Dock.Left);
            header.Children.Add(badge);
        }
        header.Children.Add(new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center,
        });

        var body = new ScrollViewer
        {
            MaxHeight = 420,
            Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20 },
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var effectiveDefault = defaultResult != MessageBoxResult.None ? defaultResult
            : buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
        void Add(string label, MessageBoxResult r, bool cancel = false)
        {
            var b = new Button { Content = label, MinWidth = 96, IsCancel = cancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (r == effectiveDefault) { b.IsDefault = true; b.Classes.Add("primary"); }
            b.Click += (_, _) => { result = r; w.Close(); };
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

        var frame = new DockPanel();
        var stripe = new Border { Height = 4, Background = B("AccentGradient") };
        DockPanel.SetDock(stripe, Dock.Top);
        frame.Children.Add(stripe);
        frame.Children.Add(content);

        w.Content = new Border { BorderBrush = B("Line"), BorderThickness = new Thickness(1), Child = frame };
        w.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(w).Properties.IsLeftButtonPressed) w.BeginMoveDrag(e);
        };

        if (owner is not null)
        {
            await w.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            w.Closed += (_, _) => closed.TrySetResult();
            w.Show();
            await closed.Task;
        }
        return result;
    }
}
