using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

#nullable enable

namespace LayoutProfiles.WinUI;

public sealed partial class MainWindow : Window
{
    private Grid _contentRoot = null!;
    private Grid _chromeHost = null!;
    private Grid _rootGrid = null!;
    private WindowPositionController? _positionController;
    private DockRevealPillController? _dockPillController;
    private WindowGlassBackdropHelper? _glassBackdrop;
    private ScrollViewer _profileScrollViewer = null!;
    private Grid _profileChipHost = null!;
    private Grid _profileChipOverlay = null!;
    private GumballGridLayout _gumballLayout = null!;
    private ProfileChipReorderController? _chipReorder;
    private Button _addChip = null!;
    private Border? _addChipFace;
    private FontIcon? _addChipIcon;
    private readonly List<UIElement> _profileChipElements = new();
    private TextBlock _statusText = null!;

    private const int DefaultWindowWidth = 200;
    private const int DefaultWindowHeight = 200;

    // Compact gumballs — slightly larger than Discord channel icons (~24px).
    private const double ProfileChipSize = 30;
    private const double ProfileChipMargin = 3;
    private const double RootGridPaddingSide = 8;
    private const double RootGridPaddingTop = 4;
    private const double RootGridPaddingBottom = 12;
    /// <summary>Extra inset so chips are not clipped by rounding or tight layout.</summary>
    private const int MinChromeSlackPx = 16;

    /// <summary>Horizontal slack so auto-sized dock layouts do not clip rounded chips.</summary>
    private const int PresetWidthSlackPx = 4;
    private const int DockHorizontalSlackDip = 4;
    private const int DockLongAxisSlackDip = 20;
    private const int DockCrossAxisSlackDip = 4;

    /// <summary>Absolute minimum AppWindow client width.</summary>
    private const int MinClientWidthPx = 96;
    private const double ProfileChipFontSize = 13;
    private const double AddChipFontSize = 17;

    private readonly ProfileService _profiles;
    private readonly SettingsService _settings;
    private readonly PythonBridge _python;
    private readonly GitHubReleaseUpdateChecker _updateChecker = new();
    private readonly AutoUpdateCheckService _autoUpdateCheck;
    private readonly UpdateInstallService _updateInstallService = new();
    private readonly WindowGeometryController _geometryController;
    private Dictionary<string, System.Text.Json.JsonElement> _settingsCache = new();
    private bool _busy;
    private AppWindow? _appWindow;
    private bool _skipActivationRefresh;
    private bool _gumballLayoutMeasureHooked;
    private bool _rootLoadedHooked;
    private MenuFlyout? _rootContextFlyout;
    private MenuFlyoutItem? _themeDarkItem;
    private MenuFlyoutItem? _themeLightItem;
    private MenuFlyoutItem? _themeSystemItem;
    private MenuFlyoutItem? _checkForUpdatesItem;
    private MenuFlyoutItem? _versionMenuItem;
    private MenuFlyoutItem? _updateAvailableItem;
    private ReleaseCheckResult? _cachedUpdateResult;
    private bool _updateCheckInProgress;
    private WidgetSizePreset _widgetSizePreset = WidgetSizePreset.OneByOne;
    private WindowDockEdge _layoutDockEdge = WindowDockEdge.None;
    private bool _applyingDockLayout;
    private int _undockedSizedForChipCount = -1;
    private const string ThemeCheckGlyph = "\uE73E";

    public MainWindow()
    {
        StartupTrace.Write("MainWindow.ctor begin");
        // MainWindow.xaml is an empty shell; UI is built in BuildUi() (no InitializeComponent — avoids IDE CS0103 when XAML codegen isn’t loaded).
        _profiles = new ProfileService();
        _settings = new SettingsService();
        _autoUpdateCheck = new AutoUpdateCheckService(_settings, _updateChecker);
        _python = new PythonBridge();
        _geometryController = new WindowGeometryController(
            this,
            () => AppWindowRef,
            _settings,
            () => _settingsCache,
            settings => _settingsCache = settings,
            GetMinClientWidth,
            GetMinClientHeight);

        BuildUi();
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        _rootGrid.Loaded += OnRootGridLoaded;
        StartupTrace.Write("MainWindow.ctor end");
    }

    /// <summary>AppWindow / DWM / profile load — run after <see cref="Window.Activate"/>.</summary>
    public void FinishStartup()
    {
        try
        {
            StartupTrace.Write("FinishStartup begin");
            ConfigureBorderlessWidgetChrome();
            _glassBackdrop = new WindowGlassBackdropHelper(this);
            _glassBackdrop.TryEnable();

            _settingsCache = _settings.Load();
            ApplySavedOrDefaultGeometry();

            StartupShortcutService.SyncFromSettings(_settings.GetRunOnStartup(_settingsCache));

            var themePref = _settings.GetThemePreference(_settingsCache);
            AppTheme.Initialize(themePref);
            SyncThemeMenuChecks();
            ApplyWidgetChromeFromTheme();

            EnsureAddChipLast();
            SafeRefreshProfiles();
            ScheduleGumballLayoutAfterMeasure();
            HookGumballLayoutWhenReady();

            _skipActivationRefresh = true;
            SubscribeToMoveResize();

            EnsurePositionController();
            _dockPillController ??= new DockRevealPillController(() => AppWindowRef);

            StartupTrace.Write(
                $"FinishStartup end chips={_profileChipElements.Count} "
                + $"window={AppWindowRef.Size.Width}x{AppWindowRef.Size.Height}");

            ScheduleBackgroundUpdateCheck();
        }
        catch (Exception ex)
        {
            CrashLog.Write("FinishStartup", ex);
            StartupTrace.Write($"FinishStartup FAILED: {ex.Message}");
        }
    }

    private void BuildUi()
    {
        Title = TryDevTitleSuffix("Snapdesk");

        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var chromeBackground = AppTheme.Palette.Background;
        _chromeHost = new Grid
        {
            Background = chromeBackground,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _rootGrid = new Grid
        {
            Background = transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(
                RootGridPaddingSide,
                RootGridPaddingTop,
                RootGridPaddingSide,
                RootGridPaddingBottom),
        };
        _rootGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
            MinHeight = MinChipRowHeightPx(),
        });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var chipUnit = ChipUnitPx();
        _addChip = CreateAddChip();
        _profileChipElements.Add(_addChip);

