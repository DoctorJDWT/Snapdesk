using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Hold left-click on empty chrome and move past a small threshold to drag the window.
/// Uses a per-frame cursor poll so ScrollViewer pointer capture cannot block moves.
/// </summary>
internal sealed class WindowPositionController
{
    private const int DragThresholdPixels = 2;
    private const int VkLButton = 0x01;

    private readonly Window _window;
    private readonly UIElement _dragSurface;
    private readonly Func<AppWindow> _getAppWindow;
    private readonly Action? _onPositionChanged;

    private bool _tracking;
    private bool _dragging;
    private Pointer? _activePointer;
    private PointInt32 _windowStart;
    private NativePoint _cursorStart;

    public WindowPositionController(
        Window window,
        UIElement dragSurface,
        Func<AppWindow> getAppWindow,
        Action? onPositionChanged = null)
    {
        _window = window;
        _dragSurface = dragSurface;
        _getAppWindow = getAppWindow;
        _onPositionChanged = onPositionChanged;

        _dragSurface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        _dragSurface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        _dragSurface.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        _dragSurface.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_dragSurface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (WindowChromeHelper.IsDescendantOfInteractiveControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginTracking(e.Pointer);
    }

    private void BeginTracking(Pointer pointer)
    {
        EndTracking();

        _tracking = true;
        _dragging = false;
        _activePointer = pointer;

        try
        {
            _windowStart = _getAppWindow().Position;
        }
        catch
        {
            _windowStart = new PointInt32(0, 0);
        }

        _ = NativeMethods.GetCursorPos(out _cursorStart);
        NativeMethods.SetCapture(WindowHandle);
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (!_tracking)
        {
            return;
        }

        if (!IsLeftButtonDown())
        {
            EndTracking();
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cur))
        {
            return;
        }

        var dx = cur.X - _cursorStart.X;
        var dy = cur.Y - _cursorStart.Y;
        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThresholdPixels && Math.Abs(dy) < DragThresholdPixels)
            {
                return;
            }

            _dragging = true;
        }

        try
        {
            _getAppWindow().Move(new PointInt32(_windowStart.X + dx, _windowStart.Y + dy));
            _onPositionChanged?.Invoke();
        }
        catch
        {
            // ignore
        }
    }

    private static bool IsLeftButtonDown() =>
        (NativeMethods.GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_tracking || _activePointer is null || e.Pointer != _activePointer)
        {
            return;
        }

        EndTracking();
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_tracking && _activePointer is not null && e.Pointer == _activePointer)
        {
            EndTracking();
        }
    }

    private void EndTracking()
    {
        if (!_tracking)
        {
            return;
        }

        _tracking = false;
        _dragging = false;
        _activePointer = null;
        CompositionTarget.Rendering -= OnCompositionRendering;
        NativeMethods.ReleaseCapture();
        _onPositionChanged?.Invoke();
    }

    private IntPtr WindowHandle => WindowNative.GetWindowHandle(_window);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
    }
}
