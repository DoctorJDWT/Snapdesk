using System.Collections.Generic;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>Persist and restore screen position for profile dialogs (same <c>WxH+X+Y</c> format as the widget).</summary>
internal static class SecondaryWindowPlacement
{
    private static readonly SettingsService Settings = new();

    public const string ProfileEditor = "profile_editor";
    public const string ProfileDelete = "profile_delete";
    public const string ProfileActions = "profile_actions";
    public const string UninstallConfirm = "uninstall_confirm";

    public static void Apply(Window window, string dialogId, int width, int height, bool resizable = false)
    {
        try
        {
            var appWindow = GetAppWindow(window);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = resizable;
            }

            var settings = Settings.Load();
            var key = SettingsService.DialogGeometryKey(dialogId);
            if (settings.TryGetValue(key, out var el)
                && el.ValueKind == System.Text.Json.JsonValueKind.String
                && SettingsService.TryParseTkGeometry(el.GetString()) is { } saved)
            {
                var (_, _, x, y) = WindowGeometryHelper.ClampToVirtualScreen(
                    width,
                    height,
                    saved.X,
                    saved.Y,
                    minWidth: width,
                    minHeight: height);
                if (WindowGeometryHelper.IntersectsAnyWorkArea(x, y, width, height))
                {
                    appWindow.Resize(new SizeInt32(width, height));
                    appWindow.Move(new PointInt32(x, y));
                    return;
                }
            }

            WindowGeometryHelper.CenterOnPrimaryWorkArea(appWindow, width, height);
        }
        catch
        {
            // ignore placement failures
        }

        try
        {
            WindowChromeHelper.ApplyRoundedCorners(window);
        }
        catch
        {
            // ignore DWM failures on older builds
        }
    }

    public static void TryPersist(Window window, string dialogId)
    {
        try
        {
            var appWindow = GetAppWindow(window);
            var size = appWindow.Size;
            var pos = appWindow.Position;
            if (size.Width < 1 || size.Height < 1)
            {
                return;
            }

            var geom = SettingsService.FormatTkGeometry(size.Width, size.Height, pos.X, pos.Y);
            Settings.MergeAndSave(new Dictionary<string, object?>
            {
                [SettingsService.DialogGeometryKey(dialogId)] = geom,
            });
        }
        catch
        {
            // ignore persistence failures
        }
    }

    private static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
