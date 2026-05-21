using System.Diagnostics;
using System.Reflection;

namespace LayoutProfiles.WinUI.Services;

/// <summary>
/// Current app version from the running executable's file version, with assembly
/// metadata as fallback (<see cref="LayoutProfiles.WinUI.csproj"/> Version).
/// </summary>
public static class AppVersion
{
    public static Version Current { get; } = ResolveCurrent();

    public static string Display => Current.ToString(3);

    private static Version ResolveCurrent()
    {
        if (TryGetVersionFromProcessPath(out var fromExe))
        {
            return fromExe;
        }

        var asm = typeof(AppVersion).Assembly;
        var informational = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)
            && Version.TryParse(NormalizeVersionString(informational), out var fromInfo))
        {
            return fromInfo;
        }

        return asm.GetName().Version ?? new Version(1, 0, 0);
    }

    public static bool TryGetVersionFromFile(string? path, out Version version)
    {
        version = new Version(1, 0, 0);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            foreach (var candidate in new[] { info.FileVersion, info.ProductVersion })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var normalized = NormalizeVersionString(candidate);
                if (Version.TryParse(normalized, out var parsed))
                {
                    version = parsed;
                    return true;
                }
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    private static bool TryGetVersionFromProcessPath(out Version version) =>
        TryGetVersionFromFile(Environment.ProcessPath, out version);

    private static string NormalizeVersionString(string raw)
    {
        var trimmed = raw.Trim();
        var plus = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }

        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            trimmed = trimmed[..dash];
        }

        return trimmed;
    }
}
