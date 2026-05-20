using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Owns the screen-edge pill overlay so it stays visible while the main widget is rolled up.
/// </summary>
internal sealed class DockRevealPillController
{
    private readonly Func<AppWindow> _getAppWindow;
    private DockRevealPillWindow? _pillWindow;
    private bool _registered;
    private bool _isShown;

    public DockRevealPillController(Func<AppWindow> getAppWindow)
    {
        _getAppWindow = getAppWindow;
    }

    public void Update(WindowDockEdge edge, bool isAutoHidden, DisplayArea? dockedDisplay)
    {
        if (!isAutoHidden || edge == WindowDockEdge.None || dockedDisplay is null)
        {
            Hide();
            return;
        }

        try
        {
            EnsureWindow();
            _pillWindow!.ApplyTheme(AppTheme.IsDark);
            _pillWindow.PlaceAtEdge(edge, dockedDisplay, _getAppWindow());
            Show();
        }
        catch
        {
            Hide();
        }
    }

    public void Hide()
    {
        if (_pillWindow is null || !_isShown)
        {
            return;
        }

        try
        {
            GetPillAppWindow().Hide();
            _isShown = false;
        }
        catch
        {
            // ignore
        }
    }

    public void Close()
    {
        if (_pillWindow is null)
        {
            return;
        }

        try
        {
            _pillWindow.Close();
        }
        catch
        {
            // ignore
        }

        _pillWindow = null;
        _isShown = false;
        _registered = false;
    }

    private void Show()
    {
        if (_pillWindow is null)
        {
            return;
        }

        try
        {
            GetPillAppWindow().Show(false);
            _isShown = true;
        }
        catch
        {
            // ignore
        }
    }

    private void EnsureWindow()
    {
        if (_pillWindow is not null)
        {
            return;
        }

        _pillWindow = new DockRevealPillWindow();
        if (!_registered)
        {
            SecondaryWindowTracker.Register(_pillWindow);
            _registered = true;
        }
    }

    private AppWindow GetPillAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(_pillWindow!);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
