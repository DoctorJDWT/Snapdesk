using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    /// <summary>Distance from the monitor work-area edge to the pill (into the desktop).</summary>
    public const int EdgeInsetPx = 6;

    private readonly Border _pill;
    private WindowDockEdge _edge = WindowDockEdge.Top;

    public DockRevealPillWindow()
    {
        Title = string.Empty;
        SystemBackdrop = null;

        _edge = WindowDockEdge.Top;
        var (clientW, clientH) = GetClientSize(_edge);
        _pill = DockRevealIndicatorHelper.Create();
        DockRevealIndicatorHelper.ApplyLayout(_pill, _edge);

        var root = new Grid
        {
            Width = clientW,
            Height = clientH,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        root.Children.Add(_pill);
        Content = root;

        ConfigureChrome(_edge);
    }

    public void ApplyTheme(bool dark) => _pill.Background = DockRevealIndicatorHelper.CreateBrush(dark);

    public void PlaceAtEdge(WindowDockEdge edge, DisplayArea display, AppWindow mainWindow)
    {
        _edge = edge;
        var size = mainWindow.Size;
        var pos = mainWindow.Position;
        var work = display.WorkArea;

        var (clientW, clientH) = GetClientSize(edge);
        DockRevealIndicatorHelper.ApplyLayout(_pill, edge);

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
            ApplyTopmostClickThrough();
        }
        catch
        {
            // ignore placement failures
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
            ApplyTopmostClickThrough();
        }
        catch
        {
            // ignore
        }
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void ApplyTopmostClickThrough()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.SetClickThroughTopmost(hwnd);
    }

    private static (int Width, int Height) GetClientSize(WindowDockEdge edge) =>
        edge is WindowDockEdge.Left or WindowDockEdge.Right
            ? ((int)DockRevealIndicatorHelper.PillThick, (int)DockRevealIndicatorHelper.PillLong)
            : ((int)DockRevealIndicatorHelper.PillLong, (int)DockRevealIndicatorHelper.PillThick);

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

        return edge switch
        {
            WindowDockEdge.Top => new PointInt32(
                centerX - clientW / 2,
                work.Y + EdgeInsetPx),
            WindowDockEdge.Bottom => new PointInt32(
                centerX - clientW / 2,
                work.Y + work.Height - clientH - EdgeInsetPx),
            WindowDockEdge.Left => new PointInt32(
                work.X + EdgeInsetPx,
                centerY - clientH / 2),
            WindowDockEdge.Right => new PointInt32(
                work.X + work.Width - clientW - EdgeInsetPx,
                centerY - clientH / 2),
            _ => mainPos,
        };
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

        public static void SetClickThroughTopmost(IntPtr hwnd)
        {
            var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
            SetWindowLongPtr(hwnd, GwlExstyle, exStyle | (nint)WsExToolwindow | (nint)WsExTransparent);
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
    }
}
