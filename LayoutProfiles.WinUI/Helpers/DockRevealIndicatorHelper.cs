using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Color = Windows.UI.Color;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Themed semi-transparent dock-edge pill while the widget is auto-hidden.
/// </summary>
internal static class DockRevealIndicatorHelper
{
    public const double PillLong = 112;
    public const double PillThick = 38;
    public const int EdgeInsetPx = 6;

    /// <summary>Extra depth into the desktop for cursor reveal (beyond pill thickness).</summary>
    public const int RevealExtraSlopPx = 16;
    public static int RevealBandDepthPx => (int)PillThick + EdgeInsetPx + RevealExtraSlopPx;

    /// <summary>
    /// Side length (px) of the square region that hosts the chevron at the geometric
    /// center of the pill. Matching <see cref="PillThick"/> keeps the chevron inside
    /// the rounded-corner ink area for all edges.
    /// </summary>
    private const double ArrowBoxSize = PillThick;

    /// <summary>Chevron arm length along its pointing axis (px in pill space).</summary>
    private const double ArrowArmLen = 6;

    /// <summary>Half-distance between the two chevron tails, perpendicular to the pointing axis.</summary>
    private const double ArrowArmSpread = 6;

    private const double ArrowStrokeThickness = 1.75;

    public static Border Create()
    {
        var parts = new PillParts();
        var corner = PillThick / 2;

        parts.Fill = new Border
        {
            CornerRadius = new CornerRadius(corner),
            Background = AppTheme.Palette.Background,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        parts.ArrowHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
        };

        // Fixed-size square Path with Stretch=None: the geometry coords are
        // absolute within the path's render slot, so the tip we compute at
        // (ArrowBoxSize/2, ArrowBoxSize/2) lands exactly on the geometric
        // center of the path -- which the parent grid then centers in the pill.
        parts.Arrow = new Microsoft.UI.Xaml.Shapes.Path
        {
            Width = ArrowBoxSize,
            Height = ArrowBoxSize,
            Stretch = Stretch.None,
            Stroke = AppTheme.Palette.Primary,
            StrokeThickness = ArrowStrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(0),
        };

        parts.ArrowHost.Children.Add(parts.Arrow);

        var body = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Children =
            {
                parts.Fill,
                parts.ArrowHost,
            },
        };

        var capsule = new Border
        {
            IsHitTestVisible = true,
            Visibility = Visibility.Visible,
            Child = body,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
        };
        capsule.Tag = parts;
        return capsule;
    }

    public static void ApplyTheme(Border capsule, bool dark)
    {
        if (capsule.Tag is not PillParts parts)
        {
            return;
        }

        _ = dark;
        parts.Fill.Background = AppTheme.Palette.Background;
        parts.Arrow.Stroke = AppTheme.Palette.Primary;
    }

    public static void ApplyLayout(Border capsule, WindowDockEdge edge)
    {
        if (capsule.Tag is not PillParts parts)
        {
            return;
        }

        capsule.Width = edge is WindowDockEdge.Left or WindowDockEdge.Right
            ? PillThick
            : PillLong;
        capsule.Height = edge is WindowDockEdge.Left or WindowDockEdge.Right
            ? PillLong
            : PillThick;
        capsule.Margin = new Thickness(0);

        var corner = Math.Min(capsule.Width, capsule.Height) / 2;
        parts.Fill.CornerRadius = new CornerRadius(corner);

        parts.ArrowHost.Width = capsule.Width;
        parts.ArrowHost.Height = capsule.Height;
        parts.ArrowHost.Margin = new Thickness(0);

        parts.Arrow.Data = BuildChevronGeometry(edge);

        capsule.HorizontalAlignment = HorizontalAlignment.Center;
        capsule.VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>
    /// Builds a chevron path whose visual TIP sits at the geometric center of
    /// the <see cref="ArrowBoxSize"/>×<see cref="ArrowBoxSize"/> render slot.
    /// The rotation is baked into the point coordinates, so no
    /// <see cref="RenderTransform"/> or <see cref="Viewbox"/> centering is
    /// required (and none of their stroke/stretch quirks can offset the tip).
    /// </summary>
    private static Geometry BuildChevronGeometry(WindowDockEdge edge)
    {
        var center = ArrowBoxSize / 2.0;
        var rad = ArrowAngleForEdge(edge) * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        // Unrotated frame (chevron points right): tip at origin, tails at
        // (-ArrowArmLen, ±ArrowArmSpread). Rotate about origin, then translate
        // so the tip lands at the box center.
        Point Place(double dx, double dy) => new(
            center + (dx * cos) - (dy * sin),
            center + (dx * sin) + (dy * cos));

        var topTail = Place(-ArrowArmLen, -ArrowArmSpread);
        var tip = Place(0, 0);
        var bottomTail = Place(-ArrowArmLen, ArrowArmSpread);

        var figure = new PathFigure
        {
            StartPoint = topTail,
            IsClosed = false,
        };
        figure.Segments.Add(new LineSegment { Point = tip });
        figure.Segments.Add(new LineSegment { Point = bottomTail });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Direction the chevron points (0° = right). Each edge aims into the work
    /// area (away from the screen bezel).
    /// </summary>
    private static double ArrowAngleForEdge(WindowDockEdge edge) =>
        edge switch
        {
            WindowDockEdge.Left => 0,
            WindowDockEdge.Right => 180,
            WindowDockEdge.Top => 90,
            WindowDockEdge.Bottom => 270,
            _ => 0,
        };

    private sealed class PillParts
    {
        public Border Fill = null!;
        public Grid ArrowHost = null!;
        public Microsoft.UI.Xaml.Shapes.Path Arrow = null!;
    }
}

internal enum WindowDockEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom,
}
