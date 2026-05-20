namespace LayoutProfiles.WinUI.Models;

public sealed record PickableWindow(string ExePath, string Title)
{
    public string Key => $"{ExePath.ToLowerInvariant()}|{Title}";

    public static bool KeysMatch(string exeA, string titleA, string exeB, string titleB) =>
        string.Equals(exeA, exeB, StringComparison.OrdinalIgnoreCase)
        && string.Equals(titleA, titleB, StringComparison.Ordinal);
}
