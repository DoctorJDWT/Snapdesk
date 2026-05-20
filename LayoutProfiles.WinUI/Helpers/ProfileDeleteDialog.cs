using LayoutProfiles.WinUI.Models;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>Opens <see cref="ProfileDeleteWindow"/> for profile deletion confirmation.</summary>
internal static class ProfileDeleteDialog
{
    public static Task<bool> ShowConfirmAsync(ProfileRow row) =>
        ProfileDeleteWindow.ShowConfirmAsync(row);
}
