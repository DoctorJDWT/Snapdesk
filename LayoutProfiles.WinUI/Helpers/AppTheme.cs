using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace LayoutProfiles.WinUI.Helpers;

internal readonly struct WidgetPalette
{
    public SolidColorBrush Background { get; init; }
    public SolidColorBrush OpaqueChrome { get; init; }
    public SolidColorBrush Primary { get; init; }
    public SolidColorBrush Border { get; init; }
    public SolidColorBrush Muted { get; init; }
    public SolidColorBrush Accent { get; init; }
    public SolidColorBrush AddChipBackground { get; init; }
}

internal static class AppTheme
{
    /// <summary>Panel veil when acrylic is off (~40% tint). With acrylic, use transparent overlay instead.</summary>
    private const byte FallbackPanelAlpha = 0x66;

    public const string WidgetBackground = "WidgetBackgroundBrush";
    public const string WidgetOpaqueChrome = "WidgetOpaqueChromeBrush";
    public const string WidgetPrimary = "WidgetPrimaryBrush";
    public const string WidgetBorder = "WidgetBorderBrush";
    public const string WidgetMuted = "WidgetMutedBrush";
    public const string WidgetAccent = "WidgetAccentBrush";
    public const string WidgetAddChipBackground = "WidgetAddChipBackgroundBrush";

    private static ThemePreference _preference = ThemePreference.Dark;
    private static WidgetPalette _palette;
    private static UISettings? _uiSettings;
    private static bool _systemListenerAttached;

    public static ThemePreference Preference => _preference;

    public static bool IsDark => ResolveDark();

    public static WidgetPalette Palette => _palette;

    public static event Action? ResolvedThemeChanged;

    public static void Initialize(ThemePreference preference)
    {
        _preference = preference;
        EnsureSystemListener();
        ApplyResolved();
    }

    public static void SetPreference(ThemePreference preference)
    {
        _preference = preference;
        EnsureSystemListener();
        ApplyResolved();
    }

    public static string ToSettingsValue(ThemePreference preference) =>
        preference switch
        {
            ThemePreference.Light => "light",
            ThemePreference.System => "system",
            _ => "dark",
        };

    public static ThemePreference FromSettingsValue(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemePreference.Light,
            "system" => ThemePreference.System,
            _ => ThemePreference.Dark,
        };

    private static void EnsureSystemListener()
    {
        if (_systemListenerAttached)
        {
            return;
        }

        _systemListenerAttached = true;
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
    }

    private static void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        if (_preference != ThemePreference.System)
        {
            return;
        }

        ApplyResolved();
    }

    private static bool ResolveDark() =>
        _preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.System => IsSystemDarkMode(),
            _ => true,
        };

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
            {
                return i == 0;
            }
        }
        catch
        {
            // ignore
        }

        return true;
    }

    private static void ApplyResolved()
    {
        var dark = ResolveDark();
        try
        {
            Application.Current.RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        catch (Exception ex)
        {
            // Unpackaged WinUI can reject Application.RequestedTheme during startup (COM 0x80131515).
            StartupTrace.Write($"Application.RequestedTheme skipped: {ex.Message}");
        }

        _palette = CreatePalette(dark);
        SyncAppResources(_palette);
        ResolvedThemeChanged?.Invoke();
    }

    private static WidgetPalette CreatePalette(bool dark)
    {
        if (dark)
        {
            return new WidgetPalette
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(FallbackPanelAlpha, 0x28, 0x28, 0x28)),
                OpaqueChrome = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x28, 0x28)),
                Primary = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                Border = new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Muted = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xB3, 0xB3, 0xB3)),
                Accent = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x4C, 0x8B, 0xF5)),
                AddChipBackground = new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            };
        }

        return new WidgetPalette
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(FallbackPanelAlpha, 0xF0, 0xF0, 0xF0)),
            OpaqueChrome = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xEF, 0xEF, 0xEF)),
            Primary = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x33, 0x33, 0x33)),
            Border = new SolidColorBrush(ColorHelper.FromArgb(0x26, 0x00, 0x00, 0x00)),
            Muted = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x47, 0x51, 0x5C)),
            Accent = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x09, 0x69, 0xDA)),
            AddChipBackground = new SolidColorBrush(ColorHelper.FromArgb(0x1A, 0x00, 0x00, 0x00)),
        };
    }

    private static void SyncAppResources(WidgetPalette palette)
    {
        var app = Application.Current;
        app.Resources[WidgetBackground] = palette.Background;
        app.Resources[WidgetOpaqueChrome] = palette.OpaqueChrome;
        app.Resources[WidgetPrimary] = palette.Primary;
        app.Resources[WidgetBorder] = palette.Border;
        app.Resources[WidgetMuted] = palette.Muted;
        app.Resources[WidgetAccent] = palette.Accent;
        app.Resources[WidgetAddChipBackground] = palette.AddChipBackground;
    }
}
