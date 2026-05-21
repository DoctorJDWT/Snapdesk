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
    private const int DwmwcpRound = 2;
    private const int GwlStyle = -16;
    private const int GwlExstyle = -20;
    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsBorder = 0x00800000;
    private const nint WsDlgFrame = 0x00400000;
    private const nint WsExAppwindow = 0x00040000;
    private const nint WsExToolwindow = 0x00000080;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);

    public static void ApplyRoundedCorners(Window window) =>
        ApplyRoundedCorners(WindowNative.GetWindowHandle(window));

    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        var preference = DwmwcpRound;
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
    /// Use on opaque dialog windows only — on the glass widget this draws a themed outline ring.
    /// </summary>
    public static void ApplyNonClientFrame(IntPtr hwnd, bool dark)
    {
        var color = dark ? 0x00282828 : 0x00F0F0F0; // COLORREF BGR
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref color, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(int));
        ApplyZeroVisibleFrameBorder(hwnd);
    }

    /// <summary>
    /// Borderless glass widget: transparent DWM caption/border so rounded acrylic has no dark/white halo.
    /// </summary>
    public static void ApplyGlassNonClientFrame(IntPtr hwnd)
    {
        var transparent = 0x00000000;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref transparent, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref transparent, sizeof(int));
        ApplyZeroVisibleFrameBorder(hwnd);
    }

    public static void ApplyDarkNonClientFrame(IntPtr hwnd) => ApplyNonClientFrame(hwnd, dark: true);

    /// <summary>Remove visible DWM frame only — no caption/border tint (for tiny overlays).</summary>
    public static void ApplyZeroVisibleFrameBorder(IntPtr hwnd)
    {
        var noBorder = 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaVisibleFrameBorderThickness, ref noBorder, sizeof(int));
    }

    /// <summary>Keep utility windows out of the taskbar and Alt-Tab when they hide/show.</summary>
    public static void ApplyNoTaskbarToolWindow(IntPtr hwnd)
    {
        var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
        exStyle |= WsExToolwindow;
        exStyle &= ~WsExAppwindow;
        SetWindowLongPtr(hwnd, GwlExstyle, exStyle);
        _ = SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>
    /// Strip Win32 caption/thick-frame so tiny overlay windows do not show a collapsed title-bar band.
    /// Do not call <see cref="ApplyBorderlessTitleBar"/> on these windows — that path reserves the band.
    /// </summary>
    public static void ApplyOverlayWindowChrome(IntPtr hwnd)
    {
        ApplyZeroVisibleFrameBorder(hwnd);
        ApplyNoTaskbarToolWindow(hwnd);

        var style = GetWindowLongPtr(hwnd, GwlStyle);
        style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
        SetWindowLongPtr(hwnd, GwlStyle, style);

        // Do not use DwmExtendFrameIntoClientArea(-1) on tiny overlays — it draws glass
        // strips on different edges depending on dock side (the white/black bar artifacts).

        _ = SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>Clip the HWND to a capsule so no rectangular frame can bleed on any edge.</summary>
    public static void ApplyPillWindowRegion(IntPtr hwnd, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var corner = Math.Max(1, Math.Min(width, height));
        var rgn = CreateRoundRectRgn(0, 0, width, height, corner, corner);
        if (rgn == 0)
        {
            return;
        }

        _ = SetWindowRgn(hwnd, rgn, true);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>
    /// Present a secondary WinUI <see cref="Window"/> above the tool-window widget.
    /// Do not set GWLP_HWNDPARENT — that breaks WinUI top-level windows (blank/hidden dialogs).
    /// </summary>
    public static void PresentModal(Window dialog, Window? owner)
    {
        _ = owner;
        try
        {
            if (GetAppWindow(dialog).Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
            }
        }
        catch
        {
            // ignore presenter configuration failures
        }

        void OnActivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                return;
            }

            dialog.Activated -= OnActivated;
            BringDialogToFront(dialog);
        }

        dialog.Activated += OnActivated;
        dialog.Activate();
        dialog.DispatcherQueue.TryEnqueue(() => BringDialogToFront(dialog));
    }

    public static void ReleaseModal(Window dialog)
    {
        try
        {
            if (GetAppWindow(dialog).Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = false;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void BringDialogToFront(Window dialog)
    {
        try
        {
            dialog.Activate();
            var dialogHwnd = WindowNative.GetWindowHandle(dialog);
            _ = BringWindowToTop(dialogHwnd);
            _ = SetForegroundWindow(dialogHwnd);
        }
        catch
        {
            // ignore Win32 failures
        }
    }

    private static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

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
