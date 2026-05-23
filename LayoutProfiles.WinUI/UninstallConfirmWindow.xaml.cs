using LayoutProfiles.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace LayoutProfiles.WinUI;

public sealed partial class UninstallConfirmWindow : Window
{
    private const int WindowWidth = 480;
    private const int WindowHeight = 360;

    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;
    private int _step;

    private UninstallConfirmWindow()
    {
        InitializeComponent();
        Title = "Uninstall Snapdesk";

        Closed += OnClosed;
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        SecondaryWindowTracker.Register(this);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        ApplyThemeFromApp();
        ShowStep(0);

        ConfigureWindowChrome();
    }

    public static Task<bool> ShowConfirmAsync()
    {
        var window = new UninstallConfirmWindow();
        WindowChromeHelper.PresentModal(window, App.MainWindowInstance);
        return window._tcs.Task;
    }

    private void ShowStep(int step)
    {
        _step = step;
        if (step == 0)
        {
            HeadingText.Text = "Uninstall Snapdesk?";
            WarningText.Text =
                "This permanently removes Snapdesk from your PC, including ALL saved layout profiles and shortcuts.";
            DetailText.Text = "This cannot be undone.";
            PrimaryButton.Content = "Continue";
        }
        else
        {
            HeadingText.Text = "Are you sure?";
            WarningText.Text =
                "Snapdesk, your profiles folder, and shortcuts will be deleted permanently.";
            DetailText.Text = "Choose Uninstall only if you are certain.";
            PrimaryButton.Content = "Uninstall";
        }

        ApplyThemeFromApp();
        DispatcherQueue.TryEnqueue(() => CancelButton.Focus(FocusState.Programmatic));
    }

    private void OnResolvedThemeChanged() =>
        DispatcherQueue.TryEnqueue(ApplyThemeFromApp);

    private void ApplyThemeFromApp()
    {
        var palette = AppTheme.Palette;
        var destructive = AppTheme.Destructive;
        var elementTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;

        RootGrid.Background = palette.Background;
        RootGrid.RequestedTheme = elementTheme;

        HeadingText.Foreground = destructive;
        WarningText.Foreground = destructive;
        DetailText.Foreground = palette.Muted;
        CancelButton.Foreground = palette.Primary;
        CancelButton.Background = palette.AddChipBackground;

        if (_step == 0)
        {
            PrimaryButton.Foreground = palette.Primary;
            PrimaryButton.Background = palette.AddChipBackground;
        }
        else
        {
            PrimaryButton.Foreground = destructive;
            PrimaryButton.Background = palette.AddChipBackground;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyNonClientFrame(hwnd, AppTheme.IsDark);
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("UninstallConfirmWindow.ApplyNonClientFrame", $"ignored: {ex.Message}"); // ignore
        }
    }

    private void ConfigureWindowChrome() =>
        SecondaryWindowPlacement.Apply(this, SecondaryWindowPlacement.UninstallConfirm, WindowWidth, WindowHeight);

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            ShowStep(1);
            return;
        }

        Complete(true);
    }

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
        Complete(false);
    }

    private void Complete(bool confirmed)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        WindowChromeHelper.ReleaseModal(this);
        _tcs.TrySetResult(confirmed);
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("UninstallConfirmWindow.Close", $"ignored: {ex.Message}"); // ignore double-close
        }
    }
}
