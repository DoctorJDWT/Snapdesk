namespace LayoutProfiles.WinUI.Services;

public static class HotkeyActionIds
{
    public const int MaxRestoreSlots = 9;

    public const string RestoreSlot1 = "restore_slot_1";
    public const string RestoreSlot2 = "restore_slot_2";
    public const string RestoreSlot3 = "restore_slot_3";
    public const string RestoreSlot4 = "restore_slot_4";
    public const string RestoreSlot5 = "restore_slot_5";
    public const string RestoreSlot6 = "restore_slot_6";
    public const string RestoreSlot7 = "restore_slot_7";
    public const string RestoreSlot8 = "restore_slot_8";
    public const string RestoreSlot9 = "restore_slot_9";
    public const string CycleNextProfile = "cycle_next_profile";
    public const string ShowWidget = "show_widget";

    private static readonly string[] RestoreSlotActionIds =
    [
        RestoreSlot1,
        RestoreSlot2,
        RestoreSlot3,
        RestoreSlot4,
        RestoreSlot5,
        RestoreSlot6,
        RestoreSlot7,
        RestoreSlot8,
        RestoreSlot9,
    ];

    public static readonly IReadOnlyList<string> All =
    [
        ..RestoreSlotActionIds,
        CycleNextProfile,
        ShowWidget,
    ];

    public static string? RestoreSlotActionId(int slot)
    {
        if (slot < 1 || slot > MaxRestoreSlots)
        {
            return null;
        }

        return RestoreSlotActionIds[slot - 1];
    }

    public static int? SlotFromRestoreActionId(string actionId)
    {
        for (var i = 0; i < RestoreSlotActionIds.Length; i++)
        {
            if (string.Equals(RestoreSlotActionIds[i], actionId, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return null;
    }
}
