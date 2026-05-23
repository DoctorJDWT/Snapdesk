namespace LayoutProfiles.WinUI.Services;

/// <summary>
/// Simple shared service container so all windows use the same instances.
/// Avoids concurrent file I/O from duplicate SettingsService/ProfileService/PythonBridge instances.
/// </summary>
internal static class ServiceLocator
{
    public static SettingsService Settings { get; } = new();
    public static ProfileService Profiles { get; } = new();
    public static PythonBridge Python { get; } = new();
}
