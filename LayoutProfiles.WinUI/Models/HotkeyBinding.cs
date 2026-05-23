namespace LayoutProfiles.WinUI.Models;

public sealed class HotkeyBinding
{
    public string ActionId { get; init; } = "";

    public bool Enabled { get; init; }

    public uint Modifiers { get; init; }

    public uint VirtualKey { get; init; }
}
