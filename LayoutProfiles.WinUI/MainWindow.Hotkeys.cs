// MainWindow.Hotkeys.cs — Hotkey registration, global hotkey service, hotkey menu items,
// and all hotkey binding/restore/cycle logic extracted from MainWindow.xaml.cs.

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
    private void BuildHotkeysMenuItems(MenuFlyoutSubItem hotkeysSub)
    {
        _hotkeysEnabledItem = new MenuFlyoutItem { Text = "Enable global hotkeys" };
        _hotkeysEnabledItem.Click += (_, _) =>
        {
            var next = !_settings.GetHotkeysEnabled(_settingsCache);
            SetHotkeysEnabled(next);
        };
        hotkeysSub.Items.Add(_hotkeysEnabledItem);
        hotkeysSub.Items.Add(new MenuFlyoutSeparator());

        _hotkeysProfilesSub = new MenuFlyoutSubItem
        {
            Text = "Profiles (chip order)",
        };
        hotkeysSub.Items.Add(_hotkeysProfilesSub);
        hotkeysSub.Items.Add(new MenuFlyoutSeparator());

        foreach (var actionId in new[] { HotkeyActionIds.CycleNextProfile, HotkeyActionIds.ShowWidget })
        {
            var sub = CreateHotkeyBindingSubMenu(actionId);
            _hotkeyBindingSubMenus[actionId] = sub;
            hotkeysSub.Items.Add(sub);
        }
    }

    private MenuFlyoutSubItem CreateHotkeyBindingSubMenu(string actionId)
    {
        var sub = new MenuFlyoutSubItem();
        var capturedActionId = actionId;

        var enableItem = new MenuFlyoutItem { Text = "Enable" };
        enableItem.Click += (_, _) => ToggleHotkeyBindingEnabled(capturedActionId);
        sub.Items.Add(enableItem);

        sub.Items.Add(new MenuFlyoutSeparator());

        var setItem = new MenuFlyoutItem { Text = "Set hotkey…" };
        setItem.Click += async (_, _) => await PromptSetHotkeyAsync(capturedActionId);
        sub.Items.Add(setItem);

        var clearItem = new MenuFlyoutItem { Text = "Clear hotkey" };
        clearItem.Click += (_, _) => ClearHotkeyBinding(capturedActionId);
        sub.Items.Add(clearItem);

        sub.Tag = enableItem;
        return sub;
    }

    private MenuFlyoutSubItem CreateProfileHotkeySubMenu(string actionId, ProfileRow row)
    {
        var sub = CreateHotkeyBindingSubMenu(actionId);
        sub.Text = row.DisplayName;
        return sub;
    }

    private void SyncHotkeyBindingSubMenu(
        MenuFlyoutSubItem sub,
        HotkeyBinding? binding,
        string fallbackLabel,
        bool masterEnabled)
    {
        var chord = binding is not null
            ? SettingsService.FormatHotkeyChord(binding.Modifiers, binding.VirtualKey)
            : "(none)";
        sub.Text = $"{fallbackLabel} — {chord}";

        if (sub.Tag is MenuFlyoutItem enableItem)
        {
            SetMenuItemCheck(enableItem, binding?.Enabled == true);
            enableItem.IsEnabled = masterEnabled;
        }

        foreach (var item in sub.Items.OfType<MenuFlyoutItem>())
        {
            if (ReferenceEquals(item, sub.Tag))
            {
                continue;
            }

            item.IsEnabled = true;
        }
    }

    private void SyncHotkeysMenu()
    {
        if (_hotkeysEnabledItem is null || _hotkeysProfilesSub is null)
        {
            return;
        }

        var masterEnabled = _settings.GetHotkeysEnabled(_settingsCache);
        SetMenuItemCheck(_hotkeysEnabledItem, masterEnabled);

        var bindings = _settings.GetHotkeyBindings(_settingsCache)
            .ToDictionary(b => b.ActionId, StringComparer.OrdinalIgnoreCase);

        _hotkeysProfilesSub.Items.Clear();
        var chips = GetProfileChipButtons();
        if (chips.Count == 0)
        {
            _hotkeysProfilesSub.Items.Add(new MenuFlyoutItem
            {
                Text = "(no profiles)",
                IsEnabled = false,
            });
        }
        else
        {
            for (var i = 0; i < chips.Count && i < HotkeyActionIds.MaxRestoreSlots; i++)
            {
                if (chips[i].Tag is not ProfileRow row)
                {
                    continue;
                }

                var slot = i + 1;
                var actionId = HotkeyActionIds.RestoreSlotActionId(slot);
                if (actionId is null)
                {
                    continue;
                }

                bindings.TryGetValue(actionId, out var binding);
                var capturedActionId = actionId;
                var item = CreateProfileHotkeySubMenu(capturedActionId, row);
                SyncHotkeyBindingSubMenu(item, binding, row.DisplayName, masterEnabled);
                _hotkeysProfilesSub.Items.Add(item);
            }

            if (chips.Count > HotkeyActionIds.MaxRestoreSlots)
            {
                _hotkeysProfilesSub.Items.Add(new MenuFlyoutItem
                {
                    Text = $"(only first {HotkeyActionIds.MaxRestoreSlots} chips have hotkey slots)",
                    IsEnabled = false,
                });
            }
        }

        foreach (var (actionId, sub) in _hotkeyBindingSubMenus)
        {
            if (!bindings.TryGetValue(actionId, out var binding))
            {
                continue;
            }

            var label = SettingsService.GetHotkeyActionLabel(binding.ActionId);
            SyncHotkeyBindingSubMenu(sub, binding, label, masterEnabled);
        }
    }

    private async Task PromptSetHotkeyAsync(string actionId)
    {
        var bindings = _settings.GetHotkeyBindings(_settingsCache);
        var existing = bindings.FirstOrDefault(b =>
            string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        var label = SettingsService.GetHotkeyActionLabel(actionId);
        HotkeyCaptureResult? captured;
        try
        {
            captured = await HotkeyCaptureWindow.ShowAsync(label, existing.Modifiers, existing.VirtualKey);
        }
        catch (Exception ex)
        {
            ReportDialogOpenFailure("hotkey capture", ex);
            return;
        }

        if (captured is null)
        {
            return;
        }

        if (captured.Modifiers == existing.Modifiers && captured.VirtualKey == existing.VirtualKey)
        {
            return;
        }

        if (!TryApplyHotkeyBinding(actionId, captured, enableMasterHotkeys: false, out var error))
        {
            ShowErrorMessageBox("Snapdesk", error);
            return;
        }

        _settingsCache = _settings.Load();
        SyncHotkeysMenu();
        RestartGlobalHotkeys();
        StartupTrace.Write(
            $"Hotkey rebound action={actionId} chord={SettingsService.FormatHotkeyChord(captured.Modifiers, captured.VirtualKey)}");
    }

    private void ClearHotkeyBinding(string actionId)
    {
        _settings.ClearHotkeyBinding(actionId);
        _settingsCache = _settings.Load();
        SyncHotkeysMenu();
        RestartGlobalHotkeys();
        StartupTrace.Write($"Hotkey cleared action={actionId}");
    }

    private bool TryApplyHotkeyBinding(
        string actionId,
        HotkeyCaptureResult captured,
        bool enableMasterHotkeys,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var bindings = _settings.GetHotkeyBindings(_settingsCache);
        var existing = bindings.FirstOrDefault(b =>
            string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            errorMessage = "Hotkey slot is not available.";
            return false;
        }

        var duplicateOwner = SettingsService.FindDuplicateBindingOwner(
            bindings,
            actionId,
            captured.Modifiers,
            captured.VirtualKey);
        if (duplicateOwner is not null)
        {
            var duplicateLabel = SettingsService.GetHotkeyActionLabel(duplicateOwner);
            var chord = SettingsService.FormatHotkeyChord(captured.Modifiers, captured.VirtualKey);
            errorMessage = $"Hotkey {chord} is already assigned to {duplicateLabel}.";
            return false;
        }

        EnsureGlobalHotkeyServiceForProbe();
        if (_globalHotkeys is not null
            && !_globalHotkeys.TryRegisterChord(captured.Modifiers, captured.VirtualKey, out _))
        {
            var chord = SettingsService.FormatHotkeyChord(captured.Modifiers, captured.VirtualKey);
            errorMessage = $"Hotkey {chord} is already in use by another application.";
            return false;
        }

        var enabled = existing.Enabled;
        if (enableMasterHotkeys)
        {
            if (!_settings.GetHotkeysEnabled(_settingsCache))
            {
                SetHotkeysEnabled(true);
            }

            enabled = true;
        }

        _settings.SaveHotkeyBinding(
            actionId,
            enabled,
            captured.Modifiers,
            captured.VirtualKey);
        return true;
    }

    private bool TryApplyProfileHotkeySelection(ProfileRow row, ProfileHotkeySelection? hotkey, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (hotkey is null || hotkey.Intent == ProfileHotkeyIntent.Unchanged)
        {
            return true;
        }

        var slot = GetProfileSlot(row);
        if (slot < 1 || slot > HotkeyActionIds.MaxRestoreSlots)
        {
            errorMessage = hotkey.Intent == ProfileHotkeyIntent.Set
                ? "This profile is outside the first nine chip slots, so it cannot have a restore hotkey."
                : string.Empty;
            return hotkey.Intent != ProfileHotkeyIntent.Set;
        }

        var actionId = HotkeyActionIds.RestoreSlotActionId(slot)!;
        if (hotkey.Intent == ProfileHotkeyIntent.Clear)
        {
            ClearHotkeyBinding(actionId);
            return true;
        }

        var captured = new HotkeyCaptureResult
        {
            Modifiers = hotkey.Modifiers,
            VirtualKey = hotkey.VirtualKey,
        };
        if (!TryApplyHotkeyBinding(actionId, captured, enableMasterHotkeys: true, out errorMessage))
        {
            return false;
        }

        return true;
    }

    private void EnsureGlobalHotkeyServiceForProbe()
    {
        if (_globalHotkeys is not null)
        {
            return;
        }

        try
        {
            var service = new GlobalHotkeyService();
            service.Start(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!,
                Array.Empty<HotkeyBinding>(),
                _ => { });
            _globalHotkeys = service;
        }
        catch (Exception ex)
        {
            CrashLog.Write("EnsureGlobalHotkeyServiceForProbe", ex);
        }
    }

    private void SetHotkeysEnabled(bool enabled)
    {
        _settings.SaveHotkeysEnabled(enabled);
        _settingsCache = _settings.Load();
        SyncHotkeysMenu();
        RestartGlobalHotkeys();
        StartupTrace.Write($"Global hotkeys {(enabled ? "enabled" : "disabled")}");
    }

    private void ToggleHotkeyBindingEnabled(string actionId)
    {
        var bindings = _settings.GetHotkeyBindings(_settingsCache);
        var existing = bindings.FirstOrDefault(b =>
            string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        if (!existing.Enabled && existing.VirtualKey == 0)
        {
            var defaultBinding = SettingsService.CreateDefaultHotkeyBindings().First(b =>
                string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            _settings.SaveHotkeyBinding(
                actionId,
                true,
                defaultBinding.Modifiers,
                defaultBinding.VirtualKey);
        }
        else
        {
            _settings.SaveHotkeyBinding(actionId, !existing.Enabled);
        }

        _settingsCache = _settings.Load();
        SyncHotkeysMenu();
        RestartGlobalHotkeys();
    }

    private void ToggleProfileHotkey(ProfileRow row)
    {
        var slot = GetProfileSlot(row);
        var actionId = slot > 0 ? HotkeyActionIds.RestoreSlotActionId(slot) : null;
        if (actionId is null)
        {
            return;
        }

        if (!_settings.GetHotkeysEnabled(_settingsCache))
        {
            SetHotkeysEnabled(true);
        }

        ToggleHotkeyBindingEnabled(actionId);
    }

    private void SyncProfileChipHotkeyMenuItem(MenuFlyoutItem item, ProfileRow row)
    {
        var slot = GetProfileSlot(row);
        if (slot < 1 || slot > HotkeyActionIds.MaxRestoreSlots)
        {
            item.Visibility = Visibility.Collapsed;
            return;
        }

        item.Visibility = Visibility.Visible;
        var actionId = HotkeyActionIds.RestoreSlotActionId(slot)!;
        var bindings = _settings.GetHotkeyBindings(_settingsCache);
        var binding = bindings.FirstOrDefault(b =>
            string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        var chord = binding is not null
            ? SettingsService.FormatHotkeyChord(binding.Modifiers, binding.VirtualKey)
            : SettingsService.FormatHotkeyChord(HotkeyModifiers.DefaultChord, (uint)(0x30 + slot));
        var enabled = binding?.Enabled == true;
        item.Text = enabled ? $"Disable hotkey ({chord})" : $"Enable hotkey ({chord})";
        item.IsEnabled = _settings.GetHotkeysEnabled(_settingsCache) || !enabled;
    }

    private int GetProfileSlot(ProfileRow row)
    {
        var chips = GetProfileChipButtons();
        for (var i = 0; i < chips.Count; i++)
        {
            if (chips[i].Tag is ProfileRow chipRow
                && string.Equals(chipRow.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return -1;
    }

    private void StartGlobalHotkeys()
    {
        StopGlobalHotkeys();
        if (!_settings.GetHotkeysEnabled(_settingsCache))
        {
            return;
        }

        var bindings = _settings.GetHotkeyBindings(_settingsCache)
            .Where(b => b.Enabled && b.VirtualKey != 0)
            .ToList();
        if (bindings.Count == 0)
        {
            return;
        }

        try
        {
            var service = new GlobalHotkeyService();
            service.Start(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!,
                Array.Empty<HotkeyBinding>(),
                OnGlobalHotkeyPressed);
            var failures = service.RegisterBindingsWithResults(bindings);
            _globalHotkeys = service;
            StartupTrace.Write($"Global hotkeys registered count={bindings.Count - failures.Count}");

            if (failures.Count > 0)
            {
                var messages = failures.Select(f =>
                {
                    var label = SettingsService.GetHotkeyActionLabel(f.ActionId);
                    var chord = SettingsService.FormatHotkeyChord(f.Modifiers, f.VirtualKey);
                    return $"{label} ({chord})";
                });
                SetErrorStatus($"Could not register hotkey(s): {string.Join(", ", messages)}");
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("StartGlobalHotkeys", ex);
            StartupTrace.Write($"StartGlobalHotkeys failed: {ex.Message}");
        }
    }

    private void StopGlobalHotkeys()
    {
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
    }

    private void RestartGlobalHotkeys()
    {
        StopGlobalHotkeys();
        StartGlobalHotkeys();
    }

    private void OnGlobalHotkeyPressed(string actionId)
    {
        if (_busy)
        {
            return;
        }

        var slot = HotkeyActionIds.SlotFromRestoreActionId(actionId);
        if (slot is not null)
        {
            _ = OnHotkeyRestoreSlotAsync(slot.Value);
            return;
        }

        switch (actionId)
        {
            case HotkeyActionIds.CycleNextProfile:
                _ = OnHotkeyCycleNextProfileAsync();
                break;
            case HotkeyActionIds.ShowWidget:
                OnHotkeyShowWidget();
                break;
        }
    }

    private async Task OnHotkeyRestoreSlotAsync(int slot)
    {
        var chips = GetProfileChipButtons();
        var index = slot - 1;
        if (index < 0 || index >= chips.Count)
        {
            return;
        }

        if (chips[index].Tag is not ProfileRow row)
        {
            return;
        }

        await RunRestoreAsync(row);
    }

    private async Task OnHotkeyCycleNextProfileAsync()
    {
        var chips = GetProfileChipButtons();
        if (chips.Count == 0)
        {
            return;
        }

        _cycleProfileIndex = (_cycleProfileIndex + 1) % chips.Count;
        if (chips[_cycleProfileIndex].Tag is not ProfileRow row)
        {
            return;
        }

        await RunRestoreAsync(row);
    }

    private void OnHotkeyShowWidget() => ShowOrRevealWidget();
}
