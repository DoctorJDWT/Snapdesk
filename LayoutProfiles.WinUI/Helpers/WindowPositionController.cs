using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
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
    private const int DockThresholdPixels = 18;
    private const int AutoHidePeekPixels = 4;
    private const int AutoHideRevealPixels = 6;
    private const int AutoHideHideDelayTicks = 4;
    private const int VkLButton = 0x01;

    private readonly Window _window;
    private readonly UIElement _dragSurface;
    private readonly Func<AppWindow> _getAppWindow;
    private readonly Action? _onPositionChanged;
    private readonly Action<WindowDockEdge, bool>? _onDockStateChanged;

    private bool _tracking;
    private bool _dragging;
    private bool _isAutoHidden;
    private int _outsideDockTicks;
    private WindowDockEdge _dockEdge = WindowDockEdge.None;
    private Pointer? _activePointer;
    private PointInt32 _windowStart;
    private NativePoint _cursorStart;
    private readonly DispatcherTimer _autoHideTimer;

    public WindowPositionController(
        Window window,
        UIElement dragSurface,
        Func<AppWindow> getAppWindow,
        Action? onPositionChanged = null,
        Action<WindowDockEdge, bool>? onDockStateChanged = null)
    {
        _window = window;
        _dragSurface = dragSurface;
        _getAppWindow = getAppWindow;
        _onPositionChanged = onPositionChanged;
        _onDockStateChanged = onDockStateChanged;

        _dragSurface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        _dragSurface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        _dragSurface.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        _dragSurface.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _autoHideTimer.Tick += OnAutoHideTimerTick;
    }

    /// <summary>
    /// Detect dock edge from the current window position and start auto-hide when docked.
    /// Call after restoring saved geometry so roll-up works without a manual re-dock.
    /// </summary>
    public void SyncDockStateFromWindow()
    {
        try
        {
            ClearAutoHideClip();
            _dockEdge = GetDockEdge(_getAppWindow());
            _isAutoHidden = false;
            _outsideDockTicks = 0;
            UpdateAutoHideTimer();
            NotifyDockStateChanged();
        }
        catch
        {
            _dockEdge = WindowDockEdge.None;
            _autoHideTimer.Stop();
            NotifyDockStateChanged();
        }
    }

    private void NotifyDockStateChanged() => _onDockStateChanged?.Invoke(_dockEdge, _isAutoHidden);

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

        ShowAutoHiddenWindow();
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
            var appWindow = _getAppWindow();
            var target = DockToNearestWorkAreaEdge(
                appWindow,
                cur,
                new PointInt32(_windowStart.X + dx, _windowStart.Y + dy));
            appWindow.Move(target);
            _dockEdge = GetDockEdge(appWindow);
            _isAutoHidden = false;
            _outsideDockTicks = 0;
            NotifyDockStateChanged();
            _onPositionChanged?.Invoke();
        }
        catch
        {
            // ignore
        }
    }

    private static PointInt32 DockToNearestWorkAreaEdge(
        AppWindow appWindow,
        NativePoint cursor,
        PointInt32 target)
    {
        var size = appWindow.Size;
        var display = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y),
            DisplayAreaFallback.Nearest);
        var work = display.WorkArea;

        var x = Math.Clamp(target.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(target.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));

        if (Math.Abs(cursor.X - work.X) <= DockThresholdPixels)
        {
            x = work.X;
        }
        else if (Math.Abs(cursor.X - (work.X + work.Width - 1)) <= DockThresholdPixels)
        {
            x = work.X + work.Width - size.Width;
        }

        if (Math.Abs(cursor.Y - work.Y) <= DockThresholdPixels)
        {
            y = work.Y;
        }
        else if (Math.Abs(cursor.Y - (work.Y + work.Height - 1)) <= DockThresholdPixels)
        {
            y = work.Y + work.Height - size.Height;
        }

        return new PointInt32(x, y);
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

        try
        {
            ClearAutoHideClip();
            _dockEdge = GetDockEdge(_getAppWindow());
            _isAutoHidden = false;
            _outsideDockTicks = 0;
            UpdateAutoHideTimer();
            NotifyDockStateChanged();
        }
        catch
        {
            _dockEdge = WindowDockEdge.None;
            _autoHideTimer.Stop();
            NotifyDockStateChanged();
        }

        _onPositionChanged?.Invoke();
    }

    private void OnAutoHideTimerTick(object? sender, object e)
    {
        if (_tracking || _dockEdge == WindowDockEdge.None)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        try
        {
            var appWindow = _getAppWindow();
            if (_isAutoHidden)
            {
                if (IsCursorOnRevealBand(appWindow, cursor, _dockEdge))
                {
                    ShowAutoHiddenWindow(appWindow);
                }

                return;
            }

            if (IsCursorInsideWindow(cursor))
            {
                _outsideDockTicks = 0;
                return;
            }

            _outsideDockTicks++;
            if (_outsideDockTicks >= AutoHideHideDelayTicks)
            {
                HideDockedWindow(appWindow);
            }
        }
        catch
        {
            _autoHideTimer.Stop();
        }
    }

    private void ShowAutoHiddenWindow()
    {
        if (!_isAutoHidden || _dockEdge == WindowDockEdge.None)
        {
            return;
        }

        ShowAutoHiddenWindow(_getAppWindow());
    }

    private void ShowAutoHiddenWindow(AppWindow appWindow)
    {
        ClearAutoHideClip();
        appWindow.Move(GetVisibleDockPosition(appWindow, _dockEdge));
        _isAutoHidden = false;
        _outsideDockTicks = 0;
        NotifyDockStateChanged();
    }

    private void HideDockedWindow(AppWindow appWindow)
    {
        ClearAutoHideClip();
        appWindow.Move(GetPeekDockPosition(appWindow, _dockEdge));
        _isAutoHidden = true;
        _outsideDockTicks = 0;
        NotifyDockStateChanged();
    }

    private void UpdateAutoHideTimer()
    {
        if (_dockEdge == WindowDockEdge.None)
        {
            _autoHideTimer.Stop();
            return;
        }

        if (!_autoHideTimer.IsEnabled)
        {
            _autoHideTimer.Start();
        }
    }

    private static PointInt32 GetVisibleDockPosition(AppWindow appWindow, WindowDockEdge edge)
    {
        var size = appWindow.Size;
        var display = DisplayArea.GetFromPoint(appWindow.Position, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var x = Math.Clamp(appWindow.Position.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(appWindow.Position.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));

        return edge switch
        {
            WindowDockEdge.Left => new PointInt32(work.X, y),
            WindowDockEdge.Right => new PointInt32(work.X + work.Width - size.Width, y),
            WindowDockEdge.Top => new PointInt32(x, work.Y),
            WindowDockEdge.Bottom => new PointInt32(x, work.Y + work.Height - size.Height),
            _ => appWindow.Position,
        };
    }

    /// <summary>
    /// Slide the window mostly off-screen, leaving only <see cref="AutoHidePeekPixels"/> on the dock edge.
    /// Unlike <c>SetWindowRgn</c>, moving the window avoids DWM painting a border around the full rect.
    /// </summary>
    private static PointInt32 GetPeekDockPosition(AppWindow appWindow, WindowDockEdge edge)
    {
        var size = appWindow.Size;
        var display = DisplayArea.GetFromPoint(appWindow.Position, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var pos = appWindow.Position;
        var x = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));

        return edge switch
        {
            WindowDockEdge.Left => new PointInt32(work.X - size.Width + AutoHidePeekPixels, y),
            WindowDockEdge.Right => new PointInt32(work.X + work.Width - AutoHidePeekPixels, y),
            WindowDockEdge.Top => new PointInt32(x, work.Y - size.Height + AutoHidePeekPixels),
            WindowDockEdge.Bottom => new PointInt32(x, work.Y + work.Height - AutoHidePeekPixels),
            _ => pos,
        };
    }

    private static WindowDockEdge GetDockEdge(AppWindow appWindow)
    {
        var size = appWindow.Size;
        var pos = appWindow.Position;
        var display = DisplayArea.GetFromPoint(pos, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;

        if (Math.Abs(pos.X - work.X) <= DockThresholdPixels)
        {
            return WindowDockEdge.Left;
        }

        if (Math.Abs(pos.X - (work.X + work.Width - size.Width)) <= DockThresholdPixels)
        {
            return WindowDockEdge.Right;
        }

        if (Math.Abs(pos.Y - work.Y) <= DockThresholdPixels)
        {
            return WindowDockEdge.Top;
        }

        if (Math.Abs(pos.Y - (work.Y + work.Height - size.Height)) <= DockThresholdPixels)
        {
            return WindowDockEdge.Bottom;
        }

        return WindowDockEdge.None;
    }

    private static bool IsCursorOnRevealBand(AppWindow appWindow, NativePoint cursor, WindowDockEdge edge)
    {
        var size = appWindow.Size;
        var pos = appWindow.Position;
        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var work = display.WorkArea;

        return edge switch
        {
            WindowDockEdge.Left =>
                cursor.X <= work.X + AutoHideRevealPixels
                && cursor.Y >= pos.Y
                && cursor.Y <= pos.Y + size.Height,
            WindowDockEdge.Right =>
                cursor.X >= work.X + work.Width - AutoHideRevealPixels
                && cursor.Y >= pos.Y
                && cursor.Y <= pos.Y + size.Height,
            WindowDockEdge.Top =>
                cursor.Y <= work.Y + AutoHideRevealPixels
                && cursor.X >= pos.X
                && cursor.X <= pos.X + size.Width,
            WindowDockEdge.Bottom =>
                cursor.Y >= work.Y + work.Height - AutoHideRevealPixels
                && cursor.X >= pos.X
                && cursor.X <= pos.X + size.Width,
            _ => false,
        };
    }

    private void ClearAutoHideClip()
    {
        _ = NativeMethods.SetWindowRgn(WindowHandle, 0, true);
    }

    private bool IsCursorInsideWindow(NativePoint cursor)
    {
        if (!NativeMethods.GetWindowRect(WindowHandle, out var rect))
        {
            return false;
        }

        return cursor.X >= rect.Left
            && cursor.X <= rect.Right
            && cursor.Y >= rect.Top
            && cursor.Y <= rect.Bottom;
    }

    private IntPtr WindowHandle => WindowNative.GetWindowHandle(_window);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
    }
}
