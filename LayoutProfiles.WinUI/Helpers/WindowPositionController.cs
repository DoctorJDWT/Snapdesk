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
    private static int AutoHideRevealPixels => DockRevealIndicatorHelper.RevealBandDepthPx;
    private const int AutoHideHideDelayTicks = 4;
    private const int VkLButton = 0x01;

    private readonly Window _window;
    private readonly UIElement _dragSurface;
    private readonly Func<AppWindow> _getAppWindow;
    private readonly Action? _onPositionChanged;
    private readonly Action<WindowDockEdge, bool, DisplayArea?>? _onDockStateChanged;

    private bool _tracking;
    private bool _dragging;
    private bool _isAutoHidden;
    private int _outsideDockTicks;
    private int _flyoutSuppressDepth;
    private WindowDockEdge _dockEdge = WindowDockEdge.None;
    private RectInt32 _dockedWorkArea;
    private bool _hasDockedWorkArea;
    private Pointer? _activePointer;
    private PointInt32 _windowStart;
    private NativePoint _cursorStart;
    private readonly DispatcherTimer _autoHideTimer;

    public WindowDockEdge DockEdge => _dockEdge;

    public bool IsAutoHidden => _isAutoHidden;

    public bool IsTracking => _tracking;

    /// <summary>
    /// Pill dragged away from the dock edge: show the widget, clear dock state, and keep dragging on the main window.
    /// </summary>
    public void ContinueDragFromPill(PointInt32 windowStart, int deltaX, int deltaY, int cursorStartX, int cursorStartY)
    {
        if (_tracking)
        {
            EndTracking();
        }

        ClearAutoHideClip();
        ClearDockState();
        _isAutoHidden = false;

        try
        {
            var appWindow = _getAppWindow();
            appWindow.Show(false);
            var target = ClampFreeMoveTarget(windowStart, deltaX, deltaY, appWindow);
            appWindow.Move(target);
        }
        catch
        {
            // ignore
        }

        NotifyDockStateChanged();
        _onPositionChanged?.Invoke();

        _windowStart = windowStart;
        _cursorStart = new NativePoint { X = cursorStartX, Y = cursorStartY };
        _tracking = true;
        _dragging = true;
        _activePointer = null;
        NativeMethods.SetCapture(WindowHandle);
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    public WindowPositionController(
        Window window,
        UIElement dragSurface,
        Func<AppWindow> getAppWindow,
        Action? onPositionChanged = null,
        Action<WindowDockEdge, bool, DisplayArea?>? onDockStateChanged = null)
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
    /// MenuFlyout popups are separate HWNDs; pause auto-hide while any flyout is open.
    /// </summary>
    public void PushFlyoutSuppress()
    {
        _flyoutSuppressDepth++;
        _outsideDockTicks = 0;
        ShowAutoHiddenWindow();
    }

    public void PopFlyoutSuppress()
    {
        if (_flyoutSuppressDepth > 0)
        {
            _flyoutSuppressDepth--;
        }
    }

    public bool TryGetDockedDisplay(out DisplayArea display)
    {
        display = null!;
        return _dockEdge != WindowDockEdge.None
            && _hasDockedWorkArea
            && DockDisplayHelper.TryGetDisplayByWorkArea(_dockedWorkArea, out display);
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
            PinDockState(_getAppWindow());
            _isAutoHidden = false;
            _outsideDockTicks = 0;
            UpdateAutoHideTimer();
            NotifyDockStateChanged();
        }
        catch
        {
            ClearDockState();
            NotifyDockStateChanged();
        }
    }

    private void NotifyDockStateChanged()
    {
        DisplayArea? display = null;
        if (TryGetDockedDisplay(out var resolved))
        {
            display = resolved;
        }

        _onDockStateChanged?.Invoke(_dockEdge, _isAutoHidden, display);
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
            PinDockState(appWindow);
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

        if (Math.Abs(cursor.X - work.X) <= DockDisplayHelper.DockThresholdPixels)
        {
            x = work.X;
        }
        else if (Math.Abs(cursor.X - (work.X + work.Width - 1)) <= DockDisplayHelper.DockThresholdPixels)
        {
            x = work.X + work.Width - size.Width;
        }

        if (Math.Abs(cursor.Y - work.Y) <= DockDisplayHelper.DockThresholdPixels)
        {
            y = work.Y;
        }
        else if (Math.Abs(cursor.Y - (work.Y + work.Height - 1)) <= DockDisplayHelper.DockThresholdPixels)
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
            PinDockState(_getAppWindow());
            _isAutoHidden = false;
            _outsideDockTicks = 0;
            UpdateAutoHideTimer();
            NotifyDockStateChanged();
        }
        catch
        {
            ClearDockState();
            NotifyDockStateChanged();
        }

        _onPositionChanged?.Invoke();
    }

    private int _idleTraceCounter;

    private void OnAutoHideTimerTick(object? sender, object e)
    {
        if (_tracking || _dockEdge == WindowDockEdge.None || _flyoutSuppressDepth > 0)
        {
            return;
        }

        // Every ~6s emit a heartbeat so we can see the timer is firing.
        if ((++_idleTraceCounter % 50) == 0)
        {
            StartupTrace.Write(
                $"AutoHide tick edge={_dockEdge} hidden={_isAutoHidden} outsideTicks={_outsideDockTicks}");
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
                    StartupTrace.Write($"AutoHide reveal edge={_dockEdge} cursor=({cursor.X},{cursor.Y})");
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
                StartupTrace.Write($"AutoHide hide edge={_dockEdge} cursor=({cursor.X},{cursor.Y}) ticks={_outsideDockTicks}");
                HideDockedWindow(appWindow);
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"AutoHide tick exception {ex.GetType().Name}: {ex.Message}");
            _autoHideTimer.Stop();
        }
    }

    /// <summary>Reveal the widget when docked and rolled up to the screen edge.</summary>
    public void RevealIfAutoHidden()
    {
        ShowAutoHiddenWindow();
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
        try
        {
            appWindow.Show(false);
        }
        catch
        {
            // ignore
        }

        if (TryGetDockedDisplay(out var display))
        {
            appWindow.Move(DockDisplayHelper.GetVisibleDockPosition(appWindow, _dockEdge, display));
        }

        _isAutoHidden = false;
        _outsideDockTicks = 0;
        NotifyDockStateChanged();
    }

    private void HideDockedWindow(AppWindow appWindow)
    {
        ClearAutoHideClip();
        if (TryGetDockedDisplay(out var display))
        {
            appWindow.Move(DockDisplayHelper.GetVisibleDockPosition(appWindow, _dockEdge, display));
        }

        _isAutoHidden = true;
        _outsideDockTicks = 0;

        try
        {
            appWindow.Hide();
        }
        catch
        {
            // ignore
        }

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

    private void PinDockState(AppWindow appWindow)
    {
        var previousEdge = _dockEdge;
        if (DockDisplayHelper.TryResolveDockedDisplay(appWindow, out var edge, out var display))
        {
            _dockEdge = edge;
            _dockedWorkArea = display.WorkArea;
            _hasDockedWorkArea = true;
            if (previousEdge != edge)
            {
                StartupTrace.Write(
                    $"PinDockState edge={edge} work=({display.WorkArea.X},{display.WorkArea.Y}," +
                    $"{display.WorkArea.Width}x{display.WorkArea.Height}) " +
                    $"win=({appWindow.Position.X},{appWindow.Position.Y},{appWindow.Size.Width}x{appWindow.Size.Height})");
            }

            return;
        }

        if (previousEdge != WindowDockEdge.None)
        {
            var pos = appWindow.Position;
            var size = appWindow.Size;
            StartupTrace.Write($"PinDockState UNDOCKED win=({pos.X},{pos.Y},{size.Width}x{size.Height})");
        }

        ClearDockState();
    }

    private void ClearDockState()
    {
        _dockEdge = WindowDockEdge.None;
        _hasDockedWorkArea = false;
        _dockedWorkArea = default;
        _autoHideTimer.Stop();
    }

    private bool IsCursorOnRevealBand(AppWindow appWindow, NativePoint cursor, WindowDockEdge edge)
    {
        if (!TryGetDockedDisplay(out var display))
        {
            return false;
        }

        var size = appWindow.Size;
        var pos = appWindow.Position;
        var work = display.WorkArea;
        var centerX = pos.X + size.Width / 2;
        var centerY = pos.Y + size.Height / 2;
        var halfPillLong = (int)(DockRevealIndicatorHelper.PillLong / 2);
        var reveal = AutoHideRevealPixels;

        return edge switch
        {
            WindowDockEdge.Left =>
                cursor.X <= work.X + reveal
                && cursor.Y >= centerY - halfPillLong
                && cursor.Y <= centerY + halfPillLong,
            WindowDockEdge.Right =>
                cursor.X >= work.X + work.Width - reveal
                && cursor.Y >= centerY - halfPillLong
                && cursor.Y <= centerY + halfPillLong,
            WindowDockEdge.Top =>
                cursor.Y <= work.Y + reveal
                && cursor.X >= centerX - halfPillLong
                && cursor.X <= centerX + halfPillLong,
            WindowDockEdge.Bottom =>
                cursor.Y >= work.Y + work.Height - reveal
                && cursor.X >= centerX - halfPillLong
                && cursor.X <= centerX + halfPillLong,
            _ => false,
        };
    }

    private static PointInt32 ClampFreeMoveTarget(
        PointInt32 windowStart,
        int deltaX,
        int deltaY,
        AppWindow appWindow)
    {
        var size = appWindow.Size;
        var display = DisplayArea.GetFromPoint(
            new PointInt32(windowStart.X + deltaX + size.Width / 2, windowStart.Y + deltaY + size.Height / 2),
            DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var target = new PointInt32(windowStart.X + deltaX, windowStart.Y + deltaY);
        var x = Math.Clamp(target.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(target.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
        return new PointInt32(x, y);
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

        var inside = cursor.X >= rect.Left
            && cursor.X < rect.Right
            && cursor.Y >= rect.Top
            && cursor.Y < rect.Bottom;

        if (inside && _outsideDockTicks > 0)
        {
            StartupTrace.Write(
                $"AutoHide cursor re-entered cursor=({cursor.X},{cursor.Y}) " +
                $"rect=({rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top})");
        }

        return inside;
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
