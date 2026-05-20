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
    public const double PillLong = 24;
    public const double PillThick = 3.5;

    public static Border Create()
    {
        return new Border
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Background = CreateBrush(dark: true),
            CornerRadius = new CornerRadius(PillThick / 2),
        };
    }

    public static SolidColorBrush CreateBrush(bool dark) =>
        new(dark
            ? Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xCC, 0x22, 0x22, 0x22));

    public static void ApplyLayout(Border indicator, WindowDockEdge edge)
    {
        indicator.HorizontalAlignment = HorizontalAlignment.Center;
        indicator.VerticalAlignment = VerticalAlignment.Center;
        indicator.Margin = new Thickness(0);
        indicator.Width = PillLong;
        indicator.Height = PillThick;

        switch (edge)
        {
            case WindowDockEdge.Top:
                indicator.VerticalAlignment = VerticalAlignment.Bottom;
                indicator.Margin = new Thickness(0);
                break;
            case WindowDockEdge.Bottom:
                indicator.VerticalAlignment = VerticalAlignment.Top;
                indicator.Margin = new Thickness(0);
                break;
            case WindowDockEdge.Left:
                indicator.HorizontalAlignment = HorizontalAlignment.Right;
                indicator.VerticalAlignment = VerticalAlignment.Center;
                indicator.Width = PillThick;
                indicator.Height = PillLong;
                indicator.Margin = new Thickness(0);
                break;
            case WindowDockEdge.Right:
                indicator.HorizontalAlignment = HorizontalAlignment.Left;
                indicator.VerticalAlignment = VerticalAlignment.Center;
                indicator.Width = PillThick;
                indicator.Height = PillLong;
                indicator.Margin = new Thickness(0);
                break;
        }
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