        _profileChipHost = new Grid
        {
            Background = transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // Overlay wraps the chip host so the reorder insertion-line can sit as a sibling and
        // never get wiped out by GumballGridLayout.Apply()'s chipHost.Children.Clear().
        // Stretch so the ScrollViewer's HorizontalContentAlignment governs centering, and the
        // chip host's own HorizontalAlignment (managed by GumballGridLayout) keeps working.
        _profileChipOverlay = new Grid
        {
            Background = transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _profileChipOverlay.Children.Add(_profileChipHost);
        _profileScrollViewer = new ScrollViewer
        {
            Background = transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = _profileChipOverlay,
            MinWidth = chipUnit,
            MinHeight = chipUnit,
        };
        _profileScrollViewer.SizeChanged += (_, _) => ApplyGumballLayout(allowFitPresetResize: false);
        _gumballLayout = new GumballGridLayout(_profileScrollViewer, _profileChipHost, chipUnit);
        _chipReorder = new ProfileChipReorderController(
            this,
            _profileChipHost,
            _profileChipOverlay,
            GetProfileChipButtons,
            OnProfileChipsReordered,
            () => _busy);

        Grid.SetRow(_profileScrollViewer, 0);

        _statusText = new TextBlock
        {
            FontSize = 11,
            Foreground = AppTheme.Palette.Muted,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 4,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(_statusText, 1);

        _rootGrid.Children.Add(_profileScrollViewer);
        _rootGrid.Children.Add(_statusText);

        _chromeHost.Children.Add(_rootGrid);

        _contentRoot = new Grid
        {
            Background = transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _contentRoot.Children.Add(_chromeHost);
        BuildRootContextFlyout();
        Content = _contentRoot;
    }

    private void BuildRootContextFlyout()
    {
        _themeDarkItem = CreateThemeMenuItem("Dark", ThemePreference.Dark);
        _themeLightItem = CreateThemeMenuItem("Light", ThemePreference.Light);
        _themeSystemItem = CreateThemeMenuItem("Match system", ThemePreference.System);

        var settingsSub = new MenuFlyoutSubItem { Text = "Settings" };
        settingsSub.Items.Add(_themeDarkItem);
        settingsSub.Items.Add(_themeLightItem);
        settingsSub.Items.Add(_themeSystemItem);
        settingsSub.Items.Add(new MenuFlyoutSeparator());

        _checkForUpdatesItem = new MenuFlyoutItem { Text = "Check for updates" };
        _checkForUpdatesItem.Click += OnCheckForUpdatesClick;

        _updateAvailableItem = new MenuFlyoutItem
        {
            Text = "Update available",
            Visibility = Visibility.Collapsed,
        };
        _updateAvailableItem.Click += OnUpdateAvailableClick;

        settingsSub.Items.Add(_checkForUpdatesItem);
        settingsSub.Items.Add(_updateAvailableItem);
        settingsSub.Items.Add(new MenuFlyoutSeparator());

        var uninstallItem = new MenuFlyoutItem
        {
            Text = "Uninstall Snapdesk...",
            Foreground = AppTheme.Destructive,
        };
        uninstallItem.Click += OnUninstallClick;
        settingsSub.Items.Add(uninstallItem);
        settingsSub.Items.Add(new MenuFlyoutSeparator());
        _versionMenuItem = CreateVersionMenuItem();
        settingsSub.Items.Add(_versionMenuItem);

        var exitItem = new MenuFlyoutItem { Text = "Exit Snapdesk" };
        exitItem.Click += OnExitAppClick;

        _rootContextFlyout = new MenuFlyout
        {
            Items =
            {
                settingsSub,
                new MenuFlyoutSeparator(),
                exitItem,
            },
        };
        _rootContextFlyout.Opening += (_, _) =>
        {
            SyncThemeMenuChecks();
            SyncUpdateMenuFromCache();
            SyncVersionMenuItem();
            _positionController?.PushFlyoutSuppress();
        };
        _rootContextFlyout.Closed += (_, _) => _positionController?.PopFlyoutSuppress();

        _rootGrid.ContextFlyout = _rootContextFlyout;
        _chromeHost.ContextFlyout = _rootContextFlyout;
        _contentRoot.ContextFlyout = _rootContextFlyout;
    }

    private MenuFlyoutItem CreateThemeMenuItem(string label, ThemePreference preference)
    {
        var item = new MenuFlyoutItem { Text = label, Tag = preference };
        item.Click += (_, _) => SetThemePreference(preference);
        return item;
    }

    private static MenuFlyoutItem CreateVersionMenuItem() =>
        new()
        {
            Text = $"Version {AppVersion.Display}",
            IsEnabled = false,
            Opacity = 0.75,
            Foreground = AppTheme.Palette.Muted,
        };

    private void SyncVersionMenuItem()
    {
        if (_versionMenuItem is null)
        {
            return;
        }

        _versionMenuItem.Foreground = AppTheme.Palette.Muted;
    }

    private void SetThemePreference(ThemePreference preference)
    {
        AppTheme.SetPreference(preference);
        var themeValue = AppTheme.ToSettingsValue(preference);
        _settings.MergeAndSave(new Dictionary<string, object?>
        {
            ["gui_theme"] = themeValue,
            ["gui_dark_mode"] = preference == ThemePreference.Dark
                || (preference == ThemePreference.System && AppTheme.IsDark),
        });
        _settingsCache = _settings.Load();
        SyncThemeMenuChecks();
        ApplyWidgetChromeFromTheme();
        StartupTrace.Write($"Theme set to {themeValue} (resolved dark={AppTheme.IsDark})");
    }

    private void SyncThemeMenuChecks()
    {
        if (_themeDarkItem is null)
        {
            return;
        }

        var pref = AppTheme.Preference;
        SetThemeItemCheck(_themeDarkItem, pref == ThemePreference.Dark);
        SetThemeItemCheck(_themeLightItem!, pref == ThemePreference.Light);
        SetThemeItemCheck(_themeSystemItem!, pref == ThemePreference.System);
    }

    private static void SetThemeItemCheck(MenuFlyoutItem item, bool selected)
    {
        item.Icon = selected
            ? new FontIcon { Glyph = ThemeCheckGlyph, FontSize = 14 }
            : null;
    }

    private void OnResolvedThemeChanged()
    {
        DispatcherQueue.TryEnqueue(ApplyWidgetChromeFromTheme);
    }

    private void ApplyWidgetChromeFromTheme()
    {
        var palette = AppTheme.Palette;
        var elementTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        // Parents transparent; acrylic carries the glass. No extra 60% veil on top (that hid the effect).
        _contentRoot.Background = transparent;
        _chromeHost.Background = _glassBackdrop?.IsEnabled == true ? transparent : palette.Background;
        _rootGrid.Background = transparent;
        _profileScrollViewer.Background = transparent;
        _profileChipHost.Background = transparent;
        _contentRoot.RequestedTheme = elementTheme;
        _chromeHost.RequestedTheme = elementTheme;
        _rootGrid.RequestedTheme = elementTheme;
        _profileScrollViewer.RequestedTheme = elementTheme;
        _statusText.Foreground = palette.Muted;
        _addChip.Background = transparent;
        if (_addChipFace is not null)
        {
            _addChipFace.Background = palette.AddChipBackground;
        }

        _glassBackdrop?.ApplyTheme(AppTheme.IsDark);
        if (_addChipIcon is not null)
        {
            _addChipIcon.Foreground = palette.Accent;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyGlassNonClientFrame(hwnd);
            WindowChromeHelper.ApplyNoTaskbarToolWindow(hwnd);
        }
        catch
        {
            // ignore
        }
    }

    private AppWindow AppWindowRef
    {
        get
        {
            if (_appWindow is not null)
            {
                return _appWindow;
            }

            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            return _appWindow;
        }
    }

    private void ConfigureBorderlessWidgetChrome()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            // Drag is handled by WindowPositionController; dock sizing is automatic.

            var appWindow = AppWindowRef;
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
            }

            WindowChromeHelper.ApplyBorderlessTitleBar(appWindow);
        }
        catch
        {
            // ignore presenter configuration failures
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            WindowChromeHelper.ApplyGlassNonClientFrame(hwnd);
            WindowChromeHelper.ApplyNoTaskbarToolWindow(hwnd);
            WindowChromeHelper.ApplyRoundedCorners(this);
        }
        catch
        {
            // ignore DWM failures on older builds
        }

        // System backdrop is enabled in FinishStartup via WindowGlassBackdropHelper (desktop acrylic).

        ApplyMinimumWindowSize();
    }

