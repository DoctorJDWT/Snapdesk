namespace LayoutProfiles.WinUI.Services;

/// <summary>Win32 RegisterHotKey modifier flags (MOD_*).</summary>
public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;

    public const uint DefaultChord = Control | Alt;
}
