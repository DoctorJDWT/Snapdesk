using System.Text.Json;
using LayoutProfiles.WinUI.Models;

namespace LayoutProfiles.WinUI.Services;

public sealed class ProfileService
{
    public IReadOnlyList<ProfileRow> ListProfiles()
    {
        var dir = RepoPaths.ProfilesDir;
        Directory.CreateDirectory(dir);
        var rows = new List<ProfileRow>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var path = file;
            string display;
            int count;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                display = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? Path.GetFileNameWithoutExtension(path)
                    : Path.GetFileNameWithoutExtension(path);
                count = root.TryGetProperty("windows", out var wins) && wins.ValueKind == JsonValueKind.Array
                    ? wins.GetArrayLength()
                    : 0;
            }
            catch (JsonException)
            {
                display = Path.GetFileNameWithoutExtension(path);
                count = 0;
            }
            catch (IOException)
            {
                display = Path.GetFileNameWithoutExtension(path);
                count = 0;
            }

            rows.Add(new ProfileRow(display, count, path));
        }

        return rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<ProfileRow> ApplyDisplayOrder(
        IReadOnlyList<ProfileRow> rows,
        IReadOnlyList<string>? savedOrder)
    {
        if (savedOrder is null || savedOrder.Count == 0)
        {
            return rows;
        }

        var byPath = rows.ToDictionary(r => r.FilePath, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProfileRow>(rows.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in savedOrder)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (byPath.TryGetValue(path, out var row) && seen.Add(path))
            {
                result.Add(row);
            }
        }

        foreach (var row in rows
                     .Where(r => !seen.Contains(r.FilePath))
                     .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(row);
        }

        return result;
    }

    public IReadOnlyList<PickableWindow> ReadProfileWindows(string filePath)
    {
        var list = new List<PickableWindow>();
        if (!File.Exists(filePath))
        {
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            if (!doc.RootElement.TryGetProperty("windows", out var wins)
                || wins.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var w in wins.EnumerateArray())
            {
                if (!w.TryGetProperty("exe_path", out var exeEl)
                    || exeEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!w.TryGetProperty("title", out var titleEl) || titleEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var exe = exeEl.GetString() ?? string.Empty;
                var title = titleEl.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var browserUrl = w.TryGetProperty("browser_url", out var urlEl)
                    && urlEl.ValueKind == JsonValueKind.String
                    ? urlEl.GetString()
                    : null;
                list.Add(new PickableWindow(exe, title, browserUrl));
            }
        }
        catch (JsonException)
        {
            // ignore malformed profile
        }
        catch (IOException)
        {
            // ignore
        }

        return list;
    }

    public bool TryDeleteProfile(string filePath)
    {
        var profilesRoot = Path.GetFullPath(RepoPaths.ProfilesDir);
        string full;
        try
        {
            full = Path.GetFullPath(filePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!full.StartsWith(profilesRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(profilesRoot, full);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (!File.Exists(full))
        {
            return true;
        }

        try
        {
            File.Delete(full);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