    private void OnExitAppClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SyncUpdateMenuFromCache()
    {
        if (_updateAvailableItem is null)
        {
            return;
        }

        if (_cachedUpdateResult is { Succeeded: true, IsUpdateAvailable: true, LatestVersion: { } latest })
        {
            _updateAvailableItem.Text = $"Update available ({latest})";
            _updateAvailableItem.Visibility = Visibility.Visible;
            return;
        }

        _updateAvailableItem.Visibility = Visibility.Collapsed;
    }

    private void ScheduleBackgroundUpdateCheck()
    {
        _ = RunUpdateCheckAsync(force: false, showDialogs: false);
    }

    private async Task RunUpdateCheckAsync(bool force, bool showDialogs)
    {
        if (_updateCheckInProgress)
        {
            return;
        }

        _updateCheckInProgress = true;
        var previousText = _checkForUpdatesItem?.Text;
        if (showDialogs && _checkForUpdatesItem is not null)
        {
            _checkForUpdatesItem.IsEnabled = false;
            _checkForUpdatesItem.Text = "Checking for updates…";
        }

        try
        {
            var (skipped, result) = await _autoUpdateCheck
                .CheckAsync(_settingsCache, force)
                .ConfigureAwait(showDialogs);

            if (skipped)
            {
                StartupTrace.Write("Update check skipped (checked within 24h)");
                return;
            }

            if (result is null)
            {
                return;
            }

            ReloadSettingsCache();
            ApplyUpdateCheckResultOnUiThread(result);

            if (showDialogs)
            {
                ShowManualUpdateCheckDialogs(result);
            }
            else if (result.Succeeded && result.IsUpdateAvailable)
            {
                PromptInstallUpdateOnUiThread(result);
            }
            else
            {
                LogSilentUpdateCheckResult(result);
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"Update check error: {ex.Message}");
        }
        finally
        {
            _updateCheckInProgress = false;
            if (showDialogs && _checkForUpdatesItem is not null)
            {
                _checkForUpdatesItem.IsEnabled = true;
                _checkForUpdatesItem.Text = previousText ?? "Check for updates";
            }
        }
    }

    private void ApplyUpdateCheckResultOnUiThread(ReleaseCheckResult result)
    {
        void Apply()
        {
            _cachedUpdateResult = result;
            SyncUpdateMenuFromCache();
        }

        var queue = DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            Apply();
            return;
        }

