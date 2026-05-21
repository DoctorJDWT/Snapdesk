using System.Diagnostics;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Xaml;

namespace LayoutProfiles.WinUI.Helpers;

internal static class UninstallSnapdesk
{
    public static Task<bool> ConfirmAsync() => UninstallConfirmWindow.ShowConfirmAsync();

    public static void LaunchDetachedAndExit()
    {
        var installDir = ResolveInstallDir();
        var scriptPath = ResolveScriptPath(installDir)
            ?? throw new FileNotFoundException("Uninstall script not found.");

        var waitPid = Environment.ProcessId;
        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" " +
            $"-InstallDir \"{installDir}\" -WaitPid {waitPid}";

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = true,
        });

        if (Application.Current is not null)
        {
            Application.Current.Exit();
            return;
        }

        Environment.Exit(0);
    }

    private static string ResolveInstallDir()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return Path.GetDirectoryName(processPath)!;
        }

        return AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string? ResolveScriptPath(string installDir)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(installDir, "scripts", "Uninstall-Snapdesk.ps1"),
                     Path.Combine(RepoPaths.RepoRoot, "scripts", "Uninstall-Snapdesk.ps1"),
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
