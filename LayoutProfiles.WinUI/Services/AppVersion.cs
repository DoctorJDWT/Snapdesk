using System.Reflection;

namespace LayoutProfiles.WinUI.Services;

/// <summary>Current app version from assembly metadata (<see cref="LayoutProfiles.WinUI.csproj"/> Version).</summary>
public static class AppVersion
{
    public static Version Current { get; } = ResolveCurrent();

    public static string Display => Current.ToString(3);

    private static Version ResolveCurrent()
    {
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

    private static string NormalizeVersionString(string raw)
    {
        var plus = raw.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? raw[..plus] : raw;
    }
}
