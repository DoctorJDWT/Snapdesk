using System.Diagnostics;
using System.Runtime.InteropServices;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Xaml;

namespace LayoutProfiles.WinUI.Helpers;

internal static class UninstallSnapdesk
{
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconWarning = 0x00000030;
    private const int IdYes = 6;

    private const string ConfirmMessage =
        "This removes Snapdesk, your profiles, and shortcuts. Continue?";

    public static bool Confirm()
    {
        try
        {
            return MessageBoxW(IntPtr.Zero, ConfirmMessage, "Uninstall Snapdesk", MbYesNo | MbIconWarning) == IdYes;
        }
        catch
        {
            return false;
        }
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, BestFitMapping = false)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
