using LayoutProfiles.WinUI.Services;

namespace LayoutProfiles.WinUI.Helpers;

internal static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        RepoPaths.LayoutProfilesDataDir,
        "snapdesk-last-error.txt");

    public static void Write(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(RepoPaths.LayoutProfilesDataDir);
            var text =
                $"[{DateTimeOffset.Now:u}] {context}{Environment.NewLine}" +
                ex + Environment.NewLine;
            File.WriteAllText(LogPath, text);
        }
        catch
        {
            // ignore logging failures
        }
    }
}
