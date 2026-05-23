using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace LayoutProfiles.WinUI.Helpers;

public static class WindowGeometryHelper
{
    /// <summary>Widget windows larger than this are treated as corrupt saves (e.g. full monitor size).</summary>
    public const int MaxWidgetWidth = 960;

    public const int MaxWidgetHeight = 720;

    public static bool IsPlausibleWidgetSize(int width, int height, int minWidth, int minHeight) =>
        width >= minWidth
        && height >= minHeight
        && width <= MaxWidgetWidth
        && height <= MaxWidgetHeight;

    public static (int Width, int Height) ClampWidgetSize(int width, int height, int minWidth, int minHeight)
    {
        width = Math.Clamp(width, minWidth, MaxWidgetWidth);
        height = Math.Clamp(height, minHeight, MaxWidgetHeight);
        return (width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool TryGetWindowRect(nint hwnd, out RectInt32 rect)
    {
        rect = default;
        if (hwnd == 0)
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var native))
        {
            return false;
        }

        var w = native.Right - native.Left;
        var h = native.Bottom - native.Top;
        if (w < 1 || h < 1)
        {
            return false;
        }

        rect = new RectInt32(native.Left, native.Top, w, h);
        return true;
    }
    public static bool IntersectsAnyWorkArea(int x, int y, int width, int height)
    {
        var window = new RectInt32(x, y, width, height);
        try
        {
            foreach (var display in DisplayArea.FindAll())
            {
                if (RectsIntersect(window, display.WorkArea))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("WindowGeometryHelper.IntersectsAnyWorkArea", $"ignored: {ex.Message}"); // ignore
            return true;
        }

        return false;
    }

    public static void CenterOnPrimaryWorkArea(AppWindow appWindow, int width, int height)
    {
        var display = DisplayArea.GetFromPoint(new PointInt32(0, 0), DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private static bool RectsIntersect(RectInt32 a, RectInt32 b)
    {
        var aRight = a.X + a.Width;
        var aBottom = a.Y + a.Height;
        var bRight = b.X + b.Width;
        var bBottom = b.Y + b.Height;
        return a.X < bRight && aRight > b.X && a.Y < bBottom && aBottom > b.Y;
    }

    public static (int Width, int Height, int X, int Y) ClampToVirtualScreen(
        int width,
        int height,
        int x,
        int y,
        int minWidth = 280,
        int minHeight = 200)
    {
        width = Math.Max(minWidth, width);
        height = Math.Max(minHeight, height);

        if (!TryGetVirtualScreen(out var vl, out var vt, out var vrx, out var vby))
        {
            vl = 0;
            vt = 0;
            vrx = 1920;
            vby = 1080;
        }

        var vw = Math.Max(1, vrx - vl);
        var vh = Math.Max(1, vby - vt);
        width = Math.Min(width, vw);
        height = Math.Min(height, vh);
        x = Math.Max(vl, Math.Min(x, vrx - width));
        y = Math.Max(vt, Math.Min(y, vby - height));
        return (width, height, x, y);
    }

    private static bool TryGetVirtualScreen(out int left, out int top, out int right, out int bottom)
    {
        left = GetSystemMetrics(76);
        top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        right = left + width;
        bottom = top + height;
        return width > 0 && height > 0;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    /// <summary>
    /// Convert a desired client-area size to the corresponding outer window size for WM_GETMINMAXINFO.
    /// </summary>
    public static (int Width, int Height) ClientAreaToWindowSize(nint hwnd, int clientWidth, int clientHeight)
    {
        if (hwnd == 0 || clientWidth < 1 || clientHeight < 1)
        {
            return (Math.Max(1, clientWidth), Math.Max(1, clientHeight));
        }

        var rect = new NativeRect
        {
            Left = 0,
            Top = 0,
            Right = clientWidth,
            Bottom = clientHeight,
        };
        var style = (uint)GetWindowLong(hwnd, GwlStyle);
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        if (!AdjustWindowRectEx(ref rect, style, false, exStyle))
        {
            return (clientWidth, clientHeight);
        }

        return (Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AdjustWindowRectEx(
        ref NativeRect lpRect,
        uint dwStyle,
        bool bMenu,
        int dwExStyle);
}
