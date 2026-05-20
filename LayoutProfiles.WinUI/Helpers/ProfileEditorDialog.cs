using LayoutProfiles.WinUI.Models;

namespace LayoutProfiles.WinUI.Helpers;

public enum ProfileEditorMode
{
    Create,
    Edit,
}

public sealed record ProfileEditorResult(
    string Name,
    IReadOnlyList<PickableWindow> SelectedWindows);

/// <summary>Legacy entry points — opens <see cref="ProfileEditorWindow"/> instead of ContentDialog.</summary>
internal static class ProfileEditorDialog
{
    public static Task<ProfileEditorResult?> ShowCreateAsync() =>
        ProfileEditorWindow.ShowCreateAsync();

    public static Task<ProfileEditorResult?> ShowEditAsync(ProfileRow row) =>
        ProfileEditorWindow.ShowEditAsync(row);
}
