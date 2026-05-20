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
    private const int WindowPadPx = 2;

    private readonly Border _pill;

    public DockRevealPillWindow()
    {
        Title = string.Empty;

        var (clientW, clientH) = GetClientSize(WindowDockEdge.Top);
        _pill = DockRevealIndicatorHelper.Create();
        DockRevealIndicatorHelper.ApplyLayout(_pill, WindowDockEdge.Top);

        var root = new Grid
        {
            Width = clientW,
            Height = clientH,
            Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
        };
        root.Children.Add(_pill);
        Content = root;

        ConfigureChrome(clientW, clientH);
    }

    public void ApplyTheme(bool dark) => _pill.Background = DockRevealIndicatorHelper.CreateBrush(dark);

    public void PlaceAtEdge(WindowDockEdge edge, DisplayArea display, AppWindow mainWindow)
    {
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
            ApplyTopmostClickThrough();
        }
        catch
        {
            // ignore placement failures
        }
    }

    private void ConfigureChrome(int width, int height)
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

            WindowChromeHelper.ApplyBorderlessTitleBar(appWindow);
            appWindow.Resize(new SizeInt32(width, height));

            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyNonClientFrame(hwnd, AppTheme.IsDark);
            WindowChromeHelper.ApplySquareCorners(this);
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
            ? ((int)Math.Ceiling(DockRevealIndicatorHelper.PillThick + WindowPadPx * 2),
                (int)Math.Ceiling(DockRevealIndicatorHelper.PillLong + WindowPadPx * 2))
            : ((int)Math.Ceiling(DockRevealIndicatorHelper.PillLong + WindowPadPx * 2),
                (int)Math.Ceiling(DockRevealIndicatorHelper.PillThick + WindowPadPx * 2));

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
