using LayoutProfiles.WinUI.Models;

namespace LayoutProfiles.WinUI.Helpers;

public enum ProfileEditorMode
{
    Create,
    Edit,
}

public enum ProfileHotkeyIntent
{
    Unchanged,
    Set,
    Clear,
}

public sealed record ProfileHotkeySelection(
    ProfileHotkeyIntent Intent,
    uint Modifiers = 0,
    uint VirtualKey = 0);

public sealed record ProfileEditorResult(
    string Name,
    IReadOnlyList<PickableWindow> SelectedWindows,
    ProfileHotkeySelection? Hotkey = null);

/// <summary>Legacy entry points — opens <see cref="ProfileEditorWindow"/> instead of ContentDialog.</summary>
internal static class ProfileEditorDialog
{
    public static Task<ProfileEditorResult?> ShowCreateAsync(int predictedRestoreSlot = 0) =>
        ProfileEditorWindow.ShowCreateAsync(predictedRestoreSlot);

    public static Task<ProfileEditorResult?> ShowEditAsync(ProfileRow row, HotkeyBinding? currentHotkey = null) =>
        ProfileEditorWindow.ShowEditAsync(row, currentHotkey);
}
