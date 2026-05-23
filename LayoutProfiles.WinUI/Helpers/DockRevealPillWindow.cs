using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Tiny borderless overlay that draws only the dock-edge pill on the screen bezel.
/// </summary>
internal sealed class DockRevealPillWindow : Window
{
    private const int EdgeBandPx =
        DockRevealIndicatorHelper.EdgeInsetPx * 2
        + (int)DockRevealIndicatorHelper.PillThick
        + 8;
    private const int DragThresholdPixels = 2;
    private const int UndockThresholdPixels = DockDisplayHelper.DockThresholdPixels;
    private const int VkLButton = 0x01;

    private readonly Border _pill;
    private readonly WindowGlassBackdropHelper _glassBackdrop;
    private WindowDockEdge _edge = WindowDockEdge.Top;
    private DisplayArea? _dockedDisplay;
    private AppWindow? _mainAppWindow;
    private Action? _onMainWindowMoved;
    private Action<PointInt32, int, int, int, int>? _onUndockDragStarted;

    private bool _tracking;
    private bool _dragging;
    private Pointer? _activePointer;
    private PointInt32 _mainWindowStart;
    private NativePoint _cursorStart;

    public DockRevealPillWindow()
    {
        Title = string.Empty;

        _glassBackdrop = new WindowGlassBackdropHelper(this);
        _glassBackdrop.TryEnable();

        _edge = WindowDockEdge.Top;
        var (clientW, clientH) = GetClientSize(_edge);
        _pill = DockRevealIndicatorHelper.Create();
        DockRevealIndicatorHelper.ApplyLayout(_pill, _edge);
        ApplyTheme(AppTheme.IsDark);

        var root = new Grid
        {
            Width = clientW,
            Height = clientH,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        root.Children.Add(_pill);
        Content = root;
        AlignPillOnRoot(_edge);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        Closed += OnClosed;

        _pill.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPillPointerPressed), true);
        _pill.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPillPointerReleased), true);
        _pill.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPillPointerReleased), true);
        _pill.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPillPointerCaptureLost), true);

        ConfigureChrome(_edge);
    }

    public Action? OnMainWindowMoved
    {
        get => _onMainWindowMoved;
        set => _onMainWindowMoved = value;
    }

    public Action<PointInt32, int, int, int, int>? OnUndockDragStarted
    {
        get => _onUndockDragStarted;
        set => _onUndockDragStarted = value;
    }

    private void OnResolvedThemeChanged() => ApplyTheme(AppTheme.IsDark);

    private void OnClosed(object sender, WindowEventArgs e)
    {
        EndTracking();
        AppTheme.ResolvedThemeChanged -= OnResolvedThemeChanged;
        _glassBackdrop.Dispose();
    }

    public void ApplyTheme(bool dark)
    {
        _glassBackdrop.ApplyTheme(dark);
        DockRevealIndicatorHelper.ApplyTheme(_pill, dark);
    }

    public void PlaceAtEdge(WindowDockEdge edge, DisplayArea display, AppWindow mainWindow)
    {
        _edge = edge;
        _dockedDisplay = display;
        _mainAppWindow = mainWindow;
        var size = mainWindow.Size;
        var pos = mainWindow.Position;
        var work = display.WorkArea;

        var (clientW, clientH) = GetClientSize(edge);
        DockRevealIndicatorHelper.ApplyLayout(_pill, edge);
        AlignPillOnRoot(edge);

        if (Content is FrameworkElement root)
        {
            root.Width = clientW;
            root.Height = clientH;
        }

        try
        {
            var appWindow = GetAppWindow();
            appWindow.Resize(new SizeInt32(clientW, clientH));
            appWindow.Move(ComputeScreenPosition(edge, pos, size, work, clientW, clientH));

            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyOverlayWindowChrome(hwnd);
            WindowChromeHelper.ApplyPillWindowRegion(hwnd, clientW, clientH);
            ApplyTopmostOverlay();
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("DockRevealPillWindow.ApplyTopmostOverlay", $"ignored: {ex.Message}"); // ignore placement failures
        }
    }

    private void OnPillPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_pill).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_mainAppWindow is null || _dockedDisplay is null || _edge == WindowDockEdge.None)
        {
            return;
        }

        BeginTracking(e.Pointer);
        e.Handled = true;
    }

    private void BeginTracking(Pointer pointer)
    {
        EndTracking();

        _tracking = true;
        _dragging = false;
        _activePointer = pointer;

        try
        {
            _mainWindowStart = _mainAppWindow!.Position;
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("DockRevealPillWindow.BeginTracking", $"ignored: {ex.Message}"); // ignore
            _mainWindowStart = new PointInt32(0, 0);
        }

        _ = NativeMethods.GetCursorPos(out _cursorStart);
        NativeMethods.SetCapture(WindowHandle);
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (!_tracking || _mainAppWindow is null || _dockedDisplay is null)
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

        if (TryGetUndockPerpendicularDelta(dx, dy, out var perpendicular)
            && perpendicular >= UndockThresholdPixels)
        {
            _onUndockDragStarted?.Invoke(_mainWindowStart, dx, dy, _cursorStart.X, _cursorStart.Y);
            EndTracking();
            return;
        }

        try
        {
            var main = _mainAppWindow;
            var work = _dockedDisplay.WorkArea;
            var target = DockDisplayHelper.SlideAlongDockEdge(
                _edge,
                _mainWindowStart,
                dx,
                dy,
                main.Size,
                work);
            main.Move(target);
            PlaceAtEdge(_edge, _dockedDisplay, main);
            _onMainWindowMoved?.Invoke();
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("DockRevealPillWindow.Invoke", $"ignored: {ex.Message}"); // ignore
        }
    }

    private static bool IsLeftButtonDown() =>
        (NativeMethods.GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    private void OnPillPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_tracking || _activePointer is null || e.Pointer != _activePointer)
        {
            return;
        }

        EndTracking();
        e.Handled = true;
    }

    private void OnPillPointerCaptureLost(object sender, PointerRoutedEventArgs e)
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

        var didDrag = _dragging;
        _tracking = false;
        _dragging = false;
        _activePointer = null;
        CompositionTarget.Rendering -= OnCompositionRendering;
        NativeMethods.ReleaseCapture();

        if (didDrag)
        {
            _onMainWindowMoved?.Invoke();
        }
    }

    private void ConfigureChrome(WindowDockEdge edge)
    {
        try
        {
            var appWindow = GetAppWindow();
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            var (w, h) = GetClientSize(edge);
            appWindow.Resize(new SizeInt32(w, h));

            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyOverlayWindowChrome(hwnd);
            WindowChromeHelper.ApplyPillWindowRegion(hwnd, w, h);
            ApplyTopmostOverlay();
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("DockRevealPillWindow.ApplyTopmostOverlay", $"ignored: {ex.Message}"); // ignore
        }
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void ApplyTopmostOverlay()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.SetTopmostToolWindow(hwnd);
    }

    private IntPtr WindowHandle => WindowNative.GetWindowHandle(this);

    private void AlignPillOnRoot(WindowDockEdge edge)
    {
        if (Content is not Grid root)
        {
            return;
        }

        _pill.HorizontalAlignment = edge switch
        {
            WindowDockEdge.Left => HorizontalAlignment.Left,
            WindowDockEdge.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        _pill.VerticalAlignment = edge switch
        {
            WindowDockEdge.Top => VerticalAlignment.Top,
            WindowDockEdge.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        _pill.Margin = edge switch
        {
            WindowDockEdge.Left => new Thickness(DockRevealIndicatorHelper.EdgeInsetPx, 0, 0, 0),
            WindowDockEdge.Right => new Thickness(0, 0, DockRevealIndicatorHelper.EdgeInsetPx, 0),
            WindowDockEdge.Top => new Thickness(0, DockRevealIndicatorHelper.EdgeInsetPx, 0, 0),
            WindowDockEdge.Bottom => new Thickness(0, 0, 0, DockRevealIndicatorHelper.EdgeInsetPx),
            _ => new Thickness(0),
        };
    }

    private bool TryGetUndockPerpendicularDelta(int deltaX, int deltaY, out int perpendicular)
    {
        perpendicular = _edge switch
        {
            WindowDockEdge.Top => deltaY,
            WindowDockEdge.Bottom => -deltaY,
            WindowDockEdge.Left => deltaX,
            WindowDockEdge.Right => -deltaX,
            _ => 0,
        };
        return _edge != WindowDockEdge.None;
    }

    private static (int Width, int Height) GetClientSize(WindowDockEdge edge) =>
        edge is WindowDockEdge.Left or WindowDockEdge.Right
            ? (EdgeBandPx, (int)DockRevealIndicatorHelper.PillLong)
            : ((int)DockRevealIndicatorHelper.PillLong, EdgeBandPx);

    private static PointInt32 ComputeScreenPosition(
        WindowDockEdge edge,
        PointInt32 mainPos,
        SizeInt32 mainSize,
        RectInt32 work,
        int clientW,
        int clientH)
    {
        var centerX = mainPos.X + mainSize.Width / 2;
        var centerY = mainPos.Y + mainSize.Height / 2;

        var point = edge switch
        {
            WindowDockEdge.Top => new PointInt32(
                centerX - clientW / 2,
                work.Y),
            WindowDockEdge.Bottom => new PointInt32(
                centerX - clientW / 2,
                work.Y + work.Height - clientH),
            WindowDockEdge.Left => new PointInt32(
                work.X,
                centerY - clientH / 2),
            WindowDockEdge.Right => new PointInt32(
                work.X + work.Width - clientW,
                centerY - clientH / 2),
            _ => mainPos,
        };

        var x = Math.Clamp(point.X, work.X, Math.Max(work.X, work.X + work.Width - clientW));
        var y = Math.Clamp(point.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - clientH));
        return new PointInt32(x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        private const int GwlExstyle = -20;
        private const uint WsExToolwindow = 0x00000080;
        private const uint WsExTransparent = 0x00000020;
        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNoactivate = 0x0010;
        private const uint SwpShowwindow = 0x0040;

        public static void SetTopmostToolWindow(IntPtr hwnd)
        {
            var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
            var cleared = exStyle & ~(nint)WsExTransparent;
            SetWindowLongPtr(hwnd, GwlExstyle, cleared | (nint)WsExToolwindow);
            _ = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

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
