using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace LayoutProfiles.WinUI.Helpers;

internal static class HotkeyCaptureHelper
{
    public static uint ReadModifierFlags()
    {
        uint mods = 0;
        if (IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftControl) || IsKeyDown(VirtualKey.RightControl))
        {
            mods |= HotkeyModifiers.Control;
        }

        if (IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.LeftShift) || IsKeyDown(VirtualKey.RightShift))
        {
            mods |= HotkeyModifiers.Shift;
        }

        if (IsKeyDown(VirtualKey.Menu) || IsKeyDown(VirtualKey.LeftMenu) || IsKeyDown(VirtualKey.RightMenu))
        {
            mods |= HotkeyModifiers.Alt;
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            mods |= HotkeyModifiers.Win;
        }

        return mods;
    }

    public static bool IsModifierKey(VirtualKey key) =>
        key is VirtualKey.Control
            or VirtualKey.LeftControl
            or VirtualKey.RightControl
            or VirtualKey.Shift
            or VirtualKey.LeftShift
            or VirtualKey.RightShift
            or VirtualKey.Menu
            or VirtualKey.LeftMenu
            or VirtualKey.RightMenu
            or VirtualKey.LeftWindows
            or VirtualKey.RightWindows;

    public static uint VirtualKeyToVk(VirtualKey key) => (uint)key;

    public static HotkeyValidationResult Validate(uint modifiers, uint virtualKey)
    {
        if (virtualKey == 0)
        {
            return HotkeyValidationResult.Error("Press a key combination.");
        }

        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            return HotkeyValidationResult.Error("Windows key combinations are not supported.");
        }

        if (modifiers == 0)
        {
            return HotkeyValidationResult.Warning(
                "Single-key shortcuts may conflict with normal typing. Consider adding Ctrl, Alt, or Shift.");
        }

        return HotkeyValidationResult.Ok();
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return state.HasFlag(CoreVirtualKeyStates.Down);
    }
}

internal sealed class HotkeyValidationResult
{
    public HotkeyValidationSeverity Severity { get; init; }

    public string Message { get; init; } = "";

    public bool CanSave => Severity != HotkeyValidationSeverity.Error;

    public static HotkeyValidationResult Ok() =>
        new() { Severity = HotkeyValidationSeverity.Ok };

    public static HotkeyValidationResult Warning(string message) =>
        new() { Severity = HotkeyValidationSeverity.Warning, Message = message };

    public static HotkeyValidationResult Error(string message) =>
        new() { Severity = HotkeyValidationSeverity.Error, Message = message };
}

internal enum HotkeyValidationSeverity
{
    Ok,
    Warning,
    Error,
}
