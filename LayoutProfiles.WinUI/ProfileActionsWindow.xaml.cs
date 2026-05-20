using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace LayoutProfiles.WinUI;

public enum ProfileActionsResult
{
    None,
    Edit,
    Update,
    Delete,
}

public sealed partial class ProfileActionsWindow : Window
{
    private const int WindowWidth = 380;
    private const int WindowHeight = 280;
    /// <summary>Block click-through from the gumball right-button release (esp. "Edit layout").</summary>
    private const int InputArmDelayMs = 200;

    private readonly ProfileRow _row;
    private readonly TaskCompletionSource<ProfileActionsResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;
    private bool _inputArmed;
    private DispatcherTimer? _inputArmTimer;

    private ProfileActionsWindow(ProfileRow row)
    {
        InitializeComponent();
        _row = row;
        Title = "Snapdesk — Profile";
        ArmDeferredInput();

        var baseColor = ProfileColors.BaseColorForName(row.DisplayName);
        var fg = ProfileColors.ForegroundFor(baseColor);
        ProfileBadge.Background = new SolidColorBrush(baseColor);
        ProfileInitial.Text = ProfileColors.Initial(row.DisplayName);
        ProfileInitial.Foreground = new SolidColorBrush(fg);
        ProfileNameText.Text = row.DisplayName;
        ProfileMetaText.Text = $"{row.WindowCount} windows";

        Closed += OnClosed;
        SecondaryWindowTracker.Register(this);
        ConfigureWindowChrome();
    }

    public static Task<ProfileActionsResult> ShowAsync(ProfileRow row)
    {
        var window = new ProfileActionsWindow(row);
        window.Activate();
        return window._tcs.Task;
    }

    private void ConfigureWindowChrome() =>
        SecondaryWindowPlacement.Apply(this, SecondaryWindowPlacement.ProfileActions, WindowWidth, WindowHeight);

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!_inputArmed)
        {
            return;
        }

        Complete(ProfileActionsResult.Edit);
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (!_inputArmed)
        {
            return;
        }

        Complete(ProfileActionsResult.Update);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!_inputArmed)
        {
            return;
        }

        if (!await ProfileDeleteDialog.ShowConfirmAsync(_row))
        {
            return;
        }

        Complete(ProfileActionsResult.Delete);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!_inputArmed)
        {
            return;
        }

        Complete(ProfileActionsResult.None);
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        SecondaryWindowPlacement.TryPersist(this, SecondaryWindowPlacement.ProfileActions);
        Complete(ProfileActionsResult.None);
    }

    /// <summary>
    /// Ignore the pointer-up from the gumball right-click that opened this window (click-through on "Edit layout").
    /// </summary>
    private void ArmDeferredInput()
    {
        var root = (UIElement)Content;
        root.IsHitTestVisible = false;

        _inputArmTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(InputArmDelayMs),
        };
        _inputArmTimer.Tick += OnInputArmTimerTick;
        _inputArmTimer.Start();
    }

    private void OnInputArmTimerTick(object? sender, object e)
    {
        if (_inputArmTimer is not null)
        {
            _inputArmTimer.Tick -= OnInputArmTimerTick;
            _inputArmTimer.Stop();
            _inputArmTimer = null;
        }

        _inputArmed = true;
        ((UIElement)Content).IsHitTestVisible = true;
    }

    private void Complete(ProfileActionsResult result)
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
}
