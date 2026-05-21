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
    private const int WindowWidth = 640;
    private const int InitialWindowHeight = 520;
    private const int MinWindowHeight = 420;
    private const int FixedEditorHeight = 280;
    private const int ApplicationRowHeight = 44;
    private const int WorkAreaInset = 80;

    private readonly TaskCompletionSource<ProfileEditorResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ProfileEditorMode _mode;
    private readonly string? _profilePath;
    private readonly ProfileService _profiles = new();
    private readonly PythonBridge _python = new();
    private readonly List<WindowPickerItem> _pickerItems = new();
    private HashSet<string> _savedKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _completed;
    private bool _listInitialized;

    private ProfileEditorWindow(ProfileEditorMode mode, string initialName, string? profilePath)
    {
        InitializeComponent();
        Title = "Snapdesk";
        _mode = mode;
        _profilePath = profilePath;

        HeadingText.Text = mode == ProfileEditorMode.Create ? "New layout" : "Edit layout";
        NameBox.PlaceholderText = mode == ProfileEditorMode.Create ? "e.g. League" : "Profile name";
        NameBox.Text = initialName;
        WindowsHintText.Text = mode == ProfileEditorMode.Create
            ? "Select windows to include in this layout."
            : "Add or remove windows. Checked items are saved in the profile.";

        NameBox.TextChanged += (_, _) => ErrorText.Visibility = Visibility.Collapsed;
        RootGrid.Loaded += OnRootGridLoaded;
        Closed += OnClosed;
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        SecondaryWindowTracker.Register(this);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        ApplyThemeFromApp();

        ConfigureWindowChrome(InitialWindowHeight);
    }

    public static Task<ProfileEditorResult?> ShowCreateAsync()
    {
        var window = new ProfileEditorWindow(ProfileEditorMode.Create, string.Empty, profilePath: null);
        window.Activate();
        return window._tcs.Task;
    }

    public static Task<ProfileEditorResult?> ShowEditAsync(ProfileRow row)
    {
        var window = new ProfileEditorWindow(ProfileEditorMode.Edit, row.DisplayName, row.FilePath);
        window.Activate();
        return window._tcs.Task;
    }

    private void OnResolvedThemeChanged() =>
        DispatcherQueue.TryEnqueue(ApplyThemeFromApp);

    private void ApplyThemeFromApp()
    {
        var palette = AppTheme.Palette;
        var elementTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;

        RootGrid.Background = palette.Background;
        RootGrid.RequestedTheme = elementTheme;

        HeadingText.Foreground = palette.Primary;
        NameLabelText.Foreground = palette.Muted;
        ApplicationsLabelText.Foreground = palette.Primary;
        WindowsHintText.Foreground = palette.Muted;
        EmptyWindowsText.Foreground = palette.Muted;

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
        SecondaryWindowPlacement.Apply(this, SecondaryWindowPlacement.ProfileEditor, WindowWidth, height);

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
            ErrorText.Text = $"Could not list windows: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadWindowListAsync();

    private async Task LoadWindowListAsync()
    {
        RefreshButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

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

            WindowList.ItemsSource = null;
            WindowList.ItemsSource = _pickerItems;
            EmptyWindowsText.Visibility = _pickerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResizeToFitApplicationList();
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Could not list windows: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ResizeToFitApplicationList()
    {
        var itemCount = Math.Max(1, _pickerItems.Count);
        var desiredHeight = Math.Max(MinWindowHeight, FixedEditorHeight + itemCount * ApplicationRowHeight);

        try
        {
            var appWindow = GetAppWindow();
            var pos = appWindow.Position;
            var size = appWindow.Size;
            var center = new PointInt32(
                pos.X + Math.Max(1, size.Width) / 2,
                pos.Y + Math.Max(1, size.Height) / 2);
            var display = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
            var maxHeight = Math.Max(MinWindowHeight, display.WorkArea.Height - WorkAreaInset);
            var height = Math.Min(desiredHeight, maxHeight);
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
            ConfigureWindowChrome(desiredHeight);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Enter a profile name.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var selected = _pickerItems.Where(i => i.IsChecked).Select(i => i.Window).ToList();
        if (selected.Count == 0)
        {
            ErrorText.Text = "Select at least one window.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Complete(new ProfileEditorResult(NameBox.Text.Trim(), selected));
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

        if (SaveButton.IsEnabled)
        {
            e.Handled = true;
            OnSaveClick(SaveButton, new RoutedEventArgs());
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
