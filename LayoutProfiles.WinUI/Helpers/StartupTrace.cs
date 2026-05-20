using LayoutProfiles.WinUI.Services;

namespace LayoutProfiles.WinUI.Helpers;

internal static class StartupTrace
{
    private static readonly string LogPath = Path.Combine(
        RepoPaths.LayoutProfilesDataDir,
        "snapdesk-startup.log");

    public static void Write(string step)
    {
        try
        {
            Directory.CreateDirectory(RepoPaths.LayoutProfilesDataDir);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:u}] {step}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }
}
