// MainWindow.Theme.cs — Theme preference persistence, theme menu check synchronization,
// widget chrome theming, version menu item, and resolved-theme-changed handler
// extracted from MainWindow.xaml.cs.

using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

#nullable enable

namespace LayoutProfiles.WinUI;

public sealed partial class MainWindow
{
    private MenuFlyoutItem CreateThemeMenuItem(string label, ThemePreference preference)
    {
        var item = new MenuFlyoutItem { Text = label, Tag = preference };
        item.Click += (_, _) => SetThemePreference(preference);
        return item;
    }

    private static MenuFlyoutItem CreateVersionMenuItem() =>
        new()
        {
            Text = $"Version {AppVersion.Display}",
            IsEnabled = false,
            Opacity = 0.75,
            Foreground = AppTheme.Palette.Muted,
        };

    private void SyncVersionMenuItem()
    {
        if (_versionMenuItem is null)
        {
            return;
        }

        _versionMenuItem.Foreground = AppTheme.Palette.Muted;
    }

    private void SetThemePreference(ThemePreference preference)
    {
        AppTheme.SetPreference(preference);
        var themeValue = AppTheme.ToSettingsValue(preference);
        _settings.MergeAndSave(new Dictionary<string, object?>
        {
            ["gui_theme"] = themeValue,
            ["gui_dark_mode"] = preference == ThemePreference.Dark
                || (preference == ThemePreference.System && AppTheme.IsDark),
        });
        _settingsCache = _settings.Load();
        SyncThemeMenuChecks();
        ApplyWidgetChromeFromTheme();
        StartupTrace.Write($"Theme set to {themeValue} (resolved dark={AppTheme.IsDark})");
    }

    private void SyncThemeMenuChecks()
    {
        if (_themeDarkItem is null)
        {
            return;
        }

        var pref = AppTheme.Preference;
        SetThemeItemCheck(_themeDarkItem, pref == ThemePreference.Dark);
        SetThemeItemCheck(_themeLightItem!, pref == ThemePreference.Light);
        SetThemeItemCheck(_themeSystemItem!, pref == ThemePreference.System);
    }

    private static void SetThemeItemCheck(MenuFlyoutItem item, bool selected)
    {
        item.Icon = selected
            ? new FontIcon { Glyph = ThemeCheckGlyph, FontSize = 14 }
            : null;
    }

    private void OnResolvedThemeChanged()
    {
        DispatcherQueue.TryEnqueue(ApplyWidgetChromeFromTheme);
    }

    private void ApplyWidgetChromeFromTheme()
    {
        var palette = AppTheme.Palette;
        var elementTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        // Parents transparent; acrylic carries the glass. No extra 60% veil on top (that hid the effect).
        _contentRoot.Background = transparent;
        _chromeHost.Background = _glassBackdrop?.IsEnabled == true ? transparent : palette.Background;
        _rootGrid.Background = transparent;
        _profileScrollViewer.Background = transparent;
        _profileChipHost.Background = transparent;
        _contentRoot.RequestedTheme = elementTheme;
        _chromeHost.RequestedTheme = elementTheme;
        _rootGrid.RequestedTheme = elementTheme;
        _profileScrollViewer.RequestedTheme = elementTheme;
        _statusText.Foreground = palette.Muted;
        _addChip.Background = transparent;
        if (_addChipFace is not null)
        {
            _addChipFace.Background = palette.AddChipBackground;
        }

        _glassBackdrop?.ApplyTheme(AppTheme.IsDark);
        if (_addChipIcon is not null)
        {
            _addChipIcon.Foreground = palette.Accent;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyGlassNonClientFrame(hwnd);
            WindowChromeHelper.ApplyNoTaskbarToolWindow(hwnd);
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("MainWindow.ApplyNoTaskbarToolWindow", $"ignored: {ex.Message}"); // ignore
        }
    }
}
