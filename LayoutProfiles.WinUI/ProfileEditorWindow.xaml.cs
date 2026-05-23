using System.ComponentModel;
using System.Runtime.CompilerServices;
using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace LayoutProfiles.WinUI;

public sealed partial class ProfileEditorWindow : Window
{
    private const int WindowWidth = 760;
    private const int MinWindowHeight = 520;
    /// <summary>Non-list chrome: heading, name, hotkey, labels, buttons, padding.</summary>
    private const int FixedEditorHeight = 328;
    private const int ApplicationRowHeight = 44;
    private const int MinListViewportHeight = 280;
    /// <summary>Share of monitor work-area height used for the editor at most.</summary>
    private const double MaxWorkAreaHeightFraction = 0.92;
    private const int WorkAreaInset = 24;

    private Grid _rootGrid = null!;
    private TextBlock _headingText = null!;
    private TextBlock _nameLabelText = null!;
    private TextBox _nameBox = null!;
    private TextBlock _hotkeyLabelText = null!;
    private TextBlock _hotkeyChordText = null!;
    private Button _setHotkeyButton = null!;
    private Button _clearHotkeyButton = null!;
    private TextBlock _applicationsLabelText = null!;
    private TextBlock _windowsHintText = null!;
    private ScrollViewer _windowListScroll = null!;
    private StackPanel _windowListPanel = null!;
    private TextBlock _emptyWindowsText = null!;
    private TextBlock _errorText = null!;
    private Button _cancelButton = null!;
    private Button _refreshButton = null!;
    private Button _saveButton = null!;

    private readonly TaskCompletionSource<ProfileEditorResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ProfileEditorMode _mode;
    private readonly string? _profilePath;
    private readonly int _predictedRestoreSlot;
    private readonly HotkeyBinding? _initialHotkey;
    private readonly ProfileService _profiles = new();
    private readonly PythonBridge _python = new();
    private readonly List<WindowPickerItem> _pickerItems = new();
    private HashSet<string> _savedKeys = new(StringComparer.OrdinalIgnoreCase);
    private uint _hotkeyModifiers;
    private uint _hotkeyVirtualKey;
    private ProfileHotkeyIntent _hotkeyIntent = ProfileHotkeyIntent.Unchanged;
    private bool _completed;
    private bool _listInitialized;

    private ProfileEditorWindow(
        ProfileEditorMode mode,
        string initialName,
        string? profilePath,
        int predictedRestoreSlot,
        HotkeyBinding? initialHotkey)
    {
        CrashLog.WriteDiagnostic("ProfileEditorWindow.ctor", $"begin mode={mode}");
        try
        {
            // UI is built in code (no InitializeComponent) — published secondary windows fail XAML parse.
            BuildUi();
            CrashLog.WriteDiagnostic("ProfileEditorWindow.ctor", "BuildUi completed");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ProfileEditorWindow.BuildUi", ex);
            throw;
        }

        Title = "Snapdesk";
        _mode = mode;
        _profilePath = profilePath;
        _predictedRestoreSlot = predictedRestoreSlot;
        _initialHotkey = initialHotkey;
        if (initialHotkey is not null)
        {
            _hotkeyModifiers = initialHotkey.Modifiers;
            _hotkeyVirtualKey = initialHotkey.VirtualKey;
        }

        _headingText.Text = mode == ProfileEditorMode.Create ? "New layout" : "Edit layout";
        _nameBox.PlaceholderText = mode == ProfileEditorMode.Create ? "e.g. League" : "Profile name";
        _nameBox.Text = initialName;
        _windowsHintText.Text = mode == ProfileEditorMode.Create
            ? "Select windows to include in this layout."
            : "Add or remove windows. Checked items are saved in the profile.";

        _nameBox.TextChanged += (_, _) => _errorText.Visibility = Visibility.Collapsed;
        UpdateHotkeyDisplay();
        _rootGrid.Loaded += OnRootGridLoaded;
        Closed += OnClosed;
        _rootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        SecondaryWindowTracker.Register(this);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        ApplyThemeFromApp();

        ConfigureWindowChrome(MinWindowHeight);
        CrashLog.WriteDiagnostic("ProfileEditorWindow.ctor", "end");
    }

    public static Task<ProfileEditorResult?> ShowCreateAsync(int predictedRestoreSlot = 0)
    {
        CrashLog.WriteDiagnostic("ProfileEditorWindow.ShowCreateAsync", "creating window");
        var window = new ProfileEditorWindow(
            ProfileEditorMode.Create,
            string.Empty,
            profilePath: null,
            predictedRestoreSlot,
            initialHotkey: null);
        WindowChromeHelper.PresentModal(window, App.MainWindowInstance);
        return window._tcs.Task;
    }

