using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuickWheel.Models;
using Path = System.Windows.Shapes.Path;

namespace QuickWheel.Services;

/// <summary>Transforme une référence d'icône ("mdi:", "img:", "exe:", "txt:") en élément WPF.</summary>
public static class IconFactory
{
    private static readonly Dictionary<string, Geometry> GeometryCache = new();
    private static readonly Dictionary<string, ImageSource?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static Geometry? MdiGeometry(string name)
    {
        if (GeometryCache.TryGetValue(name, out var g)) return g;
        var entry = IconCatalog.Get(name);
        if (entry == null) return null;
        try
        {
            g = Geometry.Parse(entry.PathData);
            g.Freeze();
        }
        catch { return null; }
        GeometryCache[name] = g;
        return g;
    }

    public static FrameworkElement Create(string? iconRef, double size, Brush brush)
    {
        iconRef ??= "";
        int colon = iconRef.IndexOf(':');
        string kind = colon > 0 ? iconRef[..colon] : "txt";
        string value = colon > 0 ? iconRef[(colon + 1)..] : iconRef;

        switch (kind)
        {
            case "mdi":
                var geo = MdiGeometry(value);
                if (geo != null)
                    return new Viewbox
                    {
                        Width = size, Height = size, Stretch = Stretch.Uniform,
                        Child = new Path { Data = geo, Fill = brush, Width = 24, Height = 24, Stretch = Stretch.None },
                        IsHitTestVisible = false
                    };
                break;

            case "img":
            case "exe":
                var src = kind == "img" ? LoadImage(value) : ExeIcon(value);
                if (src != null)
                    return new Image
                    {
                        Source = src, Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false,
                        SnapsToDevicePixels = true
                    };
                break;
        }

        // Texte / emoji (et repli si l'icône est introuvable)
        return new TextBlock
        {
            Text = kind == "txt" ? value : "?",
            FontSize = size * 0.82,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI Variable Text, Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
    }

    public static ImageSource? LoadImage(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (ImageCache.TryGetValue(path, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // ne verrouille pas le fichier
                bmp.DecodePixelWidth = 128;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                result = bmp;
            }
        }
        catch { }
        ImageCache[path] = result;
        return result;
    }

    /// <summary>Icône associée à un exe (ou à n'importe quel fichier).</summary>
    public static ImageSource? ExeIcon(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        string key = "exe|" + path;
        if (ImageCache.TryGetValue(key, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    result = src;
                }
            }
        }
        catch { }
        ImageCache[key] = result;
        return result;
    }

    // ---------------- Couleurs ----------------

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return null; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Luminance relative (0 sombre → 1 clair).</summary>
    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
