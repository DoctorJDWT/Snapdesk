using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using LayoutProfiles.WinUI.Helpers;
using Windows.System;

namespace LayoutProfiles.WinUI.Services;

/// <summary>Downloads the latest release zip and runs Install-Snapdesk.ps1 after the app exits.</summary>
public sealed class UpdateInstallService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly SettingsService _settings;

    public UpdateInstallService(SettingsService? settings = null)
    {
        _settings = settings ?? new SettingsService();
    }

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

        if (result.LatestVersion is null)
        {
            ShowInstallFailedWithGithubFallback(
                "The release version could not be determined.",
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

            var installDir = ResolveInstallDir();
            var expectedVersion = result.LatestVersion.ToString(3);
            if (!VerifyPayloadVersion(extractDir, expectedVersion))
            {
                throw new InvalidOperationException(
                    $"Downloaded update does not contain version {expectedVersion}. Try again later or install manually from GitHub.");
            }

            _settings.SaveUpdateInstallStartedUtc(DateTimeOffset.UtcNow);

            RemoveSnapdeskStartupShortcuts();

            NativeMessageBox.Show(
                "Snapdesk",
                $"Installing update {expectedVersion}… Snapdesk will close and restart.",
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);

            LaunchInstallDetached(scriptPath, installDir, Environment.ProcessId, expectedVersion);
            ExitApplicationForUpdate();
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

    public static bool TryReadInstalledExeVersion(string installDir, out Version version)
    {
        version = new Version(1, 0, 0);
        foreach (var name in new[] { "Snapdesk.exe", "LayoutProfiles.WinUI.exe" })
        {
            var path = Path.Combine(installDir, name);
            if (AppVersion.TryGetVersionFromFile(path, out version))
            {
                return true;
            }
        }

        return false;
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

    public static void ShowUpdateCompleteIfPending(SettingsService settings)
    {
        var loaded = settings.Load();
        if (!loaded.TryGetValue(SettingsService.LastInstalledVersionKey, out var versionEl)
            || versionEl.ValueKind != JsonValueKind.String
            || !Version.TryParse(versionEl.GetString(), out var target))
        {
            return;
        }

        var current = AppVersion.Current;
        if (current < target)
        {
            return;
        }

        NativeMessageBox.Show(
            "Snapdesk",
            $"Snapdesk was updated to version {AppVersion.Display}.",
            NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);

        settings.ClearLastInstalledVersionAttempt();
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

    private static bool VerifyPayloadVersion(string extractDir, string expectedVersion)
    {
        var distDir = Path.Combine(extractDir, "dist", "Snapdesk");
        if (!Directory.Exists(distDir))
        {
            return false;
        }

        if (!TryReadInstalledExeVersion(distDir, out var payloadVersion))
        {
            return false;
        }

        if (!Version.TryParse(expectedVersion, out var expected))
        {
            return false;
        }

        return payloadVersion >= expected;
    }

    private static void RemoveSnapdeskStartupShortcuts()
    {
        var startupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup");
        foreach (var name in new[] { "SnapdeskWidget.bat", "LayoutProfilesWidget.bat" })
        {
            TryDeleteFile(Path.Combine(startupDir, name));
        }
    }

    private static void LaunchInstallDetached(
        string scriptPath,
        string installDir,
        int waitPid,
        string expectedVersion)
    {
        RemoveSnapdeskStartupShortcuts();

        var logPath = Path.Combine(RepoPaths.LayoutProfilesDataDir, "update-install.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" " +
            $"-NoPrompt -InstallDir \"{installDir}\" -ProcessId {waitPid} -LaunchAfterInstall " +
            $"-ExpectedVersion \"{expectedVersion}\" -LogPath \"{logPath}\"";

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
        }) ?? throw new InvalidOperationException(
            "Could not start the update installer. Try installing manually from GitHub.");

        StartupTrace.Write($"Update installer started pid={process.Id} log={logPath}");
    }

    /// <summary>Terminate immediately so the single-instance mutex and file locks are released.</summary>
    private static void ExitApplicationForUpdate()
    {
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
