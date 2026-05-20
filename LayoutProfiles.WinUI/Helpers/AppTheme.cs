using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace LayoutProfiles.WinUI.Helpers;

internal readonly struct WidgetPalette
{
    public SolidColorBrush Background { get; init; }
    public SolidColorBrush Border { get; init; }
    public SolidColorBrush Muted { get; init; }
    public SolidColorBrush Accent { get; init; }
    public SolidColorBrush AddChipBackground { get; init; }
}

internal static class AppTheme
{
    public const string WidgetBackground = "WidgetBackgroundBrush";
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
                Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)),
                Border = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x2E, 0x32, 0x38)),
                Muted = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x9A, 0xA3, 0xAD)),
                Accent = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x4C, 0x8B, 0xF5)),
                AddChipBackground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x48, 0x4C, 0x54)),
            };
        }

        return new WidgetPalette
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF0, 0xF2, 0xF5)),
            Border = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xC8, 0xD0, 0xD9)),
            Muted = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x47, 0x51, 0x5C)),
            Accent = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x09, 0x69, 0xDA)),
            AddChipBackground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xDC, 0xE0, 0xE6)),
        };
    }

    private static void SyncAppResources(WidgetPalette palette)
    {
        var app = Application.Current;
        app.Resources[WidgetBackground] = palette.Background;
        app.Resources[WidgetBorder] = palette.Border;
        app.Resources[WidgetMuted] = palette.Muted;
        app.Resources[WidgetAccent] = palette.Accent;
        app.Resources[WidgetAddChipBackground] = palette.AddChipBackground;
    }
}
