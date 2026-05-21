using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using LayoutProfiles.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Windows.System;

namespace LayoutProfiles.WinUI.Services;

/// <summary>Downloads the latest release zip and runs Install-Snapdesk.ps1 after the app exits.</summary>
public sealed class UpdateInstallService
{
    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<bool> TryInstallUpdateAsync(
        ReleaseCheckResult result,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(result.SetupZipDownloadUrl))
        {
            ShowInstallFailedWithGithubFallback(
                "The release does not include a Snapdesk-Setup.zip download.",
                result.ReleasePageUrl);
            return false;
        }

        string? extractDir = null;
        try
        {
            NativeMessageBox.Show(
                "Snapdesk",
                "Downloading update…",
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);

            extractDir = await DownloadAndExtractSetupZipAsync(result.SetupZipDownloadUrl, cancellationToken)
                .ConfigureAwait(false);

            var scriptPath = Path.Combine(extractDir, "scripts", "Install-Snapdesk.ps1");
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException(
                    "Installer script was not found in the downloaded update package.",
                    scriptPath);
            }

            NativeMessageBox.Show(
                "Snapdesk",
                "Installing update… Snapdesk will close.",
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);

            LaunchInstallDetached(scriptPath, ResolveInstallDir(), Environment.ProcessId);
            ExitApplication();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (extractDir is not null)
            {
                TryDeleteDirectory(extractDir);
            }

            ShowInstallFailedWithGithubFallback(ex.Message, result.ReleasePageUrl);
            return false;
        }
    }

    public static void ShowInstallFailedWithGithubFallback(string message, string releasePageUrl)
    {
        var open = NativeMessageBox.Show(
            "Snapdesk",
            $"{message}\n\nOpen the release page in your browser to download manually?",
            NativeMessageBox.MbYesNo | NativeMessageBox.MbIconWarning);
        if (open == NativeMessageBox.IdYes)
        {
            _ = OpenReleasePageAsync(releasePageUrl);
        }
    }

    private static async Task OpenReleasePageAsync(string url)
    {
        try
        {
            _ = await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            NativeMessageBox.Show(
                "Snapdesk",
                $"Could not open the browser: {ex.Message}",
                NativeMessageBox.MbOk | NativeMessageBox.MbIconError);
        }
    }

    private static async Task<string> DownloadAndExtractSetupZipAsync(
        string downloadUrl,
        CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(
            Path.GetTempPath(),
            $"Snapdesk-Setup-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(
            Path.GetTempPath(),
            $"Snapdesk-Update-{Guid.NewGuid():N}");

        try
        {
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Download failed ({(int)response.StatusCode}). Check your connection and try again.");
            }

            await using (var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = File.Create(zipPath))
            {
                await network.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            return extractDir;
        }
        finally
        {
            TryDeleteFile(zipPath);
        }
    }

    private static void LaunchInstallDetached(string scriptPath, string installDir, int waitPid)
    {
        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" " +
            $"-NoPrompt -InstallDir \"{installDir}\" -ProcessId {waitPid} -LaunchAfterInstall";

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = true,
        });
    }

    private static void ExitApplication()
    {
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Snapdesk/{AppVersion.Display}");
        return client;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
