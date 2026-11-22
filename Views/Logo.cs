using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickWheel.Services;
using Path = System.Windows.Shapes.Path;

namespace QuickWheel.Views;

/// <summary>
/// Logo QuickWheel : une roue à 6 cases, la case du haut « sélectionnée » en laiton, et un moyeu central.
/// Même dessin que Assets/QuickWheel.ico (généré par tools/make-icon.ps1).
/// </summary>
public static class Logo
{
    private const int Segments = 6;
    private const double Inner = 27, Outer = 49, Hub = 13, GapDeg = 9;

    /// <summary>Logo vectoriel (sans fond) de la taille demandée.</summary>
    public static FrameworkElement Create(double size)
    {
        var canvas = new Canvas { Width = 100, Height = 100, IsHitTestVisible = false };

        var dim = IconFactory.Brush(IconFactory.Blend(Palette.Raised, Palette.Brass, 0.38));
        double step = 360.0 / Segments;
        for (int i = 0; i < Segments; i++)
        {
            double start = i * step - step / 2 + GapDeg / 2, end = start + step - GapDeg;
            var seg = new Path
            {
                Data = WheelRenderer.SlotGeometry(Inner, Outer, start, end),
                Fill = i == 0 ? Palette.BrassBrush : dim,
            };
            Canvas.SetLeft(seg, 50); Canvas.SetTop(seg, 50);
            canvas.Children.Add(seg);
        }

        var hub = new Ellipse { Width = Hub * 2, Height = Hub * 2, Fill = Palette.BrassBrush };
        Canvas.SetLeft(hub, 50 - Hub); Canvas.SetTop(hub, 50 - Hub);
        canvas.Children.Add(hub);

        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
