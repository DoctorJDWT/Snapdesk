using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;

namespace LayoutProfiles.WinUI.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly Regex GeometryRe = new(
        @"^(\d+)x(\d+)([+-]\d+)([+-]\d+)$",
        RegexOptions.Compiled);

    private readonly object _ioLock = new();

    public Dictionary<string, JsonElement> Load()
    {
        var path = RepoPaths.SettingsPath;
        if (!File.Exists(path))
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            string json;
            lock (_ioLock)
            {
                json = File.ReadAllText(path);
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            }

            return doc.RootElement
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void MergeAndSave(IReadOnlyDictionary<string, object?> updates)
    {
        var path = RepoPaths.SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        lock (_ioLock)
        {
            var root = LoadMutableRoot(path);

            foreach (var (key, value) in updates)
            {
                root[key] = CreateSettingsNode(value);
            }

            File.WriteAllText(path, root.ToJsonString(WriteJsonOptions));
        }
    }

    public bool GetRunOnStartup(Dictionary<string, JsonElement> settings) =>
        settings.TryGetValue("gui_run_on_startup", out var el)
        && el.ValueKind == JsonValueKind.True;

    public const string LastInstalledVersionKey = "last_installed_version";
    public const string LastInstalledVersionUtcKey = "last_installed_version_utc";
    public const string UpdateInstallStartedUtcKey = "update_install_started_utc";
    public const string HotkeysEnabledKey = "hotkeys_enabled";
    public const string HotkeyBindingsKey = "hotkey_bindings";

    public static readonly TimeSpan UpdateInstallInProgressWindow = TimeSpan.FromMinutes(15);

    public void SaveLastUpdateCheckUtc(DateTimeOffset utcNow)
    {
        MergeAndSave(new Dictionary<string, object?>
        {
            [AutoUpdateCheckService.LastUpdateCheckUtcKey] = utcNow.UtcDateTime.ToString(
                "o",
                CultureInfo.InvariantCulture),
        });
    }

    public void SaveLastInstalledVersionAttempt(Version targetVersion)
    {
        MergeAndSave(new Dictionary<string, object?>
        {
            [LastInstalledVersionKey] = targetVersion.ToString(3),
            [LastInstalledVersionUtcKey] = DateTimeOffset.UtcNow.UtcDateTime.ToString(
                "o",
                CultureInfo.InvariantCulture),
            [UpdateInstallStartedUtcKey] = null,
        });
    }

    public void SaveUpdateInstallStartedUtc(DateTimeOffset utcNow)
    {
        MergeAndSave(new Dictionary<string, object?>
        {
            [UpdateInstallStartedUtcKey] = utcNow.UtcDateTime.ToString(
                "o",
                CultureInfo.InvariantCulture),
        });
    }

    public void ClearLastInstalledVersionAttempt()
    {
        var path = RepoPaths.SettingsPath;

        lock (_ioLock)
        {
            var root = LoadMutableRoot(path);
            var changed = root.Remove(LastInstalledVersionKey)
                | root.Remove(LastInstalledVersionUtcKey)
                | root.Remove(UpdateInstallStartedUtcKey);
            if (!changed)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(WriteJsonOptions));
        }
    }

    public static bool IsUpdateInstallInProgress(IReadOnlyDictionary<string, JsonElement> settings)
    {
        if (!settings.TryGetValue(UpdateInstallStartedUtcKey, out var utcEl)
            || utcEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = utcEl.GetString();
        if (string.IsNullOrWhiteSpace(raw)
            || !DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedUtc))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - startedUtc < UpdateInstallInProgressWindow;
    }

    public static bool ShouldSuppressUpdatePrompt(
        IReadOnlyDictionary<string, JsonElement> settings,
        Version current,
        Version? latest)
    {
        if (latest is null)
        {
            return false;
        }

        if (current >= latest)
        {
            return true;
        }

        if (settings.TryGetValue(LastInstalledVersionKey, out var versionEl)
            && versionEl.ValueKind == JsonValueKind.String
            && Version.TryParse(versionEl.GetString(), out var installedTarget)
            && installedTarget >= latest
            && current >= installedTarget)
        {
            return true;
        }

        return IsUpdateInstallInProgress(settings);
    }

    public WidgetSizePreset GetWidgetSizePreset(Dictionary<string, JsonElement> settings)
    {
        if (settings.TryGetValue("widget_size_preset", out var el)
            && el.ValueKind == JsonValueKind.String)
        {
            return WidgetSizePresets.FromSettingsValue(el.GetString());
        }

        return WidgetSizePreset.OneByOne;
    }

    public IReadOnlyList<string>? GetProfileOrder(Dictionary<string, JsonElement> settings)
    {
        if (!settings.TryGetValue("profile_order", out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var path = item.GetString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                list.Add(path);
            }
        }

        return list.Count > 0 ? list : null;
    }

    public void SaveProfileOrder(IReadOnlyList<string> orderedPaths)
    {
        MergeAndSave(new Dictionary<string, object?>
        {
            ["profile_order"] = orderedPaths.ToArray(),
        });
    }

    public bool GetHotkeysEnabled(Dictionary<string, JsonElement> settings) =>
        settings.TryGetValue(HotkeysEnabledKey, out var el) && el.ValueKind == JsonValueKind.True;

    public void SaveHotkeysEnabled(bool enabled)
    {
        MergeAndSave(new Dictionary<string, object?> { [HotkeysEnabledKey] = enabled });
    }

    public IReadOnlyList<HotkeyBinding> GetHotkeyBindings(Dictionary<string, JsonElement> settings)
    {
        var defaults = CreateDefaultHotkeyBindings();
        if (!settings.TryGetValue(HotkeyBindingsKey, out var root)
            || root.ValueKind != JsonValueKind.Object)
        {
            return defaults;
        }

        var merged = defaults.ToDictionary(b => b.ActionId, StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var obj = prop.Value;
            var enabled = obj.TryGetProperty("enabled", out var enabledEl)
                && enabledEl.ValueKind == JsonValueKind.True;
            uint modifiers = HotkeyModifiers.DefaultChord;
            if (obj.TryGetProperty("modifiers", out var modEl) && modEl.TryGetUInt32(out var mod))
            {
                modifiers = mod;
            }

            uint vk = 0;
            if (obj.TryGetProperty("vk", out var vkEl) && vkEl.TryGetUInt32(out var vkParsed))
            {
                vk = vkParsed;
            }

            merged[prop.Name] = new HotkeyBinding
            {
                ActionId = prop.Name,
                Enabled = enabled,
                Modifiers = modifiers,
                VirtualKey = vk,
            };
        }

        return HotkeyActionIds.All.Select(id => merged[id]).ToList();
    }

    public void SaveHotkeyBinding(string actionId, bool enabled, uint? modifiers = null, uint? virtualKey = null)
    {
        var current = GetHotkeyBindings(Load()).ToDictionary(b => b.ActionId, StringComparer.OrdinalIgnoreCase);
        if (!current.TryGetValue(actionId, out var existing))
        {
            existing = CreateDefaultHotkeyBindings().First(b =>
                string.Equals(b.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        }

        var updated = new HotkeyBinding
        {
            ActionId = actionId,
            Enabled = enabled,
            Modifiers = modifiers ?? existing.Modifiers,
            VirtualKey = virtualKey ?? existing.VirtualKey,
        };
        current[actionId] = updated;
        SaveHotkeyBindings(current.Values.ToList());
    }

    public void ClearHotkeyBinding(string actionId)
    {
        var current = GetHotkeyBindings(Load()).ToDictionary(b => b.ActionId, StringComparer.OrdinalIgnoreCase);
        if (!current.TryGetValue(actionId, out var existing))
        {
            return;
        }

        current[actionId] = new HotkeyBinding
        {
            ActionId = actionId,
            Enabled = false,
            Modifiers = existing.Modifiers,
            VirtualKey = 0,
        };
        SaveHotkeyBindings(current.Values.ToList());
    }

    public static string? FindDuplicateBindingOwner(
        IReadOnlyList<HotkeyBinding> bindings,
        string actionId,
        uint modifiers,
        uint virtualKey)
    {
        if (virtualKey == 0)
        {
            return null;
        }

        foreach (var binding in bindings)
        {
            if (string.Equals(binding.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (binding.VirtualKey == virtualKey && binding.Modifiers == modifiers)
            {
                return binding.ActionId;
            }
        }

        return null;
    }

    public void SaveHotkeyBindings(IReadOnlyList<HotkeyBinding> bindings)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            map[binding.ActionId] = new Dictionary<string, object?>
            {
                ["enabled"] = binding.Enabled,
                ["modifiers"] = binding.Modifiers,
                ["vk"] = binding.VirtualKey,
            };
        }

        MergeAndSave(new Dictionary<string, object?> { [HotkeyBindingsKey] = map });
    }

    public static string FormatHotkeyChord(uint modifiers, uint virtualKey)
    {
        if (virtualKey == 0)
        {
            return "(none)";
        }

        var parts = new List<string>();
        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(FormatVirtualKey(virtualKey));
        return string.Join("+", parts);
    }

    private static string FormatVirtualKey(uint vk) =>
        vk switch
        {
            0x30 => "0",
            0x31 => "1",
            0x32 => "2",
            0x33 => "3",
            0x34 => "4",
            0x35 => "5",
            0x36 => "6",
            0x37 => "7",
            0x38 => "8",
            0x39 => "9",
            0x41 => "A",
            0x42 => "B",
            0x43 => "C",
            0x44 => "D",
            0x45 => "E",
            0x46 => "F",
            0x47 => "G",
            0x48 => "H",
            0x49 => "I",
            0x4A => "J",
            0x4B => "K",
            0x4C => "L",
            0x4D => "M",
            0x4E => "N",
            0x4F => "O",
            0x50 => "P",
            0x51 => "Q",
            0x52 => "R",
            0x53 => "S",
            0x54 => "T",
            0x55 => "U",
            0x56 => "V",
            0x57 => "W",
            0x58 => "X",
            0x59 => "Y",
            0x5A => "Z",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0xBD => "-",
            0xBB => "=",
            0xBC => ",",
            0xBE => ".",
            0xBA => ";",
            0xDE => "'",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBF => "/",
            0xC0 => "`",
            0x20 => "Space",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x1B => "Esc",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => $"0x{vk:X}",
        };

    public static IReadOnlyList<HotkeyBinding> CreateDefaultHotkeyBindings()
    {
        var bindings = new List<HotkeyBinding>();
        for (var slot = 1; slot <= HotkeyActionIds.MaxRestoreSlots; slot++)
        {
            var actionId = HotkeyActionIds.RestoreSlotActionId(slot);
            if (actionId is null)
            {
                continue;
            }

            bindings.Add(new HotkeyBinding
            {
                ActionId = actionId,
                Enabled = false,
                Modifiers = HotkeyModifiers.DefaultChord,
                VirtualKey = (uint)(0x30 + slot),
            });
        }

        bindings.Add(new HotkeyBinding
        {
            ActionId = HotkeyActionIds.CycleNextProfile,
            Enabled = false,
            Modifiers = HotkeyModifiers.DefaultChord,
            VirtualKey = 0x4E,
        });
        bindings.Add(new HotkeyBinding
        {
            ActionId = HotkeyActionIds.ShowWidget,
            Enabled = false,
            Modifiers = HotkeyModifiers.DefaultChord | HotkeyModifiers.Shift,
            VirtualKey = 0x57,
        });
        return bindings;
    }

    public static string GetHotkeyActionLabel(string actionId)
    {
        var slot = HotkeyActionIds.SlotFromRestoreActionId(actionId);
        if (slot is not null)
        {
            return $"Restore profile slot {slot}";
        }

        return actionId switch
        {
            HotkeyActionIds.CycleNextProfile => "Cycle next profile",
            HotkeyActionIds.ShowWidget => "Show widget",
            _ => actionId,
        };
    }

    public ThemePreference GetThemePreference(Dictionary<string, JsonElement> settings)
    {
        if (settings.TryGetValue("gui_theme", out var el) && el.ValueKind == JsonValueKind.String)
        {
            return AppTheme.FromSettingsValue(el.GetString());
        }

        if (settings.TryGetValue("gui_dark_mode", out var legacyDark))
        {
            return legacyDark.ValueKind == JsonValueKind.True
                ? ThemePreference.Dark
                : ThemePreference.Light;
        }

        return ThemePreference.System;
    }

    public static (int Width, int Height, int X, int Y)? ParseWindowGeometry(Dictionary<string, JsonElement> settings)
    {
        if (settings.TryGetValue("window_geometry", out var geomEl)
            && geomEl.ValueKind == JsonValueKind.String)
        {
            var parsed = ParseTkGeometry(geomEl.GetString() ?? "");
            if (parsed is not null)
            {
                return parsed;
            }
        }

        try
        {
            if (!settings.TryGetValue("gui_window_w", out var wEl)
                || !settings.TryGetValue("gui_window_h", out var hEl)
                || !settings.TryGetValue("gui_window_x", out var xEl)
                || !settings.TryGetValue("gui_window_y", out var yEl))
            {
                return null;
            }

            var w = wEl.GetInt32();
            var h = hEl.GetInt32();
            var x = xEl.GetInt32();
            var y = yEl.GetInt32();
            if (w < 1 || h < 1)
            {
                return null;
            }

            return (w, h, x, y);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string FormatTkGeometry(int width, int height, int x, int y) =>
        $"{width}x{height}+{x}+{y}";

    public static (int Width, int Height, int X, int Y)? TryParseTkGeometry(string? geom)
    {
        if (string.IsNullOrWhiteSpace(geom))
        {
            return null;
        }

        return ParseTkGeometry(geom);
    }

    private static (int Width, int Height, int X, int Y)? ParseTkGeometry(string geom)
    {
        var m = GeometryRe.Match(geom.Trim());
        if (!m.Success)
        {
            return null;
        }

        var w = int.Parse(m.Groups[1].Value);
        var h = int.Parse(m.Groups[2].Value);
        var x = int.Parse(m.Groups[3].Value);
        var y = int.Parse(m.Groups[4].Value);
        if (w < 1 || h < 1)
        {
            return null;
        }

        return (w, h, x, y);
    }

    public static string DialogGeometryKey(string dialogId) => $"dialog_geometry_{dialogId}";

    private static JsonObject LoadMutableRoot(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
        catch (IOException)
        {
            return new JsonObject();
        }
    }

    private static JsonNode? CreateSettingsNode(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return s;
            case bool b:
                return b;
            case int i:
                return i;
            case long l:
                return l;
            case double d:
                return d;
            case IEnumerable<string> strings:
                var array = new JsonArray();
                foreach (var item in strings)
                {
                    array.Add(item);
                }

                return array;
            default:
                return JsonSerializer.SerializeToNode(value, WriteJsonOptions);
        }
    }
}
