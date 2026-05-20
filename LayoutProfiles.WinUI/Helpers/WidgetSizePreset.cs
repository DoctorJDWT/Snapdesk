namespace LayoutProfiles.WinUI.Helpers;

/// <summary>Fixed gumball grid size for the widget window (columns × rows).</summary>
public enum WidgetSizePreset
{
    OneByOne,
    OneByTwo,
    OneByThree,
    OneByFour,
    TwoByOne,
    ThreeByOne,
    FourByOne,
    /// <summary>One column; height fits every gumball (vertical).</summary>
    OneByFit,
    /// <summary>One row; width fits every gumball (horizontal).</summary>
    FitX1,
    TwoByFit,
    /// <summary>Use saved <c>window_geometry</c> / manual resize.</summary>
    Custom,
}

public static class WidgetSizePresets
{
    // Tuned client widths — set from window_geometry after you pick a good size in each layout.
    // Fixed presets adjust width only; height stays whatever you have when switching.

    public const int OneByOneClientWidthPx = 96;
    public const int OneByTwoClientWidthPx = 208;
    public const int OneByThreeClientWidthPx = 262;
    public const int TwoByOneClientWidthPx = 118;

    public static string ToSettingsValue(WidgetSizePreset preset) =>
        preset switch
        {
            WidgetSizePreset.OneByTwo => "1x2",
            WidgetSizePreset.OneByThree => "1x3",
            WidgetSizePreset.OneByFour => "1x4",
            WidgetSizePreset.TwoByOne => "2x1",
            WidgetSizePreset.ThreeByOne => "3x1",
            WidgetSizePreset.FourByOne => "4x1",
            WidgetSizePreset.OneByFit => "1xfit",
            WidgetSizePreset.FitX1 => "fitx1",
            WidgetSizePreset.TwoByFit => "2xfit",
            WidgetSizePreset.Custom => "custom",
            _ => "1x1",
        };

    public static WidgetSizePreset FromSettingsValue(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "1x2" => WidgetSizePreset.OneByTwo,
            "1x3" => WidgetSizePreset.OneByThree,
            "1x4" => WidgetSizePreset.OneByFour,
            "2x1" => WidgetSizePreset.TwoByOne,
            "3x1" => WidgetSizePreset.ThreeByOne,
            "4x1" => WidgetSizePreset.FourByOne,
            "1xfit" => WidgetSizePreset.OneByFit,
            "fitx1" => WidgetSizePreset.FitX1,
            "2xfit" => WidgetSizePreset.TwoByFit,
            "custom" => WidgetSizePreset.Custom,
            _ => WidgetSizePreset.OneByOne,
        };

    public static bool IsFit(WidgetSizePreset preset) =>
        preset is WidgetSizePreset.OneByFit or WidgetSizePreset.FitX1 or WidgetSizePreset.TwoByFit;

    public static bool IsVerticalFit(WidgetSizePreset preset) => preset == WidgetSizePreset.OneByFit;

    public static bool IsHorizontalFit(WidgetSizePreset preset) => preset == WidgetSizePreset.FitX1;

    public static string GetMenuLabel(WidgetSizePreset preset) =>
        preset switch
        {
            WidgetSizePreset.OneByTwo => "1×2",
            WidgetSizePreset.OneByThree => "1×3",
            WidgetSizePreset.OneByFour => "1×4",
            WidgetSizePreset.TwoByOne => "2×1",
            WidgetSizePreset.ThreeByOne => "3×1",
            WidgetSizePreset.FourByOne => "4×1",
            WidgetSizePreset.OneByFit => "1×fit",
            WidgetSizePreset.FitX1 => "fit×1",
            WidgetSizePreset.TwoByFit => "2×fit",
            WidgetSizePreset.Custom => "Custom",
            _ => "1×1",
        };

    /// <summary>Hand-tuned width for a fixed grid preset; false until that layout has been calibrated.</summary>
    public static bool TryGetTunedClientWidth(WidgetSizePreset preset, out int width)
    {
        switch (preset)
        {
            case WidgetSizePreset.OneByOne:
                width = OneByOneClientWidthPx;
                return true;
            case WidgetSizePreset.OneByTwo:
                width = OneByTwoClientWidthPx;
                return true;
            case WidgetSizePreset.OneByThree:
                width = OneByThreeClientWidthPx;
                return true;
            case WidgetSizePreset.TwoByOne:
                width = TwoByOneClientWidthPx;
                return true;
            default:
                width = 0;
                return false;
        }
    }

    public static (int Columns, int Rows) GetGrid(WidgetSizePreset preset, int chipCount)
    {
        chipCount = Math.Max(1, chipCount);
        return preset switch
        {
            WidgetSizePreset.OneByTwo => (1, 2),
            WidgetSizePreset.OneByThree => (1, 3),
            WidgetSizePreset.OneByFour => (1, 4),
            WidgetSizePreset.TwoByOne => (2, 1),
            WidgetSizePreset.ThreeByOne => (3, 1),
            WidgetSizePreset.FourByOne => (4, 1),
            WidgetSizePreset.OneByFit => (1, chipCount),
            WidgetSizePreset.FitX1 => (chipCount, 1),
            WidgetSizePreset.TwoByFit => (2, (chipCount + 1) / 2),
            WidgetSizePreset.Custom => (1, 1),
            _ => (1, 1),
        };
    }
}