    public static Task<ProfileEditorResult?> ShowEditAsync(ProfileRow row, HotkeyBinding? currentHotkey = null)
    {
        CrashLog.WriteDiagnostic("ProfileEditorWindow.ShowEditAsync", $"profile={row.DisplayName}");
        var window = new ProfileEditorWindow(
            ProfileEditorMode.Edit,
            row.DisplayName,
            row.FilePath,
            predictedRestoreSlot: 0,
            currentHotkey);
        WindowChromeHelper.PresentModal(window, App.MainWindowInstance);
        return window._tcs.Task;
    }

    private void BuildUi()
    {
        _rootGrid = new Grid { Padding = new Thickness(16) };
        for (var i = 0; i < 8; i++)
        {
            var row = new RowDefinition
            {
                Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            };
            if (i == 4)
            {
                row.MinHeight = 0;
            }

            _rootGrid.RowDefinitions.Add(row);
        }

        _headingText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 14),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        Grid.SetRow(_headingText, 0);

        _nameLabelText = new TextBlock
        {
            Text = "Profile name",
            FontSize = 13,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _nameBox = new TextBox { MaxLength = 120 };
        var namePanel = new StackPanel();
        namePanel.Children.Add(_nameLabelText);
        namePanel.Children.Add(_nameBox);
        Grid.SetRow(namePanel, 1);

        _hotkeyLabelText = new TextBlock
        {
            Text = "Keyboard shortcut",
            FontSize = 13,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _hotkeyChordText = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _setHotkeyButton = new Button { Content = "Set hotkey…" };
        _setHotkeyButton.Click += OnSetHotkeyClick;
        _clearHotkeyButton = new Button { Content = "Clear" };
        _clearHotkeyButton.Click += OnClearHotkeyClick;

        var hotkeyActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hotkeyActions.Children.Add(_setHotkeyButton);
        hotkeyActions.Children.Add(_clearHotkeyButton);

        var hotkeyRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_hotkeyChordText, 0);
        Grid.SetColumn(hotkeyActions, 1);
        hotkeyRow.Children.Add(_hotkeyChordText);
        hotkeyRow.Children.Add(hotkeyActions);

        var hotkeyPanel = new StackPanel();
        hotkeyPanel.Children.Add(_hotkeyLabelText);
        hotkeyPanel.Children.Add(hotkeyRow);
        Grid.SetRow(hotkeyPanel, 2);

        _applicationsLabelText = new TextBlock
        {
            Text = "Applications",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.85,
        };
        _windowsHintText = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = "Select windows to include in this layout.",
            Margin = new Thickness(0, 4, 0, 0),
        };
        var appsPanel = new StackPanel { Margin = new Thickness(0, 18, 0, 8) };
        appsPanel.Children.Add(_applicationsLabelText);
        appsPanel.Children.Add(_windowsHintText);
        Grid.SetRow(appsPanel, 3);

        _windowListPanel = new StackPanel();
        _windowListScroll = new ScrollViewer
        {
            MinHeight = MinListViewportHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _windowListPanel,
        };
        Grid.SetRow(_windowListScroll, 4);

        _emptyWindowsText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            Text = "No pickable windows found. Open the apps you want, then click Refresh.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_emptyWindowsText, 5);

