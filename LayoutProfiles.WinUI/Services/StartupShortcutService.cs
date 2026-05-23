using LayoutProfiles.WinUI.Helpers;

namespace LayoutProfiles.WinUI.Services;

public static class StartupShortcutService
{
    private const string BatName = "SnapdeskWidget.bat";
    private const string LegacyBatName = "LayoutProfilesWidget.bat";

    private static string StartupDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            "Startup");

    private static string BatPath => Path.Combine(StartupDir, BatName);

    private static string LegacyBatPath => Path.Combine(StartupDir, LegacyBatName);

    private static string LauncherCmdPath
    {
        get
        {
            var open = Path.Combine(RepoPaths.RepoRoot, "Open Snapdesk.cmd");
            if (File.Exists(open))
            {
                return open;
            }

            var published = Path.Combine(RepoPaths.RepoRoot, "Snapdesk.cmd");
            if (File.Exists(published))
            {
                return published;
            }

            return Path.Combine(RepoPaths.RepoRoot, "launchers", "run_winui.cmd");
        }
    }

    public static bool Exists() => File.Exists(BatPath);

    public static void SyncFromSettings(bool wantRunOnStartup)
    {
        if (wantRunOnStartup)
        {
            Install();
        }
        else if (Exists())
        {
            Remove();
        }
    }

    public static void Install()
    {
        var launcher = LauncherCmdPath;
        if (!File.Exists(launcher))
        {
            return;
        }

        Directory.CreateDirectory(StartupDir);
        var repo = RepoPaths.RepoRoot;
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            $"cd /d \"{repo}\"",
            $"start \"\" \"{launcher}\"",
            "exit /b 0",
            "",
        };
        File.WriteAllLines(BatPath, lines);
        RemoveLegacyBat();
    }

    public static void Remove()
    {
        try
        {
            if (File.Exists(BatPath))
            {
                File.Delete(BatPath);
            }

            RemoveLegacyBat();
        }
        catch (IOException ex)
        {
            CrashLog.WriteDiagnostic("StartupShortcutService.RemoveLegacyBat", $"ignored: {ex.Message}"); // ignore
        }
    }

    private static void RemoveLegacyBat()
    {
        try
        {
            if (File.Exists(LegacyBatPath))
            {
                File.Delete(LegacyBatPath);
            }
        }
        catch (IOException ex)
        {
            CrashLog.WriteDiagnostic("StartupShortcutService.Delete", $"ignored: {ex.Message}"); // ignore
        }
    }
}
