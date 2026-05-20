using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LayoutProfiles.WinUI.Helpers;

namespace LayoutProfiles.WinUI.Services;

public sealed class SettingsService
{
    private static readonly Regex GeometryRe = new(
        @"^(\d+)x(\d+)([+-]\d+)([+-]\d+)$",
        RegexOptions.Compiled);

    public Dictionary<string, JsonElement> Load()
    {
        var path = RepoPaths.SettingsPath;
        if (!File.Exists(path))
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
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

        var root = LoadMutableRoot(path);

        foreach (var (key, value) in updates)
        {
            root[key] = value switch
            {
                null => null,
                string s => s,
                bool b => b,
                int i => i,
                long l => l,
                double d => d,
                _ => JsonValue.Create(value),
            };
        }

        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool GetRunOnStartup(Dictionary<string, JsonElement> settings) =>
        settings.TryGetValue("gui_run_on_startup", out var el)
        && el.ValueKind == JsonValueKind.True;

    public WidgetSizePreset GetWidgetSizePreset(Dictionary<string, JsonElement> settings)
    {
        if (settings.TryGetValue("widget_size_preset", out var el)
            && el.ValueKind == JsonValueKind.String)
        {
            return WidgetSizePresets.FromSettingsValue(el.GetString());
        }

        return WidgetSizePreset.OneByOne;
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

        return ThemePreference.Dark;
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
}
