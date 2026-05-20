using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

internal static class WindowChromeHelper
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const nuint MinTrackSubclassId = 0x5A4D;

    private static readonly ConcurrentDictionary<nint, (int Width, int Height)> MinTrackSizes = new();
    private static readonly SubclassProc MinTrackSubclassProcImpl = MinTrackSubclassProc;

    private delegate nint SubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData);
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaVisibleFrameBorderThickness = 37;
    private const int DwmwcpDoNotRound = 1;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);

    public static void ApplySquareCorners(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var preference = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    /// <summary>
    /// WinUI still reserves a title-bar band after SetBorderAndTitleBar; collapse it and clear colors.
    /// </summary>
    public static void ApplyBorderlessTitleBar(AppWindow appWindow)
    {
        try
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;

            var transparent = Colors.Transparent;
            titleBar.BackgroundColor = transparent;
            titleBar.InactiveBackgroundColor = transparent;
            titleBar.ForegroundColor = transparent;
            titleBar.InactiveForegroundColor = transparent;
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.ButtonInactiveBackgroundColor = transparent;
            titleBar.ButtonHoverBackgroundColor = transparent;
            titleBar.ButtonPressedBackgroundColor = transparent;
            titleBar.ButtonForegroundColor = transparent;
            titleBar.ButtonInactiveForegroundColor = transparent;
            titleBar.ButtonHoverForegroundColor = transparent;
            titleBar.ButtonPressedForegroundColor = transparent;
        }
        catch
        {
            // ignore on older WinAppSDK / OS builds
        }
    }

    /// <summary>
    /// Match non-client caption/border to widget chrome so no light strip shows at the top.
    /// </summary>
    public static void ApplyNonClientFrame(IntPtr hwnd, bool dark)
    {
        var color = dark ? 0x001E1E1E : 0x00F5F7FA; // COLORREF BGR
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref color, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(int));

        var noBorder = 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaVisibleFrameBorderThickness, ref noBorder, sizeof(int));
    }

    public static void ApplyDarkNonClientFrame(IntPtr hwnd) => ApplyNonClientFrame(hwnd, dark: true);

    /// <summary>
    /// Win32 minimum resize track (outer window pixels). <paramref name="minClientWidth"/> /
    /// <paramref name="minClientHeight"/> are AppWindow client-area minimums.
    /// </summary>
    public static void ApplyMinimumTrackSize(Window window, int minClientWidth, int minClientHeight)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var track = WindowGeometryHelper.ClientAreaToWindowSize(hwnd, minClientWidth, minClientHeight);
        MinTrackSizes[hwnd] = track;
        _ = SetWindowSubclass(hwnd, MinTrackSubclassProcImpl, MinTrackSubclassId, 0);
    }

    public static void RemoveMinimumTrackSize(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        MinTrackSizes.TryRemove(hwnd, out _);
        _ = RemoveWindowSubclass(hwnd, MinTrackSubclassProcImpl, MinTrackSubclassId);
    }

    private static nint MinTrackSubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData)
    {
        if (uMsg == WmGetMinMaxInfo
            && MinTrackSizes.TryGetValue(hWnd, out var min)
            && lParam != 0)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.PtMinTrackSize.X = min.Width;
            info.PtMinTrackSize.Y = min.Height;
            Marshal.StructureToPtr(info, lParam, false);
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 PtReserved;
        public Point32 PtMaxSize;
        public Point32 PtMaxPosition;
        public Point32 PtMinTrackSize;
        public Point32 PtMaxTrackSize;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass,
        nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    public static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    /// <summary>
    /// True when the pointer hit a control that should keep its own pointer behavior
    /// (profile chips, scroll thumb, text fields) — not empty chrome or grid padding.
    /// </summary>
    public static bool IsDescendantOfInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or TextBox or HyperlinkButton or ScrollBar)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
