using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// macOS-style pill handle on the screen edge when a docked widget is rolled up.
/// </summary>
internal static class DockRevealIndicatorHelper
{
    // macOS-style edge tick: ~9:1 length:thickness.
    public const double PillLong = 50;
    public const double PillThick = 4;

    /// <summary>Soft grey capsule (~90% alpha), visible on light and dark wallpapers.</summary>
    public const byte PillAlpha = 0xE6;

    public static Border Create()
    {
        return new Border
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Visible,
            Background = CreateBrush(dark: true),
            CornerRadius = new CornerRadius(PillThick / 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    public static SolidColorBrush CreateBrush(bool dark) =>
        new(Color.FromArgb(PillAlpha, 0xB8, 0xB8, 0xB8));

    public static void ApplyLayout(Border indicator, WindowDockEdge edge, double edgeInset = 0)
    {
        indicator.Width = edge is WindowDockEdge.Left or WindowDockEdge.Right
            ? PillThick
            : PillLong;
        indicator.Height = edge is WindowDockEdge.Left or WindowDockEdge.Right
            ? PillLong
            : PillThick;
        indicator.Margin = edge switch
        {
            WindowDockEdge.Left => new Thickness(edgeInset, 0, 0, 0),
            WindowDockEdge.Right => new Thickness(0, 0, edgeInset, 0),
            WindowDockEdge.Top => new Thickness(0, edgeInset, 0, 0),
            WindowDockEdge.Bottom => new Thickness(0, 0, 0, edgeInset),
            _ => new Thickness(0),
        };
        indicator.HorizontalAlignment = edge switch
        {
            WindowDockEdge.Left => HorizontalAlignment.Left,
            WindowDockEdge.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        indicator.VerticalAlignment = edge switch
        {
            WindowDockEdge.Top => VerticalAlignment.Top,
            WindowDockEdge.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        indicator.CornerRadius = new CornerRadius(PillThick / 2);
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
