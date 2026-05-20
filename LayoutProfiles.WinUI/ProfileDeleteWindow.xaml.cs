using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace LayoutProfiles.WinUI;

public sealed partial class ProfileDeleteWindow : Window
{
    // Match ProfileEditorWindow name step so buttons are not clipped by chrome.
    private const int WindowWidth = 480;
    private const int WindowHeight = 340;

    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    private ProfileDeleteWindow(ProfileRow row)
    {
        InitializeComponent();
        Title = "Snapdesk";

        var baseColor = ProfileColors.BaseColorForName(row.DisplayName);
        var fg = ProfileColors.ForegroundFor(baseColor);
        ProfileBadge.Background = new SolidColorBrush(baseColor);
        ProfileInitial.Text = ProfileColors.Initial(row.DisplayName);
        ProfileInitial.Foreground = new SolidColorBrush(fg);
        ProfileNameText.Text = row.DisplayName;
        ProfileMetaText.Text = $"{row.WindowCount} windows";
        MessageText.Text = $"Remove \"{row.DisplayName}\"? This cannot be undone.";

        Closed += OnClosed;
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        SecondaryWindowTracker.Register(this);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        ApplyThemeFromApp();

        ConfigureWindowChrome();
    }

    public static Task<bool> ShowConfirmAsync(ProfileRow row)
    {
        var window = new ProfileDeleteWindow(row);
        window.Activate();
        return window._tcs.Task;
    }

    private void OnResolvedThemeChanged() =>
        DispatcherQueue.TryEnqueue(ApplyThemeFromApp);

    private void ApplyThemeFromApp()
    {
        var palette = AppTheme.Palette;
        var elementTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        var primary = AppTheme.IsDark
            ? new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF0, 0xF2, 0xF5))
            : new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1E, 0x1E, 0x1E));

        RootGrid.Background = palette.Background;
        RootGrid.RequestedTheme = elementTheme;

        HeadingText.Foreground = primary;
        ProfileNameText.Foreground = primary;
        ProfileMetaText.Foreground = palette.Muted;
        MessageText.Foreground = palette.Muted;

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyNonClientFrame(hwnd, AppTheme.IsDark);
        }
        catch
        {
            // ignore
        }
    }

    private void ConfigureWindowChrome() =>
        SecondaryWindowPlacement.Apply(this, SecondaryWindowPlacement.ProfileDelete, WindowWidth, WindowHeight);

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void OnDeleteClick(object sender, RoutedEventArgs e) => Complete(true);

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(false);
        }
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        AppTheme.ResolvedThemeChanged -= OnResolvedThemeChanged;
        SecondaryWindowPlacement.TryPersist(this, SecondaryWindowPlacement.ProfileDelete);
        Complete(false);
    }

    private void Complete(bool confirmed)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _tcs.TrySetResult(confirmed);
        try
        {
            Close();
        }
        catch
        {
            // ignore double-close
        }
    }
}