        _errorText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x6C, 0x75)),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_errorText, 6);

        _cancelButton = new Button { Content = "Cancel" };
        _cancelButton.Click += OnCancelClick;
        _refreshButton = new Button { Content = "Refresh" };
        _refreshButton.Click += OnRefreshClick;
        _saveButton = new Button { Content = "Save" };
        _saveButton.Click += OnSaveClick;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 8,
        };
        buttonPanel.Children.Add(_cancelButton);
        buttonPanel.Children.Add(_refreshButton);
        buttonPanel.Children.Add(_saveButton);
        Grid.SetRow(buttonPanel, 7);

        _rootGrid.Children.Add(_headingText);
        _rootGrid.Children.Add(namePanel);
        _rootGrid.Children.Add(hotkeyPanel);
        _rootGrid.Children.Add(appsPanel);
        _rootGrid.Children.Add(_windowListScroll);
        _rootGrid.Children.Add(_emptyWindowsText);
        _rootGrid.Children.Add(_errorText);
        _rootGrid.Children.Add(buttonPanel);

        Content = _rootGrid;
    }

    private void RebuildWindowListUi()
    {
        _windowListPanel.Children.Clear();
        foreach (var item in _pickerItems)
        {
            var text = new TextBlock
            {
                Text = item.DisplayText,
                TextWrapping = TextWrapping.WrapWholeWords,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
            };
            var checkBox = new CheckBox
            {
                MinHeight = 36,
                Padding = new Thickness(2, 4, 2, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = text,
                IsChecked = item.IsChecked,
                Tag = item,
            };
            checkBox.Checked += (_, _) => item.IsChecked = true;
            checkBox.Unchecked += (_, _) => item.IsChecked = false;
            _windowListPanel.Children.Add(checkBox);
        }
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
        _nameLabelText.Foreground = palette.Muted;
        _hotkeyLabelText.Foreground = palette.Muted;
        _hotkeyChordText.Foreground = palette.Primary;
        _applicationsLabelText.Foreground = palette.Primary;
        _windowsHintText.Foreground = palette.Muted;
        _emptyWindowsText.Foreground = palette.Muted;
        _saveButton.Background = palette.Accent;
        _saveButton.Foreground = palette.Primary;

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

    private void ConfigureWindowChrome(int height) =>
        SecondaryWindowPlacement.Apply(this, SecondaryWindowPlacement.ProfileEditor, WindowWidth, height, resizable: true);

    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        if (_listInitialized)
        {
            return;
        }

        _listInitialized = true;
        await InitializeWindowListAsync();
    }

    private async Task InitializeWindowListAsync()
    {
        try
        {
            if (_mode == ProfileEditorMode.Edit && !string.IsNullOrEmpty(_profilePath))
            {
                _savedKeys = _profiles.ReadProfileWindows(_profilePath)
                    .Select(w => w.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            await LoadWindowListAsync();
        }
        catch (Exception ex)
        {
            _errorText.Text = $"Could not list windows: {ex.Message}";
            _errorText.Visibility = Visibility.Visible;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadWindowListAsync();

    private async Task LoadWindowListAsync()
    {
        _refreshButton.IsEnabled = false;
        _errorText.Visibility = Visibility.Collapsed;

        try
        {
            var live = await Task.Run(() => _python.ListPickableWindowsAsync().GetAwaiter().GetResult());
            var merged = new Dictionary<string, PickableWindow>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in live)
            {
                merged[w.Key] = w;
            }

            if (_mode == ProfileEditorMode.Edit)
            {
                foreach (var saved in _profiles.ReadProfileWindows(_profilePath ?? string.Empty))
                {
                    merged.TryAdd(saved.Key, saved);
                }
            }

            var checkedKeys = _pickerItems.Count > 0
                ? _pickerItems.Where(i => i.IsChecked)
                    .Select(i => i.Window.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : _savedKeys;

            _pickerItems.Clear();
            foreach (var w in merged.Values.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
            {
                _pickerItems.Add(new WindowPickerItem(w, checkedKeys.Contains(w.Key)));
            }

            RebuildWindowListUi();
            _emptyWindowsText.Visibility = _pickerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResizeToFitApplicationList();
            _windowListScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception ex)
        {
            _errorText.Text = $"Could not list windows: {ex.Message}";
            _errorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _refreshButton.IsEnabled = true;
        }
    }

    private void ResizeToFitApplicationList()
    {
        var itemCount = _pickerItems.Count;
        var contentListHeight = Math.Max(MinListViewportHeight, itemCount * ApplicationRowHeight);

        try
        {
            var appWindow = GetAppWindow();
            var pos = appWindow.Position;
            var size = appWindow.Size;
            var center = new PointInt32(
                pos.X + Math.Max(1, size.Width) / 2,
                pos.Y + Math.Max(1, size.Height) / 2);
            var display = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
            var workHeight = display.WorkArea.Height;
            var maxWindowHeight = Math.Max(
                MinWindowHeight,
                Math.Min(workHeight - WorkAreaInset, (int)(workHeight * MaxWorkAreaHeightFraction)));
            var maxListHeight = Math.Max(MinListViewportHeight, maxWindowHeight - FixedEditorHeight);
            var listViewportHeight = itemCount == 0
                ? MinListViewportHeight
                : maxListHeight;
            var height = Math.Max(MinWindowHeight, FixedEditorHeight + listViewportHeight);

            _windowListScroll.VerticalScrollBarVisibility = itemCount > 0 && contentListHeight > maxListHeight
                ? ScrollBarVisibility.Visible
                : ScrollBarVisibility.Auto;

            var (width, clampedHeight, x, y) = WindowGeometryHelper.ClampToVirtualScreen(
                WindowWidth,
                height,
                pos.X,
                pos.Y,
                minWidth: WindowWidth,
                minHeight: MinWindowHeight);

            appWindow.Resize(new SizeInt32(width, clampedHeight));
            appWindow.Move(new PointInt32(x, y));
        }
        catch
        {
            ConfigureWindowChrome(Math.Max(MinWindowHeight, FixedEditorHeight + contentListHeight));
        }
    }

    private string GetHotkeyActionLabel()
    {
        var profileName = string.IsNullOrWhiteSpace(_nameBox.Text)
            ? "this profile"
            : _nameBox.Text.Trim();
        if (_mode == ProfileEditorMode.Create
            && _predictedRestoreSlot is > 0 and <= HotkeyActionIds.MaxRestoreSlots)
        {
            return $"Restore {profileName} (slot {_predictedRestoreSlot})";
        }

        return $"Restore {profileName}";
    }

    private void UpdateHotkeyDisplay()
    {
        _hotkeyChordText.Text = SettingsService.FormatHotkeyChord(_hotkeyModifiers, _hotkeyVirtualKey);
        _clearHotkeyButton.IsEnabled = _hotkeyVirtualKey != 0 || _hotkeyIntent == ProfileHotkeyIntent.Set;
    }

    private async void OnSetHotkeyClick(object sender, RoutedEventArgs e)
    {
        HotkeyCaptureResult? captured;
        try
        {
            captured = await HotkeyCaptureWindow.ShowAsync(
                GetHotkeyActionLabel(),
                _hotkeyModifiers,
                _hotkeyVirtualKey);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ProfileEditorWindow.OnSetHotkeyClick", ex);
            _errorText.Text = "Could not open hotkey capture.";
            _errorText.Visibility = Visibility.Visible;
            return;
        }

        if (captured is null)
        {
            return;
        }

        _hotkeyModifiers = captured.Modifiers;
        _hotkeyVirtualKey = captured.VirtualKey;
        _hotkeyIntent = ProfileHotkeyIntent.Set;
        UpdateHotkeyDisplay();
    }

    private void OnClearHotkeyClick(object sender, RoutedEventArgs e)
    {
        _hotkeyModifiers = _initialHotkey?.Modifiers ?? HotkeyModifiers.DefaultChord;
        _hotkeyVirtualKey = 0;
        _hotkeyIntent = ProfileHotkeyIntent.Clear;
        UpdateHotkeyDisplay();
    }

    private ProfileHotkeySelection? BuildHotkeySelection()
    {
        return _hotkeyIntent switch
        {
            ProfileHotkeyIntent.Unchanged => null,
            ProfileHotkeyIntent.Clear => new ProfileHotkeySelection(ProfileHotkeyIntent.Clear),
            ProfileHotkeyIntent.Set => new ProfileHotkeySelection(
                ProfileHotkeyIntent.Set,
                _hotkeyModifiers,
                _hotkeyVirtualKey),
            _ => null,
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _errorText.Text = "Enter a profile name.";
            _errorText.Visibility = Visibility.Visible;
            return;
        }

        var selected = _pickerItems.Where(i => i.IsChecked).Select(i => i.Window).ToList();
        if (selected.Count == 0)
        {
            _errorText.Text = "Select at least one window.";
            _errorText.Visibility = Visibility.Visible;
            return;
        }

        Complete(new ProfileEditorResult(_nameBox.Text.Trim(), selected, BuildHotkeySelection()));
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(null);

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(null);
            return;
        }

        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        if (_saveButton.IsEnabled)
        {
            e.Handled = true;
            OnSaveClick(_saveButton, new RoutedEventArgs());
        }
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        AppTheme.ResolvedThemeChanged -= OnResolvedThemeChanged;
        SecondaryWindowPlacement.TryPersist(this, SecondaryWindowPlacement.ProfileEditor);
        Complete(null);
    }

    private void Complete(ProfileEditorResult? result)
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
        catch
        {
            // ignore double-close
        }
    }

    private sealed class WindowPickerItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public WindowPickerItem(PickableWindow window, bool isChecked)
        {
            Window = window;
            _isChecked = isChecked;
            DisplayText = string.IsNullOrWhiteSpace(window.BrowserUrl)
                ? $"{window.Title} - {System.IO.Path.GetFileName(window.ExePath)}"
                : $"{window.Title} - {window.BrowserUrl}";
        }

        public PickableWindow Window { get; }
        public string DisplayText { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