        queue.TryEnqueue(Apply);
    }

    private void ReloadSettingsCache()
    {
        _settingsCache = _settings.Load();
    }

    private static void LogSilentUpdateCheckResult(ReleaseCheckResult result)
    {
        if (!result.Succeeded)
        {
            StartupTrace.Write($"Update check failed: {result.ErrorMessage ?? "unknown"}");
            return;
        }

        if (result.IsUpdateAvailable && result.LatestVersion is not null)
        {
            StartupTrace.Write(
                $"Update available: {result.LatestVersion} (current {result.CurrentVersion})");
            return;
        }

        StartupTrace.Write($"Up to date ({result.CurrentVersion})");
    }

    private void ShowManualUpdateCheckDialogs(ReleaseCheckResult result)
    {
        if (!result.Succeeded)
        {
            UpdateInstallService.ShowInstallFailedWithGithubFallback(
                result.ErrorMessage ?? "Could not check for updates.",
                result.ReleasePageUrl);
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            NativeMessageBox.Show(
                "Snapdesk",
                result.StatusMessage,
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);
            return;
        }

        if (PromptInstallUpdate(result))
        {
            _ = RunInstallUpdateAsync(result);
        }
    }

    private void PromptInstallUpdateOnUiThread(ReleaseCheckResult result)
    {
        void Prompt()
        {
            if (PromptInstallUpdate(result))
            {
                _ = RunInstallUpdateAsync(result);
            }
        }

        var queue = DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            Prompt();
            return;
        }

        queue.TryEnqueue(Prompt);
    }

    private static bool PromptInstallUpdate(ReleaseCheckResult result)
    {
        var versionLabel = result.LatestVersion?.ToString(3)
                             ?? result.LatestTag
                             ?? "newer";
        var choice = NativeMessageBox.Show(
            "Snapdesk",
            $"Update available: {versionLabel} (you have {result.CurrentVersion}). Install now?",
            NativeMessageBox.MbYesNo | NativeMessageBox.MbIconQuestion);
        return choice == NativeMessageBox.IdYes;
    }

    private async Task RunInstallUpdateAsync(ReleaseCheckResult result)
    {
        await _updateInstallService.TryInstallUpdateAsync(result).ConfigureAwait(true);
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_checkForUpdatesItem is null)
        {
            return;
        }

        await RunUpdateCheckAsync(force: true, showDialogs: true).ConfigureAwait(true);
    }

    private void OnUpdateAvailableClick(object sender, RoutedEventArgs e)
    {
        if (_cachedUpdateResult is { Succeeded: true, IsUpdateAvailable: true })
        {
            PromptInstallUpdateOnUiThread(_cachedUpdateResult);
            return;
        }

        var url = _cachedUpdateResult?.ReleasePageUrl ?? GitHubReleaseUpdateChecker.ReleasesLatestPage;
        _ = OpenReleasePageAsync(url);
    }

    private static async Task OpenReleasePageAsync(string url)
    {
        try
        {
            _ = await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            NativeMessageBox.Show(
                "Snapdesk",
                $"Could not open the browser: {ex.Message}",
                NativeMessageBox.MbOk | NativeMessageBox.MbIconError);
        }
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (!await UninstallSnapdesk.ConfirmAsync())
        {
            return;
        }

        try
        {
            UninstallSnapdesk.LaunchDetachedAndExit();
        }
        catch (Exception ex)
        {
            ShowErrorMessageBox("Snapdesk", $"Could not start uninstall: {ex.Message}");
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        if (_skipActivationRefresh)
        {
            _skipActivationRefresh = false;
            return;
        }

        SafeRefreshProfiles();
        ApplyGumballLayout();
    }

    private void EnsurePositionController()
    {
        _positionController ??= new WindowPositionController(
            this,
            _chromeHost,
            () => AppWindowRef,
            ScheduleGeometrySave,
            UpdateDockRevealIndicator);
        _positionController.SyncDockStateFromWindow();
    }

    private void UpdateDockRevealIndicator(WindowDockEdge edge, bool isAutoHidden, DisplayArea? dockedDisplay)
    {
        _dockPillController ??= new DockRevealPillController(() => AppWindowRef);
        _dockPillController.Update(edge, isAutoHidden, dockedDisplay);

        var dockEdgeChanged = _layoutDockEdge != edge;
        if (dockEdgeChanged)
        {
            _layoutDockEdge = edge;
            _gumballLayout.Reset();
            DispatcherQueue.TryEnqueue(() => ApplyGumballLayout(allowFitPresetResize: false));
        }

        // Hide widget while rolled up; the overlay pill is the only affordance.
        var hidden = isAutoHidden && edge != WindowDockEdge.None;
        var opacity = hidden ? 0 : 1;
        _chromeHost.Opacity = opacity;
        _rootGrid.Opacity = opacity;

        if (!hidden && _positionController?.IsTracking != true)
        {
            ApplyDockLayout(edge, dockedDisplay);
        }
    }

    private void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        if (_rootLoadedHooked)
        {
            return;
        }

        _rootLoadedHooked = true;
        ApplyMinimumWindowSize();
        EnsurePositionController();
        StartupTrace.Write(
            $"RootLoaded scroll={_profileScrollViewer.ActualWidth:F0}x{_profileScrollViewer.ActualHeight:F0} "
            + $"profiles={_profileChipElements.Count}");
        EnsureAddChipLast();
        _gumballLayout.Reset();
        SafeRefreshProfiles();
        ApplyGumballLayout();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        PersistWindowGeometry(force: true);

        AppTheme.ResolvedThemeChanged -= OnResolvedThemeChanged;

        try
        {
            WindowChromeHelper.RemoveMinimumTrackSize(this);
            _dockPillController?.Close();
            _glassBackdrop?.Dispose();
            _glassBackdrop = null;
        }
        catch
        {
            // ignore
        }

        SecondaryWindowTracker.CloseAll();
    }

    private void ApplySavedOrDefaultGeometry()
    {
        ApplyMinimumWindowSize();
        _widgetSizePreset = _geometryController.RestoreSavedOrDefaultGeometry(
            DefaultWindowWidth,
            DefaultWindowHeight,
            ApplyWidgetSizePreset);
        ApplyMinimumWindowSize();
    }

    private void SubscribeToMoveResize()
    {
        try
        {
            AppWindowRef.Changed += OnAppWindowChanged;
        }
        catch
        {
            // ignore
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            ApplyMinimumWindowSize();
            // ViewportWidth lags AppWindow resize; re-layout after the scroll viewer measures.
            DispatcherQueue.TryEnqueue(() => ApplyGumballLayout(allowFitPresetResize: false));
        }

        if (args.DidPositionChange || args.DidSizeChange)
        {
            ScheduleGeometrySave();
            RefreshDockRevealIndicatorFromWindowGeometry();
        }
    }

    private void RefreshDockRevealIndicatorFromWindowGeometry()
    {
        try
        {
            if (_positionController is null)
            {
                return;
            }

            DisplayArea? display = null;
            if (_positionController.TryGetDockedDisplay(out var resolved))
            {
                display = resolved;
            }

            UpdateDockRevealIndicator(
                _positionController.DockEdge,
                _positionController.IsAutoHidden,
                display);
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyGumballLayout(bool allowFitPresetResize = true)
    {
        EnsureAddChipLast();
        var clientWidth = WidthForGumballColumnCount();
        var result = _gumballLayout.Apply(
            _profileChipElements,
            clientWidth,
            ClientHeightForChips(),
            ChipsPerRowForLayout());

        var chipCount = _profileChipElements.Count;
        var needsUndockedResize = allowFitPresetResize
            && _layoutDockEdge == WindowDockEdge.None
            && _geometryController.IsReadyForPersist
            && chipCount != _undockedSizedForChipCount;

        if (!result.Changed)
        {
            if (needsUndockedResize && _profileScrollViewer.ActualWidth > 0)
            {
                MaybeResizeUndockedFromGumball();
            }

            return;
        }

        StartupTrace.Write(
            $"ApplyGumballLayout chips={result.Count} grid={result.Rows}x{result.ChipsPerRow} "
            + $"host={result.HostWidth}x{result.HostHeight} clientW={result.ClientWidth} "
            + $"scroll={_profileScrollViewer.ActualWidth:F0}x{_profileScrollViewer.ActualHeight:F0}");

        if (_profileScrollViewer.ActualWidth <= 0)
        {
            EnsureGumballLayoutWhenMeasured();
        }
        else if (allowFitPresetResize)
        {
            MaybeResizeUndockedFromGumball();
        }
    }

    private void EnsureAddChipLast()
    {
        if (_addChip.Parent is Panel parent)
        {
            parent.Children.Remove(_addChip);
        }

        _addChip.Visibility = Visibility.Visible;
        if (_profileChipElements.Count == 0 || _profileChipElements[^1] != _addChip)
        {
            _profileChipElements.Remove(_addChip);
            _profileChipElements.Add(_addChip);
        }
    }

    private static int ChipUnitPx() =>
        (int)Math.Ceiling(ProfileChipSize + 2 * ProfileChipMargin);

    /// <summary>Inner width available to the scroll viewer (window minus chrome/padding).</summary>
    private double WindowContentWidthForChips()
    {
        try
        {
            return Math.Max(
                0,
                AppWindowRef.Size.Width - 2 * RootGridPaddingSide);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Width used for column count — window inner width, not inflated scroll/content measure.</summary>
    private int WidthForGumballColumnCount()
    {
        // Prefer measured viewport so a bad transient window size does not reflow to full-monitor width.
        var measured = ClientWidthForChips();
        if (measured > 0)
        {
            return measured;
        }

        var windowCap = (int)Math.Floor(WindowContentWidthForChips());
        if (windowCap > 0)
        {
            return Math.Min(windowCap, WindowGeometryHelper.MaxWidgetWidth);
        }

        return ChipUnitPx();
    }

    private int ClientWidthForChips()
    {
        var viewport = _profileScrollViewer.ViewportWidth;
        var actual = _profileScrollViewer.ActualWidth;
        double measured = 0;
        if (viewport > 0 && actual > 0)
        {
            measured = Math.Min(viewport, actual);
        }
        else if (viewport > 0)
        {
            measured = viewport;
        }
        else if (actual > 0)
        {
            measured = actual;
        }

        var windowCap = WindowContentWidthForChips();
        if (windowCap > 0)
        {
            measured = measured > 0 ? Math.Min(measured, windowCap) : windowCap;
        }

        if (measured <= 0)
        {
            measured = ChipUnitPx();
        }

        return (int)Math.Floor(measured);
    }

    /// <summary>Inner height available to the scroll viewer (client minus padding and status).</summary>
    private double WindowContentHeightForChips()
    {
        try
        {
            var height = AppWindowRef.Size.Height - RootGridPaddingTop - RootGridPaddingBottom;
            if (_statusText.Visibility == Visibility.Visible)
            {
                height -= _statusText.ActualHeight + 8;
            }

            return Math.Max(0, height);
        }
        catch
        {
            return 0;
        }
    }

    private int ClientHeightForChips()
    {
        var viewport = _profileScrollViewer.ViewportHeight;
        var actual = _profileScrollViewer.ActualHeight;
        double measured = 0;
        if (viewport > 0 && actual > 0)
        {
            measured = Math.Min(viewport, actual);
        }
        else if (viewport > 0)
        {
            measured = viewport;
        }
        else if (actual > 0)
        {
            measured = actual;
        }

        var windowCap = WindowContentHeightForChips();
        if (windowCap > 0)
        {
            measured = measured > 0 ? Math.Min(measured, windowCap) : windowCap;
        }

        if (measured <= 0)
        {
            measured = ChipUnitPx();
        }

        return (int)Math.Floor(measured);
    }

    /// <summary>Re-run grid layout after the first measure pass so chips are visible on cold start.</summary>
    private void ScheduleGumballLayoutAfterMeasure()
    {
        DispatcherQueue.TryEnqueue(() =>
            DispatcherQueue.TryEnqueue(() =>
            {
                EnsureAddChipLast();
                _gumballLayout.Reset();
                ApplyGumballLayout();
                if (_profileScrollViewer.ActualWidth <= 0)
                {
                    EnsureGumballLayoutWhenMeasured();
                }
            }));
    }

    private void HookGumballLayoutWhenReady()
    {
        DispatcherQueue.TryEnqueue(() =>
            DispatcherQueue.TryEnqueue(() =>
            {
                EnsureAddChipLast();
                _gumballLayout.Reset();
                ApplyGumballLayout();
            }));
    }

    private void EnsureGumballLayoutWhenMeasured()
    {
        if (_gumballLayoutMeasureHooked)
        {
            return;
        }

        _gumballLayoutMeasureHooked = true;

        void OnMeasured(object? sender, object e)
        {
            if (_profileScrollViewer.ActualWidth <= 0)
            {
                return;
            }

            _profileScrollViewer.Loaded -= OnMeasured;
            _profileScrollViewer.SizeChanged -= OnMeasuredSizeChanged;
            _gumballLayout.Reset();
            ApplyGumballLayout();
        }

        void OnMeasuredSizeChanged(object sender, SizeChangedEventArgs e) => OnMeasured(sender, e);

        _profileScrollViewer.Loaded += OnMeasured;
        _profileScrollViewer.SizeChanged += OnMeasuredSizeChanged;
    }

    /// <summary>Inner width for one full gumball column (chip + layout slack).</summary>
    private static int MinChipColumnWidthPx() =>
        ChipUnitPx() + MinChromeSlackPx;

    /// <summary>Minimum scroll viewport height for one full gumball row.</summary>
    private static int MinChipRowHeightPx() =>
        ChipUnitPx() + MinChromeSlackPx;

    /// <summary>Absolute resize floor (Win32 track + clamp).</summary>
    private static int GetMinClientWidth() => MinClientWidthPx;

    private static (int Width, int Height) ComputeClientSizeForGrid(int columns, int rows)
    {
        var chipUnit = ChipUnitPx();
        var width = (int)Math.Ceiling(2 * RootGridPaddingSide + columns * chipUnit + PresetWidthSlackPx);
        var height = (int)Math.Ceiling(
            RootGridPaddingTop
            + RootGridPaddingBottom
            + rows * chipUnit
            + MinChromeSlackPx);
        return (width, height);
    }

    /// <summary>Square client size for an undocked gumball grid (profiles + add chip).</summary>
    private (int Width, int Height) ComputeUndockedSquareClientSize(int columns, int rows)
    {
        var gumballSide = Math.Max(columns, rows) * ChipUnitPx();
        var statusReserve = _statusText.Visibility == Visibility.Visible
            ? (int)Math.Ceiling(_statusText.ActualHeight + 8)
            : 0;
        var width = (int)Math.Ceiling(2 * RootGridPaddingSide + gumballSide + PresetWidthSlackPx);
        var height = (int)Math.Ceiling(
            RootGridPaddingTop
            + RootGridPaddingBottom
            + gumballSide
            + MinChromeSlackPx
            + statusReserve);
        var side = Math.Max(width, height);
        side = Math.Max(side, GetMinClientWidth());
        side = Math.Max(side, GetMinClientHeight());
        return WindowGeometryHelper.ClampWidgetSize(
            side,
            side,
            GetMinClientWidth(),
            GetMinClientHeight());
    }

    private static (int Columns, int Rows) ResolveUndockedSquareGrid(int chipCount)
    {
        var chipsPerRow = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, chipCount)));
        var columns = Math.Min(chipsPerRow, chipCount);
        var rows = (chipCount + chipsPerRow - 1) / chipsPerRow;
        return (columns, rows);
    }

    private int UndockedIdealChipsPerRow() =>
        (int)Math.Ceiling(Math.Sqrt(Math.Max(1, _profileChipElements.Count)));

    private (int Width, int Height) ComputeClientSizeForPreset(WidgetSizePreset preset, int columns, int rows)
    {
        var gridSize = ComputeClientSizeForGrid(columns, rows);
        var width = gridSize.Width;
        if (WidgetSizePresets.TryGetTunedClientWidth(preset, out var preferredWidth))
        {
            width = preferredWidth;
        }

        width = Math.Max(width, GetMinClientWidth());

        // Fixed presets (1×1, 1×3, 2×1, …) adjust width only; fit presets size both axes.
        int height;
        if (WidgetSizePresets.IsFit(preset))
        {
            height = Math.Max(gridSize.Height, GetMinClientHeight());
        }
        else
        {
            try
            {
                height = Math.Max(AppWindowRef.Size.Height, GetMinClientHeight());
            }
            catch
            {
                height = Math.Max(gridSize.Height, GetMinClientHeight());
            }
        }

        return (width, height);
    }

    private (int Columns, int Rows) ResolvePresetGrid(WidgetSizePreset preset)
    {
        var chipCount = Math.Max(1, _profileChipElements.Count);
        var (columns, rows) = WidgetSizePresets.GetGrid(preset, chipCount);
        if (!WidgetSizePresets.IsFit(preset))
        {
            return (columns, rows);
        }

        var chipUnit = ChipUnitPx();
        if (WidgetSizePresets.IsVerticalFit(preset))
        {
            var overhead = (int)Math.Ceiling(
                RootGridPaddingTop + RootGridPaddingBottom + MinChromeSlackPx);
            var maxRows = Math.Max(1, (WindowGeometryHelper.MaxWidgetHeight - overhead) / chipUnit);
            rows = Math.Min(rows, maxRows);
            return (columns, rows);
        }

        if (WidgetSizePresets.IsHorizontalFit(preset))
        {
            var overhead = (int)Math.Ceiling(2 * RootGridPaddingSide + PresetWidthSlackPx);
            var maxColumns = Math.Max(1, (WindowGeometryHelper.MaxWidgetWidth - overhead) / chipUnit);
            columns = Math.Min(columns, maxColumns);
            return (columns, rows);
        }

        // 2×fit: cap rows when many chips
        var rowOverhead = (int)Math.Ceiling(
            RootGridPaddingTop + RootGridPaddingBottom + MinChromeSlackPx);
        var maxFitRows = Math.Max(1, (WindowGeometryHelper.MaxWidgetHeight - rowOverhead) / chipUnit);
        rows = Math.Min(rows, maxFitRows);
        return (columns, rows);
    }

    private (int Columns, int Rows) ResolveDockGrid(WindowDockEdge edge)
    {
        var chipCount = Math.Max(1, _profileChipElements.Count);
        return edge switch
        {
            WindowDockEdge.Left or WindowDockEdge.Right => (1, chipCount),
            WindowDockEdge.Top or WindowDockEdge.Bottom => (chipCount, 1),
            _ => ResolvePresetGrid(_widgetSizePreset),
        };
    }

    private int? ChipsPerRowForLayout()
    {
        return _layoutDockEdge switch
        {
            WindowDockEdge.Left or WindowDockEdge.Right => 1,
            WindowDockEdge.Top or WindowDockEdge.Bottom => ResolveDockGrid(_layoutDockEdge).Columns,
            _ => UndockedIdealChipsPerRow(),
        };
    }

    private void ApplyDockLayout(WindowDockEdge edge, DisplayArea? display)
    {
        if (_applyingDockLayout || edge == WindowDockEdge.None || display is null)
        {
            return;
        }

        try
        {
            _applyingDockLayout = true;
            var (width, height) = ComputeDockWindowSize(edge, display);

            var appWindow = AppWindowRef;
            var targetSize = new SizeInt32(width, height);
            var currentSize = appWindow.Size;
            var changed = currentSize.Width != width || currentSize.Height != height;
            if (changed)
            {
                WindowChromeHelper.ApplyMinimumTrackSize(this, 1, 1);
                StartupTrace.Write($"ApplyDockLayout edge={edge} target={width}x{height} scale={RasterizationScale():F2}");
                appWindow.Resize(targetSize);
            }

            var actualSize = appWindow.Size;
            var targetPosition = GetDockPositionForSize(appWindow.Position, actualSize, edge, display);
            var currentPosition = appWindow.Position;
            if (currentPosition.X != targetPosition.X || currentPosition.Y != targetPosition.Y)
            {
                appWindow.Move(targetPosition);
                changed = true;
            }

            if (changed)
            {
                _gumballLayout.Reset();
                DispatcherQueue.TryEnqueue(() => ApplyGumballLayout(allowFitPresetResize: false));
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"ApplyDockLayout failed: {ex.Message}");
        }
        finally
        {
            _applyingDockLayout = false;
        }
    }

    private void ApplyCurrentDockLayout()
    {
        if (_layoutDockEdge == WindowDockEdge.None || _positionController is null)
        {
            return;
        }

        if (_positionController.TryGetDockedDisplay(out var display))
        {
            ApplyDockLayout(_layoutDockEdge, display);
        }
    }

    private static PointInt32 GetDockPositionForSize(
        PointInt32 currentPosition,
        SizeInt32 targetSize,
        WindowDockEdge edge,
        DisplayArea display)
    {
        var work = display.WorkArea;
        var x = Math.Clamp(
            currentPosition.X,
            work.X,
            Math.Max(work.X, work.X + work.Width - targetSize.Width));
        var y = Math.Clamp(
            currentPosition.Y,
            work.Y,
            Math.Max(work.Y, work.Y + work.Height - targetSize.Height));

        return edge switch
        {
            WindowDockEdge.Left => new PointInt32(work.X, y),
            WindowDockEdge.Right => new PointInt32(work.X + work.Width - targetSize.Width, y),
            WindowDockEdge.Top => new PointInt32(x, work.Y),
            WindowDockEdge.Bottom => new PointInt32(x, work.Y + work.Height - targetSize.Height),
            _ => currentPosition,
        };
    }

    private (int Width, int Height) ComputeDockWindowSize(WindowDockEdge edge, DisplayArea display)
    {
        var (columns, rows) = ResolveDockGrid(edge);
        var chipUnit = ChipUnitPx();
        var crossAxisDip = chipUnit + DockCrossAxisSlackDip;
        double widthDip = columns * chipUnit + DockLongAxisSlackDip;
        double heightDip = rows * chipUnit + DockLongAxisSlackDip;
        var scale = RasterizationScale();
        var crossAxisPx = Math.Max(
            GetMinClientWidth(),
            (int)Math.Ceiling(crossAxisDip * scale));

        if (edge is WindowDockEdge.Left or WindowDockEdge.Right)
        {
            widthDip = crossAxisPx / scale;
        }

        if (edge is WindowDockEdge.Top or WindowDockEdge.Bottom)
        {
            heightDip = crossAxisPx / scale;
        }

        var width = (int)Math.Ceiling(widthDip * scale);
        var height = (int)Math.Ceiling(heightDip * scale);
        width = Math.Clamp(width, GetMinClientWidth(), Math.Max(GetMinClientWidth(), display.WorkArea.Width));
        height = Math.Clamp(height, GetMinClientHeight(), Math.Max(GetMinClientHeight(), display.WorkArea.Height));
        return (width, height);
    }

    private double RasterizationScale()
    {
        try
        {
            if (_contentRoot.XamlRoot is not null && _contentRoot.XamlRoot.RasterizationScale > 0)
            {
                return _contentRoot.XamlRoot.RasterizationScale;
            }
        }
        catch
        {
            // ignore
        }

        return 1;
    }

    private void ApplyWidgetSizePreset(WidgetSizePreset preset, bool persist)
    {
        try
        {
            if (_layoutDockEdge != WindowDockEdge.None)
            {
                var (columns, rows) = ResolvePresetGrid(preset);
                var (width, height) = ComputeClientSizeForPreset(preset, columns, rows);
                var size = AppWindowRef.Size;
                if (size.Width != width || size.Height != height)
                {
                    AppWindowRef.Resize(new SizeInt32(width, height));
                }
            }

            ApplyMinimumWindowSize();
            _gumballLayout.Reset();
            DispatcherQueue.TryEnqueue(() => ApplyGumballLayout());

            if (persist)
            {
                PersistWindowGeometry(force: true);
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"ApplyWidgetSizePreset failed: {ex.Message}");
        }
    }

    private void MaybeResizeUndockedFromGumball()
    {
        if (_layoutDockEdge != WindowDockEdge.None || !_geometryController.IsReadyForPersist)
        {
            return;
        }

        try
        {
            var chipCount = Math.Max(1, _profileChipElements.Count);
            var (columns, rows) = ResolveUndockedSquareGrid(chipCount);
            var (width, height) = ComputeUndockedSquareClientSize(columns, rows);
            var size = AppWindowRef.Size;
            if (size.Width == width && size.Height == height)
            {
                _undockedSizedForChipCount = chipCount;
                return;
            }

            AppWindowRef.Resize(new SizeInt32(width, height));
            _undockedSizedForChipCount = chipCount;
            _gumballLayout.Reset();
            DispatcherQueue.TryEnqueue(() => ApplyGumballLayout(allowFitPresetResize: false));
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Client height: padding + one full gumball row (+ status line when visible).</summary>
    private int GetMinClientHeight()
    {
        var statusReserve = _statusText.Visibility == Visibility.Visible
            ? (int)Math.Ceiling(_statusText.ActualHeight + 8)
            : 0;
        return (int)Math.Ceiling(
            RootGridPaddingTop
            + RootGridPaddingBottom
            + MinChipRowHeightPx()
            + statusReserve);
    }

    private void ApplyMinimumWindowSize()
    {
        try
        {
            var minH = GetMinClientHeight();
            var minW = GetMinClientWidth();

            var size = AppWindowRef.Size;
            var w = Math.Max(size.Width, minW);
            var h = Math.Max(size.Height, minH);
            if (w != size.Width || h != size.Height)
            {
                AppWindowRef.Resize(new SizeInt32(w, h));
            }
        }
        catch
        {
            // ignore
        }
    }

    private void ScheduleGeometrySave()
    {
        _geometryController.ScheduleSave();
    }

    private void PersistWindowGeometry(bool force = false)
    {
        _geometryController.Persist(force);
    }

    private void SafeRefreshProfiles()
    {
        try
        {
            RefreshProfiles();
        }
        catch (Exception ex)
        {
            CrashLog.Write("RefreshProfiles", ex);
            _statusText.Text = "Profile list failed to load.";
            _statusText.Visibility = Visibility.Visible;
            EnsureAddChipOnly();
        }
    }

    private void RefreshProfiles()
    {
        _chipReorder?.DetachAll();
        _profileChipElements.Clear();

        IReadOnlyList<ProfileRow> rows;
        try
        {
            rows = ProfileService.ApplyDisplayOrder(
                _profiles.ListProfiles(),
                _settings.GetProfileOrder(_settingsCache));
        }
        catch (Exception ex)
        {
            CrashLog.Write("ListProfiles", ex);
            rows = Array.Empty<ProfileRow>();
        }

        foreach (var row in rows)
        {
            _profileChipElements.Add(CreateProfileChip(row));
        }

        _profileChipElements.Add(_addChip);

        _gumballLayout.Reset();
        ApplyGumballLayout();
        ApplyCurrentDockLayout();
        StartupTrace.Write($"RefreshProfiles count={_profileChipElements.Count}");
    }

    /// <summary>Guarantees the + chip is mounted when profile load or layout fails.</summary>
    private void EnsureAddChipOnly()
    {
        try
        {
            _profileChipElements.Clear();
            _profileChipElements.Add(_addChip);
            _gumballLayout.Reset();
            ApplyGumballLayout();
            ApplyCurrentDockLayout();
            ScheduleGumballLayoutAfterMeasure();
        }
        catch (Exception ex)
        {
            CrashLog.Write("EnsureAddChipOnly", ex);
        }
    }

    private Button CreateAddChip()
    {
        var accent = Application.Current.Resources[AppTheme.WidgetAccent] as SolidColorBrush
            ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0x8B, 0xF5));
        var addBg = Application.Current.Resources[AppTheme.WidgetAddChipBackground] as SolidColorBrush
            ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x48, 0x4C, 0x54));
        var radius = ProfileChipSize / 2;
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _addChipFace = new Border
        {
            Width = ProfileChipSize,
            Height = ProfileChipSize,
            CornerRadius = new CornerRadius(radius),
            Background = addBg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _addChipIcon = new FontIcon
        {
            Glyph = "\uE710",
            FontSize = 12,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var face = new Grid
        {
            Width = ProfileChipSize,
            Height = ProfileChipSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        face.Children.Add(_addChipFace);
        face.Children.Add(_addChipIcon);

        var btn = new Button
        {
            Width = ChipUnitPx(),
            Height = ChipUnitPx(),
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(ChipUnitPx() / 2.0),
            Background = transparent,
            BorderThickness = new Thickness(0),
            IsEnabled = !_busy,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = face,
        };
        ToolTipService.SetToolTip(btn, "Save current layout as new profile (click or right-click)");
        btn.Click += OnAddChipClick;
        btn.RightTapped += OnAddChipRightTapped;
        return btn;
    }

    private UIElement CreateProfileChip(ProfileRow row)
    {
        var baseColor = ProfileColors.BaseColorForName(row.DisplayName);
        var fg = ProfileColors.ForegroundFor(baseColor);
        var brush = new SolidColorBrush(baseColor);

        var radius = ProfileChipSize / 2;
        var btn = new Button
        {
            Width = ProfileChipSize,
            Height = ProfileChipSize,
            Margin = new Thickness(ProfileChipMargin),
            CornerRadius = new CornerRadius(radius),
            Background = brush,
            BorderThickness = new Thickness(0),
            IsEnabled = !_busy,
            Tag = row,
            Content = new TextBlock
            {
                Text = ProfileColors.Initial(row.DisplayName),
                FontSize = ProfileChipFontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTipService.SetToolTip(
            btn,
            $"{row.DisplayName} ({row.WindowCount} windows) — click restore, drag to reorder, right-click edit or delete");
        _chipReorder?.Attach(btn);

        var editItem = new MenuFlyoutItem { Text = "Edit layout" };
        editItem.Click += async (_, _) => await RunEditAsync(row);
        var deleteItem = new MenuFlyoutItem { Text = "Delete profile" };
        deleteItem.Click += async (_, _) => await ConfirmAndDeleteProfileAsync(row);
        var chipFlyout = new MenuFlyout { Items = { editItem, deleteItem } };
        chipFlyout.Opening += (_, _) => _positionController?.PushFlyoutSuppress();
        chipFlyout.Closed += (_, _) => _positionController?.PopFlyoutSuppress();
        btn.ContextFlyout = chipFlyout;
        btn.RightTapped += (_, e) =>
        {
            e.Handled = true;
            FlyoutBase.ShowAttachedFlyout(btn);
        };

        btn.Click += OnProfileChipClick;

        return btn;
    }

    private async void OnProfileChipClick(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: ProfileRow row })
        {
            return;
        }

        if (_chipReorder?.ShouldSuppressClick(sender as Button) == true)
        {
            return;
        }

        await RunRestoreAsync(row);
    }

    private IReadOnlyList<Button> GetProfileChipButtons()
    {
        var list = new List<Button>();
        foreach (var element in _profileChipElements)
        {
            if (element is Button { Tag: ProfileRow } btn)
            {
                list.Add(btn);
            }
        }

        return list;
    }

    private void OnProfileChipsReordered(int fromIndex, int insertBeforeIndex)
    {
        var profileChips = GetProfileChipButtons()
            .Cast<UIElement>()
            .ToList();
        var profileCount = profileChips.Count;
        if (profileCount < 2
            || fromIndex < 0
            || fromIndex >= profileCount
            || insertBeforeIndex < 0
            || insertBeforeIndex > profileCount)
        {
            RebuildProfileChipElements(profileChips);
            ForceChipHostRebuild(allowFitPresetResize: false);
            return;
        }

        var to = insertBeforeIndex;
        if (fromIndex < to)
        {
            to--;
        }

        if (fromIndex == to)
        {
            // No-op: same slot. Still reset the visual tree to be safe.
            RebuildProfileChipElements(profileChips);
            ForceChipHostRebuild(allowFitPresetResize: false);
            return;
        }

        var item = profileChips[fromIndex];
        profileChips.RemoveAt(fromIndex);
        profileChips.Insert(to, item);
        StartupTrace.Write($"Profile order: moved {fromIndex} insert-before {insertBeforeIndex} (to={to})");

        RebuildProfileChipElements(profileChips);
        ForceChipHostRebuild();
        TryPersistProfileOrder();
    }

    /// <summary>
    /// Force <see cref="_profileChipHost"/> to be torn down and rebuilt from
    /// <see cref="_profileChipElements"/>. Used after reorder so the visual tree always
    /// matches the model regardless of what state GumballGridLayout cached.
    /// </summary>
    private void ForceChipHostRebuild(bool allowFitPresetResize = true)
    {
        try
        {
            _profileChipHost.Children.Clear();
        }
        catch
        {
            // ignore
        }

        _gumballLayout.Reset();
        ApplyGumballLayout(allowFitPresetResize);
        _profileChipHost.InvalidateMeasure();
        _profileChipHost.InvalidateArrange();
    }

    private void RebuildProfileChipElements(IReadOnlyList<UIElement> profileChips)
    {
        _profileChipElements.Clear();
        foreach (var chip in profileChips)
        {
            _profileChipElements.Add(chip);
        }

        _profileChipElements.Add(_addChip);
        EnsureAddChipLast();
    }

    private void TryPersistProfileOrder()
    {
        try
        {
            var paths = GetProfileChipButtons()
                .Select(b => ((ProfileRow)b.Tag!).FilePath)
                .ToList();
            _settings.SaveProfileOrder(paths);
            _settingsCache = _settings.Load();
        }
        catch (Exception ex)
        {
            CrashLog.Write("PersistProfileOrder", ex);
            StartupTrace.Write($"PersistProfileOrder failed: {ex.Message}");
            SetErrorStatus("Profile order changed, but could not be saved.");
        }
    }

    private async Task RunRestoreAsync(ProfileRow row)
    {
        SetBusy(true);
        ClearStatus();

        PythonRunResult result;
        try
        {
            result = await Task.Run(
                () => _python.RestoreProfileAsync(row.FilePath).GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Restore failed: {ex.Message}");
            SetBusy(false);
            return;
        }

        if (result.Success)
        {
            ClearStatus();
        }
        else
        {
            var err = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            SetErrorStatus(string.IsNullOrWhiteSpace(err) ? "Restore failed." : err.Split('\n')[0]);
        }

        SetBusy(false);
    }

    private async void OnAddChipClick(object sender, RoutedEventArgs e) =>
        await RunCreateProfileAsync();

    private async void OnAddChipRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        await RunCreateProfileAsync();
    }

    private async Task RunCreateProfileAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        ProfileEditorResult? result = null;
        _positionController?.PushFlyoutSuppress();
        try
        {
            StartupTrace.Write("RunCreateProfileAsync: opening layout editor");
            CrashLog.WriteDiagnostic("RunCreateProfileAsync", "before ProfileEditorDialog.ShowCreateAsync");
            result = await ProfileEditorDialog.ShowCreateAsync();
            CrashLog.WriteDiagnostic("RunCreateProfileAsync", $"editor closed result={(result is not null)}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("RunCreateProfileAsync", ex);
            ReportDialogOpenFailure("layout editor", ex);
            SetBusy(false);
            return;
        }
        finally
        {
            _positionController?.PopFlyoutSuppress();
            if (result is null)
            {
                SetBusy(false);
            }
        }

        if (result is null)
        {
            return;
        }

        ClearStatus();

        PythonRunResult saveResult;
        try
        {
            saveResult = await Task.Run(
                () => _python.SaveProfileAsync(result.Name, result.SelectedWindows).GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Save failed: {ex.Message}");
            SetBusy(false);
            return;
        }

        if (saveResult.Success)
        {
            ClearStatus();
            SafeRefreshProfiles();
        }
        else
        {
            var err = string.IsNullOrWhiteSpace(saveResult.StdErr) ? saveResult.StdOut : saveResult.StdErr;
            SetErrorStatus(string.IsNullOrWhiteSpace(err) ? "Save failed." : err.Split('\n')[0]);
        }

        SetBusy(false);
    }

    private async Task ConfirmAndDeleteProfileAsync(ProfileRow row)
    {
        if (_busy)
        {
            return;
        }

        _positionController?.PushFlyoutSuppress();
        bool confirmed;
        try
        {
            confirmed = await ProfileDeleteDialog.ShowConfirmAsync(row);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ConfirmAndDeleteProfileAsync", ex);
            ReportDialogOpenFailure("delete dialog", ex);
            return;
        }
        finally
        {
            _positionController?.PopFlyoutSuppress();
        }

        if (!confirmed)
        {
            return;
        }

        await RunDeleteAsync(row);
    }

    private async Task RunEditAsync(ProfileRow row)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        ProfileEditorResult? request = null;
        _positionController?.PushFlyoutSuppress();
        try
        {
            StartupTrace.Write($"RunEditAsync: opening layout editor for {row.DisplayName}");
            CrashLog.WriteDiagnostic("RunEditAsync", $"before ShowEditAsync profile={row.DisplayName}");
            request = await ProfileEditorDialog.ShowEditAsync(row);
            CrashLog.WriteDiagnostic("RunEditAsync", $"editor closed result={(request is not null)}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("RunEditAsync", ex);
            ReportDialogOpenFailure("layout editor", ex);
            SetBusy(false);
            return;
        }
        finally
        {
            _positionController?.PopFlyoutSuppress();
            if (request is null)
            {
                SetBusy(false);
            }
        }

        if (request is null)
        {
            return;
        }

        ClearStatus();

        PythonRunResult result;
        try
        {
            result = await Task.Run(
                () => _python.EditProfileAsync(row.FilePath, request.Name, request.SelectedWindows)
                    .GetAwaiter()
                    .GetResult());
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Edit failed: {ex.Message}");
            SetBusy(false);
            return;
        }

        if (result.Success)
        {
            ClearStatus();
            SafeRefreshProfiles();
        }
        else
        {
            var err = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            SetErrorStatus(string.IsNullOrWhiteSpace(err) ? "Edit failed." : err.Split('\n')[0]);
        }

        SetBusy(false);
    }

    private Task RunDeleteAsync(ProfileRow row)
    {
        if (_busy)
        {
            return Task.CompletedTask;
        }

        SetBusy(true);
        ClearStatus();

        if (!_profiles.TryDeleteProfile(row.FilePath))
        {
            SetErrorStatus("Could not delete profile.");
            SetBusy(false);
            return Task.CompletedTask;
        }

        ClearStatus();
        SafeRefreshProfiles();
        SetBusy(false);
        return Task.CompletedTask;
    }

    private async Task RunUpdateAsync(ProfileRow row)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        ClearStatus();

        PythonRunResult result;
        try
        {
            result = await Task.Run(
                () => _python.UpdateProfileAsync(row.FilePath).GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Update failed: {ex.Message}");
            SetBusy(false);
            return;
        }

        if (result.Success)
        {
            ClearStatus();
            SafeRefreshProfiles();
        }
        else
        {
            var err = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            SetErrorStatus(string.IsNullOrWhiteSpace(err) ? "Update failed." : err.Split('\n')[0]);
        }

        SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _addChip.IsEnabled = !busy;
        foreach (var item in _profileChipElements)
        {
            if (item is Button b)
            {
                b.IsEnabled = !busy;
            }
        }
    }

    private void SetErrorStatus(string text)
    {
        _statusText.Text = text;
        ToolTipService.SetToolTip(_statusText, text);
        _statusText.Visibility = Visibility.Visible;
        ApplyMinimumWindowSize();
        ApplyCurrentDockLayout();
    }

    private void ReportDialogOpenFailure(string dialogName, Exception ex)
    {
        var message = $"Could not open {dialogName}: {ex.Message}";
        SetErrorStatus(message);
        ShowErrorMessageBox("Snapdesk", message);
    }

    private static void ShowErrorMessageBox(string title, string message) =>
        NativeMessageBox.Show(title, message, NativeMessageBox.MbOk | NativeMessageBox.MbIconError);

    private void ClearStatus()
    {
        _statusText.Text = string.Empty;
        _statusText.Visibility = Visibility.Collapsed;
        ApplyMinimumWindowSize();
        ApplyCurrentDockLayout();
    }

    private static string TryDevTitleSuffix(string baseTitle)
    {
        var baseDir = AppContext.BaseDirectory;
        if (!baseDir.Contains(@"bin\x64", StringComparison.OrdinalIgnoreCase))
        {
            return baseTitle;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return baseTitle;
        }

        try
        {
            var stamp = File.GetLastWriteTime(exePath).ToString("MMdd HHmm");
            return $"{baseTitle} (dev {stamp})";
        }
        catch
        {
            return baseTitle;
        }
    }
}
