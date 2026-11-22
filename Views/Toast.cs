using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using QuickWheel.Services;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Views;

/// <summary>
/// Petite notification au thème QuickWheel, en bas à droite, qui disparaît seule.
/// Ne vole pas le focus ; un clic la ferme. Remplace les bulles Windows de l'icône de notification.
/// </summary>
public sealed class Toast : Window
{
    private static Toast? _current;
    private readonly DispatcherTimer _timer = new();

    private Toast(string title, string message, bool error)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = WheelRenderer.UiFont;
        Cursor = System.Windows.Input.Cursors.Hand;

        var accent = error ? Color.FromRgb(0xE0, 0x8A, 0x7A) : Palette.Brass;

        var text = new StackPanel { MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Palette.TextBrush, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(message))
            text.Children.Add(new TextBlock { Text = message, FontSize = 12, Foreground = Palette.DimBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        var row = new DockPanel();
        var logo = Logo.Create(30);
        logo.Margin = new Thickness(0, 0, 12, 0);
        logo.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(logo, Dock.Left);
        row.Children.Add(logo);
        row.Children.Add(text);

        var card = new Border
        {
            Background = IconFactory.Brush(Color.FromArgb(0xF7, Palette.Surface.R, Palette.Surface.G, Palette.Surface.B)),
            BorderBrush = IconFactory.Brush(Color.FromArgb(0x99, accent.R, accent.G, accent.B)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 16, 10), Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.5, Color = Colors.Black },
            Child = row
        };
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth;
            Top = wa.Bottom - ActualHeight;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            var slide = new TranslateTransform(0, 12);
            card.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        MouseLeftButtonUp += (_, _) => FadeOut();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();

        _timer.Interval = TimeSpan.FromSeconds(error ? 6 : 3.5);
        _timer.Tick += (_, _) => FadeOut();
        _timer.Start();
    }

    private bool _closing;

    private void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        var a = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        a.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, a);
    }

    /// <summary>Affiche une notification (remplace la précédente).</summary>
    public static void Show(string title, string message = "", bool error = false)
    {
        _current?.Close();
        _current = new Toast(title, message, error);
        _current.Closed += (s, _) => { if (_current == s) _current = null; };
        _current.Show();
    }
}
