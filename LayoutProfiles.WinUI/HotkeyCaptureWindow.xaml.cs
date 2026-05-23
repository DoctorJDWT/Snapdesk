using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using WinRT.Interop;

namespace LayoutProfiles.WinUI;

public sealed partial class HotkeyCaptureWindow : Window
{
    // Match ProfileDeleteWindow / UninstallConfirmWindow so buttons are not clipped by chrome.
    private const int WindowWidth = 480;
    private const int WindowHeight = 360;

    private Grid _rootGrid = null!;
    private TextBlock _headingText = null!;
    private TextBlock _promptText = null!;
    private TextBlock _chordText = null!;
    private TextBlock _messageText = null!;
    private Button _cancelButton = null!;
    private Button _saveButton = null!;

    private readonly TaskCompletionSource<HotkeyCaptureResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _actionLabel;
    private uint _capturedModifiers;
    private uint _capturedVirtualKey;
    private HotkeyValidationResult _validation = HotkeyValidationResult.Error("Press a key combination.");
    private bool _completed;

    private HotkeyCaptureWindow(string actionLabel, uint currentModifiers, uint currentVirtualKey)
    {
        _actionLabel = actionLabel;
        _capturedModifiers = currentModifiers;
        _capturedVirtualKey = currentVirtualKey;

        BuildUi();
        Title = "Snapdesk";
        UpdateDisplay();

        Closed += OnClosed;
        _rootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        _rootGrid.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnRootPreviewKeyDown), handledEventsToo: true);
        SecondaryWindowTracker.Register(this);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        ApplyThemeFromApp();
        SecondaryWindowPlacement.Apply(this, SecondaryWindowPlacement.HotkeyCapture, WindowWidth, WindowHeight);
        Activated += OnActivated;
    }

    public static Task<HotkeyCaptureResult?> ShowAsync(
        string actionLabel,
        uint currentModifiers,
        uint currentVirtualKey)
    {
        var window = new HotkeyCaptureWindow(actionLabel, currentModifiers, currentVirtualKey);
        WindowChromeHelper.PresentModal(window, App.MainWindowInstance);
        return window._tcs.Task;
    }

    private void BuildUi()
    {
        _rootGrid = new Grid { Padding = new Thickness(24), IsTabStop = true };
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 0 });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _headingText = new TextBlock
        {
            Text = "Set hotkey",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };

        _promptText = new TextBlock
        {
            Text = $"Press a shortcut for {_actionLabel}.",
            FontSize = 13,
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.85,
        };

        _chordText = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 4, 0, 0),
            Text = SettingsService.FormatHotkeyChord(0, 0),
        };

        _messageText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
        };

        var contentPanel = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
        contentPanel.Children.Add(_headingText);
        contentPanel.Children.Add(_promptText);
        contentPanel.Children.Add(_chordText);
        contentPanel.Children.Add(_messageText);

        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = contentPanel,
        };
        Grid.SetRow(contentScroll, 0);

        _cancelButton = new Button { Content = "Cancel", MinWidth = 88 };
        _cancelButton.Click += (_, _) => Complete(null);
        _saveButton = new Button { Content = "Save", MinWidth = 88 };
        _saveButton.Click += OnSaveClick;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttonPanel.Children.Add(_cancelButton);
        buttonPanel.Children.Add(_saveButton);
        Grid.SetRow(buttonPanel, 1);

        _rootGrid.Children.Add(contentScroll);
        _rootGrid.Children.Add(buttonPanel);
        Content = _rootGrid;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _rootGrid.Focus(FocusState.Programmatic);
    }

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(null);
            return;
        }

        if (HotkeyCaptureHelper.IsModifierKey(e.Key))
        {
            e.Handled = true;
            UpdateDisplay();
            return;
        }

        e.Handled = true;
        _capturedModifiers = HotkeyCaptureHelper.ReadModifierFlags();
        _capturedVirtualKey = HotkeyCaptureHelper.VirtualKeyToVk(e.Key);
        UpdateDisplay();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(null);
        }
    }

    private void UpdateDisplay()
    {
        if (_capturedVirtualKey == 0 && HotkeyCaptureHelper.ReadModifierFlags() != 0)
        {
            _chordText.Text = SettingsService.FormatHotkeyChord(
                HotkeyCaptureHelper.ReadModifierFlags(),
                0).Replace("(none)", "…");
            _validation = HotkeyValidationResult.Error("Press a key combination.");
        }
        else
        {
            _chordText.Text = SettingsService.FormatHotkeyChord(_capturedModifiers, _capturedVirtualKey);
            _validation = HotkeyCaptureHelper.Validate(_capturedModifiers, _capturedVirtualKey);
        }

        _saveButton.IsEnabled = _validation.CanSave;
        if (string.IsNullOrEmpty(_validation.Message))
        {
            _messageText.Visibility = Visibility.Collapsed;
            _messageText.Text = string.Empty;
        }
        else
        {
            _messageText.Text = _validation.Message;
            _messageText.Visibility = Visibility.Visible;
            _messageText.Foreground = _validation.Severity switch
            {
                HotkeyValidationSeverity.Error => new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x6C, 0x75)),
                HotkeyValidationSeverity.Warning => new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xA0, 0x3C)),
                _ => new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            };
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _validation = HotkeyCaptureHelper.Validate(_capturedModifiers, _capturedVirtualKey);
        if (!_validation.CanSave)
        {
            UpdateDisplay();
            return;
        }

        Complete(new HotkeyCaptureResult
        {
            Modifiers = _capturedModifiers,
            VirtualKey = _capturedVirtualKey,
        });
    }

    private void OnResolvedThemeChanged() =>
        DispatcherQueue.TryEnqueue(ApplyThemeFromApp);

    private void ApplyThemeFromApp()
    {
        var palette = AppTheme.Palette;
        var elementTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;

        _rootGrid.Background = palette.Background;
        _rootGrid.RequestedTheme = elementTheme;
        _headingText.Foreground = palette.Primary;
        _promptText.Foreground = palette.Muted;
        _chordText.Foreground = palette.Primary;
        _saveButton.Background = palette.Accent;
        _saveButton.Foreground = palette.Primary;

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyNonClientFrame(hwnd, AppTheme.IsDark);
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("HotkeyCaptureWindow.ApplyNonClientFrame", $"ignored: {ex.Message}"); // ignore
        }
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        AppTheme.ResolvedThemeChanged -= OnResolvedThemeChanged;
        SecondaryWindowPlacement.TryPersist(this, SecondaryWindowPlacement.HotkeyCapture);
        Complete(null);
    }

    private void Complete(HotkeyCaptureResult? result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        WindowChromeHelper.ReleaseModal(this);
        _tcs.TrySetResult(result);
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("HotkeyCaptureWindow.Close", $"ignored: {ex.Message}"); // ignore double-close
        }
    }
}
