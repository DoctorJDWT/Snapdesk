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
using Windows.System;
using WinRT.Interop;

namespace LayoutProfiles.WinUI;

public sealed partial class ProfileEditorWindow : Window
{
    private const int WindowWidth = 480;
    private const int WindowHeightName = 380;
    private const int WindowHeightWindows = 520;

    private readonly TaskCompletionSource<ProfileEditorResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ProfileEditorMode _mode;
    private readonly string? _profilePath;
    private readonly ProfileService _profiles = new();
    private readonly PythonBridge _python = new();
    private readonly List<WindowPickerItem> _pickerItems = new();
    private HashSet<string> _savedKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _completed;
    private bool _onWindowsPage;

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
        Closed += OnClosed;
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        SecondaryWindowTracker.Register(this);
        AppTheme.ResolvedThemeChanged += OnResolvedThemeChanged;
        ApplyThemeFromApp();

        ConfigureWindowChrome(WindowHeightName);
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

    private void ShowNamePage()
    {
        _onWindowsPage = false;
        NamePageBody.Visibility = Visibility.Visible;
        WindowsPageBody.Visibility = Visibility.Collapsed;
        NamePanel.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        RefreshButton.Visibility = Visibility.Collapsed;
        NextButton.Visibility = Visibility.Visible;
        SaveButton.Visibility = Visibility.Collapsed;
        HeadingText.Text = _mode == ProfileEditorMode.Create ? "New layout" : "Edit layout";
        ConfigureWindowChrome(WindowHeightName);
    }

    private void ShowWindowsPage()
    {
        _onWindowsPage = true;
        NamePageBody.Visibility = Visibility.Collapsed;
        WindowsPageBody.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Visible;
        NextButton.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Visible;
        HeadingText.Text = "Select windows";
        ConfigureWindowChrome(WindowHeightWindows);
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_onWindowsPage)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Enter a profile name.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        NextButton.IsEnabled = false;

        try
        {
            if (_mode == ProfileEditorMode.Edit && !string.IsNullOrEmpty(_profilePath))
            {
                _savedKeys = _profiles.ReadProfileWindows(_profilePath)
                    .Select(w => w.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            await LoadWindowListAsync();
            ShowWindowsPage();
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Could not list windows: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            NextButton.IsEnabled = true;
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

            _pickerItems.Clear();
            foreach (var w in merged.Values.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
            {
                var isChecked = _mode == ProfileEditorMode.Create
                    ? false
                    : _savedKeys.Contains(w.Key);
                _pickerItems.Add(new WindowPickerItem(w, isChecked));
            }

            WindowList.ItemsSource = null;
            WindowList.ItemsSource = _pickerItems;
            EmptyWindowsText.Visibility = _pickerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ShowNamePage();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!_onWindowsPage)
        {
            OnNextClick(sender, e);
            return;
        }

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

        if (_onWindowsPage)
        {
            if (SaveButton.Visibility == Visibility.Visible && SaveButton.IsEnabled)
            {
                e.Handled = true;
                OnSaveClick(SaveButton, new RoutedEventArgs());
            }

            return;
        }

        if (NextButton.Visibility == Visibility.Visible && NextButton.IsEnabled)
        {
            e.Handled = true;
            OnNextClick(NextButton, new RoutedEventArgs());
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
            DisplayText = $"{window.Title} — {System.IO.Path.GetFileName(window.ExePath)}";
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
