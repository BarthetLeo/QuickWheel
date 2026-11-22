using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using QuickWheel.Services;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Views;

/// <summary>Petit badge en haut de l'écran pendant un enregistrement. Ne prend ni le focus ni les clics.</summary>
public sealed class RecordingBadge : Window
{
    private readonly TextBlock _text = new() { FontSize = 13, Foreground = Palette.TextBrush, FontFamily = WheelRenderer.UiFont, VerticalAlignment = VerticalAlignment.Center };

    public RecordingBadge(string text)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;

        var dot = new Ellipse { Width = 10, Height = 10, Fill = IconFactory.Brush(Color.FromRgb(0xE0, 0x5A, 0x4A)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(_text);
        Content = new Border
        {
            Background = IconFactory.Brush(Color.FromArgb(0xF0, Palette.Surface.R, Palette.Surface.G, Palette.Surface.B)),
            BorderBrush = Palette.BrassBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 8, 18, 8), Margin = new Thickness(0, 14, 0, 0), Child = row
        };
        _text.Text = text;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        };
        Loaded += (_, _) => Place();
        SizeChanged += (_, _) => Place();
    }

    public void SetText(string text) => _text.Text = text;

    private void Place()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top;
    }
}
