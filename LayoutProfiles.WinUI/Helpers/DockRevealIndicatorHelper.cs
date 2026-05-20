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

    public static void ApplyLayout(Border indicator, WindowDockEdge edge)
    {
        indicator.HorizontalAlignment = HorizontalAlignment.Stretch;
        indicator.VerticalAlignment = VerticalAlignment.Stretch;
        indicator.Margin = new Thickness(0);
        indicator.ClearValue(FrameworkElement.WidthProperty);
        indicator.ClearValue(FrameworkElement.HeightProperty);
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
