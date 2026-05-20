using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;
using Windows.UI;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Desktop acrylic system backdrop so the widget can show the desktop through frosted glass.
/// Semi-transparent XAML brushes alone are not enough — WinUI needs a <see cref="SystemBackdrop"/>.
/// </summary>
internal sealed class WindowGlassBackdropHelper : IDisposable
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private readonly Window _window;
    private bool _enabled;

    public WindowGlassBackdropHelper(Window window) => _window = window;

    public bool IsEnabled => _enabled;

    public bool TryEnable()
    {
        if (_enabled || !DesktopAcrylicController.IsSupported())
        {
            return false;
        }

        try
        {
            // Always active: this widget is usually unfocused; tying acrylic to activation made
            // glass appear only while right-clicking (context menu activates the window).
            _configuration = new SystemBackdropConfiguration { IsInputActive = true };
            ApplyTheme(AppTheme.IsDark);

            _controller = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Base,
            };
            _controller.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
            _controller.SetSystemBackdropConfiguration(_configuration);

            _window.Closed += OnWindowClosed;
            _enabled = true;
            StartupTrace.Write("WindowGlassBackdropHelper: desktop acrylic enabled");
            return true;
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"WindowGlassBackdropHelper: enable failed: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public void ApplyTheme(bool dark)
    {
        if (_configuration is not null)
        {
            _configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        }

        if (_controller is null)
        {
            return;
        }

        // Light tint + strong luminosity so wallpaper blur reads clearly (XAML veil stays transparent).
        if (dark)
        {
            _controller.TintColor = Color.FromArgb(255, 0x28, 0x28, 0x28);
            _controller.FallbackColor = Color.FromArgb(255, 0x28, 0x28, 0x28);
        }
        else
        {
            _controller.TintColor = Color.FromArgb(255, 0xF0, 0xF0, 0xF0);
            _controller.FallbackColor = Color.FromArgb(255, 0xF0, 0xF0, 0xF0);
        }

        _controller.TintOpacity = 0.22f;
        _controller.LuminosityOpacity = 0.82f;

        if (_configuration is not null)
        {
            _configuration.IsInputActive = true;
            _controller.SetSystemBackdropConfiguration(_configuration);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        _window.Closed -= OnWindowClosed;
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
        _enabled = false;
    }
}
