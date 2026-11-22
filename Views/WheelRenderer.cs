using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickWheel.Models;
using QuickWheel.Services;
using Path = System.Windows.Shapes.Path;

namespace QuickWheel.Views;

/// <summary>Palette du thème « cadran » : graphite + un seul accent laiton.</summary>
public static class Palette
{
    public static readonly Color Ink = Color.FromRgb(0x10, 0x12, 0x16);
    public static readonly Color Surface = Color.FromRgb(0x16, 0x18, 0x1D);
    public static readonly Color Raised = Color.FromRgb(0x1D, 0x20, 0x27);
    public static readonly Color Line = Color.FromRgb(0x2B, 0x30, 0x3A);
    public static readonly Color Text = Color.FromRgb(0xE4, 0xE6, 0xEB);
    public static readonly Color Dim = Color.FromRgb(0x8B, 0x92, 0xA1);
    public static readonly Color Brass = Color.FromRgb(0xC9, 0xA8, 0x6A);

    public static readonly Brush TextBrush = IconFactory.Brush(Text);
    public static readonly Brush DimBrush = IconFactory.Brush(Dim);
    public static readonly Brush BrassBrush = IconFactory.Brush(Brass);
    public static readonly Brush LineBrush = IconFactory.Brush(Line);

    /// <summary>Couleurs proposées pour les cases (sobres, lisibles sur fond sombre).</summary>
    public static readonly string[] Swatches =
    {
        "#1D2027", "#2A2F3A", "#3A3F4B", "#4A3B2A", "#5C4A2E", "#6E5D3D",
        "#2E3B4E", "#24435C", "#1F4D4A", "#2F4A33", "#4B4A28", "#5A3A2A",
        "#5A2A2A", "#4E2840", "#3D2E56", "#7A6A9C", "#4F7CAC", "#3E8E7E",
        "#6E9A4F", "#B08D3C", "#C46A3C", "#B54A4A", "#A54C7C", "#D8D4CC",
    };
}

public sealed class SlotVisual
{
    public required int Ring { get; init; }
    public required int Index { get; init; }
    public required Shortcut Shortcut { get; init; }
    public required Path Shape { get; init; }
    public required Brush Fill { get; init; }
    public required Brush HoverFill { get; init; }
}

public sealed class WheelRender
{
    public List<SlotVisual> Slots { get; } = new();
    public TextBlock? CenterLabel { get; set; }
    public string CenterDefault { get; set; } = "";
    private SlotVisual? _hover;

    public SlotVisual? Find(int ring, int index) => Slots.FirstOrDefault(s => s.Ring == ring && s.Index == index);

    public void SetHover(SlotVisual? slot)
    {
        if (slot == _hover) return;
        if (_hover != null) _hover.Shape.Fill = _hover.Fill;
        _hover = slot;
        if (_hover != null) _hover.Shape.Fill = _hover.HoverFill;
        if (CenterLabel != null) CenterLabel.Text = slot?.Shortcut.Label ?? CenterDefault;
        if (CenterDisc != null) CenterDisc.StrokeThickness = 1;
    }

    /// <summary>Survol du centre (cliquable) : anneau laiton plus marqué.</summary>
    public void SetCenterHot()
    {
        if (CenterDisc != null) CenterDisc.StrokeThickness = 2.5;
    }

    public Ellipse? CenterDisc { get; set; }
}

public sealed class WheelDrawOptions
{
    public double CenterRadius { get; init; } = 46;
    public double Gap { get; init; } = 4;
    public string CenterTitle { get; init; } = "";
    public string CenterText { get; init; } = "";
    /// <summary>Mode éditeur : graduations de lunette, case sélectionnée.</summary>
    public bool Editor { get; init; }
    public string? SelectedId { get; init; }
    /// <summary>Raccourci en cours de glisser-déposer (dessiné en fantôme).</summary>
    public string? GhostId { get; init; }
    /// <summary>Affiche le logo au centre (roue principale : un clic dessus ouvre QuickWheel).</summary>
    public bool CenterLogo { get; init; }
}

