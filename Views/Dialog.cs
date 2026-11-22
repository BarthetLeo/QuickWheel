using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using QuickWheel.Services;

namespace QuickWheel.Views;

public enum DialogKind { Info, Question, Warning, Danger, Error }

/// <summary>Bouton d'un <see cref="Dialog"/>.</summary>
public sealed record DialogButton(string Text, bool Primary = false, bool Cancel = false);

/// <summary>
/// Boîte de dialogue au thème QuickWheel (remplace MessageBox) : carte graphite sans bordure système,
/// pastille d'icône, titre, message et boutons. Entrée = bouton principal, Échap = annuler.
/// </summary>
public sealed class Dialog : Window
{
    private static readonly Color Red = Color.FromRgb(0xE0, 0x8A, 0x7A);
    private int _result = -1;

    private Dialog(Window? owner, DialogKind kind, string title, string message, IReadOnlyList<DialogButton> buttons)
    {
        Owner = owner is { IsVisible: true } ? owner : null;
        WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = Owner == null;
        Topmost = Owner == null;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = title;
        FontFamily = WheelRenderer.UiFont;
        FontSize = 13;

        bool danger = kind is DialogKind.Danger or DialogKind.Error;
        var accent = danger ? Red : Palette.Brass;

        // Pastille d'icône
        string glyph = kind switch
        {
            DialogKind.Question => "mdi:help",
            DialogKind.Warning => "mdi:alert-outline",
            DialogKind.Danger => "mdi:trash-can-outline",
            DialogKind.Error => "mdi:alert-circle-outline",
            _ => "mdi:information-variant",
        };
        var badge = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Top,
            Background = IconFactory.Brush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
            BorderBrush = IconFactory.Brush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)), BorderThickness = new Thickness(1),
            Child = IconFactory.Create(glyph, 20, IconFactory.Brush(accent)),
            Margin = new Thickness(0, 0, 16, 0)
        };
        ((FrameworkElement)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)badge.Child).VerticalAlignment = VerticalAlignment.Center;

        var text = new StackPanel { MaxWidth = 380 };
        text.Children.Add(new TextBlock
        {
            Text = title, FontFamily = (FontFamily)Application.Current.FindResource("Display"),
            FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Palette.TextBrush, TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(message))
            text.Children.Add(new TextBlock
            {
                Text = message, Foreground = Palette.DimBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 19,
                Margin = new Thickness(0, 6, 0, 0)
            });

        var head = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        head.Children.Add(text);

        // Boutons, alignés à droite ; le principal en dernier
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        for (int i = 0; i < buttons.Count; i++)
        {
            var spec = buttons[i];
            int index = i;
            var b = new Button
            {
                Content = spec.Text, MinWidth = 92, Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = spec.Primary, IsCancel = spec.Cancel,
            };
            if (spec.Primary)
            {
                b.Style = (Style)Application.Current.FindResource("Primary");
                if (danger)
                {
                    b.Background = IconFactory.Brush(Color.FromRgb(0xB5, 0x4A, 0x4A));
                    b.BorderBrush = b.Background;
                    b.Foreground = Palette.TextBrush;
                }
            }
            b.Click += (_, _) => { _result = index; Close(); };
            row.Children.Add(b);
            if (spec.Primary) Loaded += (_, _) => b.Focus();
        }

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(row);

        // Fine ligne d'accent en haut de la carte
        var card = new Border
        {
            Background = IconFactory.Brush(Palette.Surface),
            BorderBrush = Palette.LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 20, 18), Margin = new Thickness(18), MinWidth = 340,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.55, Color = Colors.Black },
            Child = body
        };
        var grid = new Grid();
        grid.Children.Add(card);
        grid.Children.Add(new Border
        {
            Height = 2, Margin = new Thickness(44, 19, 44, 0), VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(1), Background = IconFactory.Brush(accent), Opacity = 0.85
        });
        Content = grid;

        // Déplaçable par n'importe quel point de la carte
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        Opacity = 0;
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        };
    }

    /// <summary>Affiche la boîte et renvoie l'index du bouton cliqué (-1 si fermée autrement).</summary>
    public static int Show(Window? owner, DialogKind kind, string title, string message, params DialogButton[] buttons)
    {
        var d = new Dialog(owner, kind, title, message, buttons);
        d.ShowDialog();
        if (d._result < 0)
        {
            // Échap / fermeture : équivaut au bouton d'annulation
            int cancel = Array.FindIndex(buttons, b => b.Cancel);
            return cancel;
        }
        return d._result;
    }

    public static void Alert(Window? owner, DialogKind kind, string title, string message = "") =>
        Show(owner, kind, title, message, new DialogButton("OK", Primary: true, Cancel: true));

    public static bool Confirm(Window? owner, string title, string message = "", string yes = "Continuer",
        DialogKind kind = DialogKind.Question) =>
        Show(owner, kind, title, message, new DialogButton("Annuler", Cancel: true), new DialogButton(yes, Primary: true)) == 1;
}
