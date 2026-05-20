using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Synchronously paints newly exposed client areas during live resize.
/// WinUI can lag the native resize loop for tiny borderless windows, briefly exposing
/// the desktop at the bottom/right edge before the compositor presents the next frame.
/// </summary>
internal static class WindowResizePaintHelper
{
    private const uint WmEraseBackground = 0x0014;
    private const uint WmSize = 0x0005;
    private const uint WmSizing = 0x0214;
    private const nuint ResizeFillSubclassId = 0x5A4E;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaVisibleFrameBorderThickness = 37;
    private const int DwmwcpDoNotRound = 1;

    private static readonly ConcurrentDictionary<nint, int> FillColors = new();
    private static readonly ConcurrentDictionary<nint, (int Width, int Height)> LastClientSizes = new();
    private static readonly ResizeSubclassProc SubclassProcImpl = OnSubclassMessage;

    private delegate nint ResizeSubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData);

    public static void Apply(Window window, Color color)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        FillColors[hwnd] = ToColorRef(color);

        if (GetClientRect(hwnd, out var rect))
        {
            LastClientSizes[hwnd] = (Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        _ = SetWindowSubclass(hwnd, SubclassProcImpl, ResizeFillSubclassId, 0);
    }

    public static void Remove(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        FillColors.TryRemove(hwnd, out _);
        LastClientSizes.TryRemove(hwnd, out _);
        _ = RemoveWindowSubclass(hwnd, SubclassProcImpl, ResizeFillSubclassId);
    }

    private static nint OnSubclassMessage(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData)
    {
        if (!FillColors.TryGetValue(hWnd, out var colorRef))
        {
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        if (uMsg == WmEraseBackground && wParam != 0)
        {
            FillClientRect(wParam, hWnd, colorRef);
            return 1;
        }

        if (uMsg is WmSizing or WmSize)
        {
            ApplySquareCorners(hWnd);
            var result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            FillExpandedClientBands(hWnd, colorRef);
            return result;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static void ApplySquareCorners(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var preference = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));

        var noBorder = 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaVisibleFrameBorderThickness, ref noBorder, sizeof(int));
    }

    private static void FillClientRect(nint hdc, nint hwnd, int colorRef)
    {
        if (!GetClientRect(hwnd, out var rect))
        {
            return;
        }

        FillSolidRect(hdc, ref rect, colorRef);
    }

    private static void FillExpandedClientBands(nint hwnd, int colorRef)
    {
        if (!GetClientRect(hwnd, out var rect))
        {
            return;
        }

        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        LastClientSizes.TryGetValue(hwnd, out var previous);
        LastClientSizes[hwnd] = (width, height);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var hdc = GetDC(hwnd);
        if (hdc == 0)
        {
            return;
        }

        try
        {
            if (width > previous.Width && previous.Width > 0)
            {
                var rightBand = new NativeRect
                {
                    Left = previous.Width,
                    Top = 0,
                    Right = width,
                    Bottom = height,
                };
                FillSolidRect(hdc, ref rightBand, colorRef);
            }

            if (height > previous.Height && previous.Height > 0)
            {
                var bottomBand = new NativeRect
                {
                    Left = 0,
                    Top = previous.Height,
                    Right = width,
                    Bottom = height,
                };
                FillSolidRect(hdc, ref bottomBand, colorRef);
            }
        }
        finally
        {
            _ = ReleaseDC(hwnd, hdc);
        }
    }

    private static void FillSolidRect(nint hdc, ref NativeRect rect, int colorRef)
    {
        var brush = CreateSolidBrush(colorRef);
        if (brush == 0)
        {
            return;
        }

        try
        {
            _ = FillRect(hdc, ref rect, brush);
        }
        finally
        {
            _ = DeleteObject(brush);
        }
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        ResizeSubclassProc pfnSubclass,
        nuint uIdSubclass,
        nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        ResizeSubclassProc pfnSubclass,
        nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hDC, ref NativeRect lprc, nint hbr);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(int colorRef);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);
}