/// <summary>Dessine une roue multi-anneaux dans un Canvas, centrée sur (0,0).</summary>
public static class WheelRenderer
{
    private static readonly Brush SlotStroke = IconFactory.Brush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CenterFill = IconFactory.Brush(Color.FromArgb(0xF7, Palette.Surface.R, Palette.Surface.G, Palette.Surface.B));

    private static Point P(double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(r * Math.Sin(a), -r * Math.Cos(a));
    }

    public static Geometry SlotGeometry(double inner, double outer, double startDeg, double endDeg)
    {
        if (endDeg - startDeg >= 359.9)
        {
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(0, 0), outer, outer),
                new EllipseGeometry(new Point(0, 0), inner, inner));
            ring.Freeze();
            return ring;
        }
        bool large = endDeg - startDeg > 180;
        var fig = new PathFigure { StartPoint = P(outer, startDeg), IsClosed = true };
        fig.Segments.Add(new ArcSegment(P(outer, endDeg), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(inner, endDeg), true));
        fig.Segments.Add(new ArcSegment(P(inner, startDeg), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }

    public static WheelRender Draw(Canvas canvas, WheelContext ctx, WheelDrawOptions o)
    {
        canvas.Children.Clear();
        var render = new WheelRender { CenterDefault = o.CenterText };
        var geo = WheelLogic.Geometry(ctx.Layout, o.CenterRadius, o.Gap);
        double outerMost = geo.Count > 0 ? geo[^1].Outer : o.CenterRadius;

        if (o.Editor) DrawBezel(canvas, outerMost + 10);

        foreach (var g in geo)
        {
            var ring = ctx.Layout.Rings[g.Index];
            double gapDeg = g.Count > 1 ? (2.0 / g.Outer) * 180 / Math.PI : 0;
            for (int i = 0; i < g.Count; i++)
            {
                var sc = ctx.Resolve(ring.Items[i]);
                if (sc == null) continue;

                bool ghost = sc.Id == o.GhostId;
                bool selected = sc.Id == o.SelectedId;
                var baseColor = IconFactory.ParseColor(sc.Color) ?? Palette.Raised;
                var fill = IconFactory.Brush(ghost ? Color.FromArgb(0x55, Palette.Brass.R, Palette.Brass.G, Palette.Brass.B)
                                                   : Color.FromArgb(0xF2, baseColor.R, baseColor.G, baseColor.B));
                var hover = IconFactory.Brush(IconFactory.Blend(baseColor, Palette.Brass, 0.55));

                var path = new Path
                {
                    Data = SlotGeometry(g.Inner, g.Outer, g.SlotStart(i) + gapDeg / 2, g.SlotStart(i) + g.Step - gapDeg / 2),
                    Fill = fill,
                    Stroke = selected ? Palette.BrassBrush : ghost ? Palette.BrassBrush : SlotStroke,
                    StrokeThickness = selected ? 2 : 1,
                    StrokeDashArray = ghost ? new DoubleCollection { 3, 2 } : null,
                    Tag = sc,
                };
                canvas.Children.Add(path);
                render.Slots.Add(new SlotVisual { Ring = g.Index, Index = i, Shortcut = sc, Shape = path, Fill = fill, HoverFill = hover });

                // Repère de dossier : petit arc laiton sur le bord extérieur
                if (sc.IsFolder)
                {
                    double c = g.SlotCenter(i), half = Math.Min(14, g.Step / 5);
                    var arc = new PathFigure { StartPoint = P(g.Outer - 4, c - half) };
                    arc.Segments.Add(new ArcSegment(P(g.Outer - 4, c + half), new Size(g.Outer - 4, g.Outer - 4), 0, false, SweepDirection.Clockwise, true));
                    canvas.Children.Add(new Path
                    {
                        Data = new PathGeometry(new[] { arc }), Stroke = Palette.BrassBrush, StrokeThickness = 2.5,
                        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false
                    });
                }

                AddContent(canvas, sc, g, i, baseColor, ghost);
            }
        }

        // Disque central
        double cr = o.CenterRadius;
        var disc = new Ellipse
        {
            Width = cr * 2, Height = cr * 2, Fill = CenterFill, Stroke = Palette.BrassBrush,
            StrokeThickness = 1, IsHitTestVisible = false
        };
        Canvas.SetLeft(disc, -cr); Canvas.SetTop(disc, -cr);
        canvas.Children.Add(disc);
        render.CenterDisc = disc;

        var center = new StackPanel { Width = cr * 1.75, IsHitTestVisible = false };
        if (o.CenterLogo && cr >= 36)
        {
            var logo = Logo.Create(Math.Min(22, cr * 0.42));
            logo.Margin = new Thickness(0, 0, 0, 3);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(logo);
        }
        if (!string.IsNullOrEmpty(o.CenterTitle))
            center.Children.Add(new TextBlock
            {
                Text = o.CenterTitle, FontSize = 10, Foreground = Palette.DimBrush, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = UiFont
            });
        render.CenterLabel = new TextBlock
        {
            Text = o.CenterText, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Palette.TextBrush,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = cr * 1.2,
            TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = UiFont
        };
        center.Children.Add(render.CenterLabel);
        center.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(center, -center.DesiredSize.Width / 2);
        Canvas.SetTop(center, -center.DesiredSize.Height / 2);
        canvas.Children.Add(center);

        return render;
    }

    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    private static void AddContent(Canvas canvas, Shortcut sc, RingGeometry g, int i, Color baseColor, bool ghost)
    {
        double thickness = g.Outer - g.Inner;
        double mid = (g.Inner + g.Outer) / 2;
        double arc = mid * g.Step * Math.PI / 180;      // largeur disponible au milieu de la case
        double iconSize = Math.Clamp(thickness * 0.36, 16, 34);
        bool showLabel = thickness >= 56 && arc >= 58;

        var iconColor = IconFactory.ParseColor(sc.IconColor)
                        ?? (IconFactory.Luminance(baseColor) > 0.62 ? Palette.Surface : Palette.Text);
        var brush = IconFactory.Brush(iconColor);

        var stack = new StackPanel { IsHitTestVisible = false, Opacity = ghost ? 0.7 : 1 };
        var icon = IconFactory.Create(sc.Icon, iconSize, brush);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        if (showLabel)
        {
            stack.Children.Add(new TextBlock
            {
                Text = sc.Label, FontSize = 11, Foreground = brush, Margin = new Thickness(0, 3, 0, 0),
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Math.Min(120, arc * 0.82), HorizontalAlignment = HorizontalAlignment.Center, FontFamily = UiFont
            });
        }
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var pos = P(mid, g.Count == 1 ? 0 : g.SlotCenter(i));
        Canvas.SetLeft(stack, pos.X - stack.DesiredSize.Width / 2);
        Canvas.SetTop(stack, pos.Y - stack.DesiredSize.Height / 2);
        canvas.Children.Add(stack);
    }

    /// <summary>Graduations façon lunette de montre autour de la roue (éditeur uniquement).</summary>
    private static void DrawBezel(Canvas canvas, double r)
    {
        var minor = new StreamGeometry();
        var major = new StreamGeometry();
        using (var a = minor.Open())
        using (var b = major.Open())
        {
            for (int i = 0; i < 120; i++)
            {
                double deg = i * 3;
                bool isMajor = i % 10 == 0;
                var ctx = isMajor ? b : a;
                ctx.BeginFigure(P(r, deg), false, false);
                ctx.LineTo(P(r + (isMajor ? 9 : 4), deg), true, false);
            }
        }
        minor.Freeze(); major.Freeze();
        canvas.Children.Add(new Path { Data = minor, Stroke = Palette.LineBrush, StrokeThickness = 1, IsHitTestVisible = false });
        canvas.Children.Add(new Path { Data = major, Stroke = Palette.BrassBrush, StrokeThickness = 1.5, IsHitTestVisible = false, Opacity = 0.7 });
    }
}
