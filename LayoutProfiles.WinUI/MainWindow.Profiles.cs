// MainWindow.Profiles.cs — Profile CRUD operations, chip creation, chip reorder handling,
// profile restore/save/update/edit/delete workflows, and add-chip creation
// extracted from MainWindow.xaml.cs.

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

public sealed partial class MainWindow
{
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

        _undockedSizedForChipCount = -1;
        _gumballLayout.Reset();
        ApplyGumballLayout();
        ApplyCurrentDockLayout();
        SyncHotkeysMenu();
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
        var hotkeyItem = new MenuFlyoutItem();
        hotkeyItem.Click += (_, _) => ToggleProfileHotkey(row);
        var setHotkeyItem = new MenuFlyoutItem { Text = "Set hotkey…" };
        setHotkeyItem.Click += async (_, _) =>
        {
            var slot = GetProfileSlot(row);
            var actionId = slot > 0 ? HotkeyActionIds.RestoreSlotActionId(slot) : null;
            if (actionId is not null)
            {
                await PromptSetHotkeyAsync(actionId);
            }
        };
        var chipFlyout = new MenuFlyout { Items = { editItem, hotkeyItem, setHotkeyItem, deleteItem } };
        chipFlyout.Opening += (_, _) =>
        {
            SyncProfileChipHotkeyMenuItem(hotkeyItem, row);
            _positionController?.PushFlyoutSuppress();
        };
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
        catch (Exception ex)
        {
            CrashLog.WriteDiagnostic("MainWindow.Clear", $"ignored: {ex.Message}"); // ignore
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
            result = await Task.Run(async () => await _python.RestoreProfileAsync(row.FilePath));
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
        var predictedSlot = Math.Min(GetProfileChipButtons().Count + 1, HotkeyActionIds.MaxRestoreSlots);
        _positionController?.PushFlyoutSuppress();
        try
        {
            StartupTrace.Write("RunCreateProfileAsync: opening layout editor");
            CrashLog.WriteDiagnostic("RunCreateProfileAsync", "before ProfileEditorDialog.ShowCreateAsync");
            result = await ProfileEditorDialog.ShowCreateAsync(predictedSlot);
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
                async () => await _python.SaveProfileAsync(result.Name, result.SelectedWindows));
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
            var savedRow = FindProfileRowAfterSave(result.Name);
            if (savedRow is not null
                && !TryApplyProfileHotkeySelection(savedRow, result.Hotkey, out var hotkeyError))
            {
                SetErrorStatus(string.IsNullOrWhiteSpace(hotkeyError)
                    ? "Profile saved, but the hotkey could not be assigned."
                    : $"Profile saved, but hotkey assignment failed: {hotkeyError}");
            }
            else if (result.Hotkey?.Intent is ProfileHotkeyIntent.Set or ProfileHotkeyIntent.Clear)
            {
                _settingsCache = _settings.Load();
                SyncHotkeysMenu();
                RestartGlobalHotkeys();
            }
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
        var slot = GetProfileSlot(row);
        HotkeyBinding? currentHotkey = null;
        if (slot is > 0 and <= HotkeyActionIds.MaxRestoreSlots)
        {
            var actionId = HotkeyActionIds.RestoreSlotActionId(slot);
            if (actionId is not null)
            {
                currentHotkey = _settings.GetHotkeyBindings(_settingsCache)
                    .FirstOrDefault(b => string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            }
        }

        _positionController?.PushFlyoutSuppress();
        try
        {
            StartupTrace.Write($"RunEditAsync: opening layout editor for {row.DisplayName}");
            CrashLog.WriteDiagnostic("RunEditAsync", $"before ShowEditAsync profile={row.DisplayName}");
            request = await ProfileEditorDialog.ShowEditAsync(row, currentHotkey);
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
                async () => await _python.EditProfileAsync(row.FilePath, request.Name, request.SelectedWindows));
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
            var updatedRow = FindProfileRowAfterSave(request.Name);
            if (updatedRow is not null
                && !TryApplyProfileHotkeySelection(updatedRow, request.Hotkey, out var hotkeyError))
            {
                SetErrorStatus(string.IsNullOrWhiteSpace(hotkeyError)
                    ? "Profile saved, but the hotkey could not be updated."
                    : $"Profile saved, but hotkey update failed: {hotkeyError}");
            }
            else if (request.Hotkey?.Intent is ProfileHotkeyIntent.Set or ProfileHotkeyIntent.Clear)
            {
                _settingsCache = _settings.Load();
                SyncHotkeysMenu();
                RestartGlobalHotkeys();
            }
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
                async () => await _python.UpdateProfileAsync(row.FilePath));
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

    private ProfileRow? FindProfileRowAfterSave(string profileName)
    {
        var expectedPath = ProfileService.ProfilePathForName(profileName);
        var rows = ProfileService.ApplyDisplayOrder(
            _profiles.ListProfiles(),
            _settings.GetProfileOrder(_settingsCache));
        return rows.FirstOrDefault(r =>
                   string.Equals(r.FilePath, expectedPath, StringComparison.OrdinalIgnoreCase))
               ?? rows.FirstOrDefault(r =>
                   string.Equals(r.DisplayName, profileName, StringComparison.OrdinalIgnoreCase));
    }
}
