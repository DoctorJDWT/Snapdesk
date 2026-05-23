// MainWindow.Updates.cs — Update checking, update menu synchronization, update installation
// prompts and background check logic extracted from MainWindow.xaml.cs.

using LayoutProfiles.WinUI.Helpers;
using LayoutProfiles.WinUI.Models;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

#nullable enable

namespace LayoutProfiles.WinUI;

public sealed partial class MainWindow
{
    private void SyncUpdateMenuFromCache()
    {
        if (_updateAvailableItem is null)
        {
            return;
        }

        if (_cachedUpdateResult is { Succeeded: true, IsUpdateAvailable: true, LatestVersion: { } latest }
            && !ShouldSuppressUpdatePrompt(_cachedUpdateResult))
        {
            _updateAvailableItem.Text = $"Update available ({latest})";
            _updateAvailableItem.Visibility = Visibility.Visible;
            return;
        }

        _updateAvailableItem.Visibility = Visibility.Collapsed;
    }

    private bool ShouldSuppressUpdatePrompt(ReleaseCheckResult result) =>
        SettingsService.ShouldSuppressUpdatePrompt(
            _settingsCache,
            result.CurrentVersion,
            result.LatestVersion);

    private void ScheduleBackgroundUpdateCheck()
    {
        _ = RunUpdateCheckAsync(force: false, showDialogs: false);
    }

    private async Task RunUpdateCheckAsync(bool force, bool showDialogs)
    {
        if (_updateCheckInProgress)
        {
            return;
        }

        _updateCheckInProgress = true;
        var previousText = _checkForUpdatesItem?.Text;
        if (showDialogs && _checkForUpdatesItem is not null)
        {
            _checkForUpdatesItem.IsEnabled = false;
            _checkForUpdatesItem.Text = "Checking for updates…";
        }

        try
        {
            var (skipped, result) = await _autoUpdateCheck
                .CheckAsync(_settingsCache, force)
                .ConfigureAwait(showDialogs);

            if (skipped)
            {
                StartupTrace.Write("Update check skipped (checked within 24h)");
                return;
            }

            if (result is null)
            {
                return;
            }

            ReloadSettingsCache();
            ApplyUpdateCheckResultOnUiThread(result);

            if (showDialogs)
            {
                ShowManualUpdateCheckDialogs(result);
            }
            else if (result.Succeeded && result.IsUpdateAvailable && !ShouldSuppressUpdatePrompt(result))
            {
                PromptInstallUpdateOnUiThread(result);
            }
            else
            {
                LogSilentUpdateCheckResult(result);
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"Update check error: {ex.Message}");
        }
        finally
        {
            _updateCheckInProgress = false;
            if (showDialogs && _checkForUpdatesItem is not null)
            {
                _checkForUpdatesItem.IsEnabled = true;
                _checkForUpdatesItem.Text = previousText ?? "Check for updates";
            }
        }
    }

    private void ApplyUpdateCheckResultOnUiThread(ReleaseCheckResult result)
    {
        void Apply()
        {
            _cachedUpdateResult = result;
            SyncUpdateMenuFromCache();
        }

        var queue = DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            Apply();
            return;
        }

        queue.TryEnqueue(Apply);
    }

    private void ReloadSettingsCache()
    {
        _settingsCache = _settings.Load();
    }

    private static void LogSilentUpdateCheckResult(ReleaseCheckResult result)
    {
        if (!result.Succeeded)
        {
            StartupTrace.Write($"Update check failed: {result.ErrorMessage ?? "unknown"}");
            return;
        }

        if (result.IsUpdateAvailable && result.LatestVersion is not null)
        {
            StartupTrace.Write(
                $"Update available: {result.LatestVersion} (current {result.CurrentVersion})");
            return;
        }

        StartupTrace.Write($"Up to date ({result.CurrentVersion})");
    }

    private void ShowManualUpdateCheckDialogs(ReleaseCheckResult result)
    {
        if (!result.Succeeded)
        {
            UpdateInstallService.ShowInstallFailedWithGithubFallback(
                result.ErrorMessage ?? "Could not check for updates.",
                result.ReleasePageUrl);
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            NativeMessageBox.Show(
                "Snapdesk",
                result.StatusMessage,
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);
            return;
        }

        if (ShouldSuppressUpdatePrompt(result))
        {
            var versionLabel = result.LatestVersion?.ToString(3) ?? "newer";
            var message = SettingsService.IsUpdateInstallInProgress(_settingsCache)
                ? $"Update {versionLabel} is installing now. Snapdesk will restart when finished."
                : $"You're on the latest version ({result.CurrentVersion}).";
            NativeMessageBox.Show(
                "Snapdesk",
                message,
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);
            return;
        }

        if (PromptInstallUpdate(result))
        {
            _ = RunInstallUpdateAsync(result);
        }
    }

    private void PromptInstallUpdateOnUiThread(ReleaseCheckResult result)
    {
        void Prompt()
        {
            if (PromptInstallUpdate(result))
            {
                _ = RunInstallUpdateAsync(result);
            }
        }

        var queue = DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            Prompt();
            return;
        }

        queue.TryEnqueue(Prompt);
    }

    private static bool PromptInstallUpdate(ReleaseCheckResult result)
    {
        var versionLabel = result.LatestVersion?.ToString(3)
                             ?? result.LatestTag
                             ?? "newer";
        var choice = NativeMessageBox.Show(
            "Snapdesk",
            $"Update available: {versionLabel} (you have {result.CurrentVersion}). Install now?",
            NativeMessageBox.MbYesNo | NativeMessageBox.MbIconQuestion);
        return choice == NativeMessageBox.IdYes;
    }

    private async Task RunInstallUpdateAsync(ReleaseCheckResult result)
    {
        await _updateInstallService.TryInstallUpdateAsync(result).ConfigureAwait(true);
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_checkForUpdatesItem is null)
        {
            return;
        }

        await RunUpdateCheckAsync(force: true, showDialogs: true).ConfigureAwait(true);
    }

    private void OnUpdateAvailableClick(object sender, RoutedEventArgs e)
    {
        if (_cachedUpdateResult is { Succeeded: true, IsUpdateAvailable: true }
            && !ShouldSuppressUpdatePrompt(_cachedUpdateResult))
        {
            PromptInstallUpdateOnUiThread(_cachedUpdateResult);
            return;
        }

        var url = _cachedUpdateResult?.ReleasePageUrl ?? GitHubReleaseUpdateChecker.ReleasesLatestPage;
        _ = OpenReleasePageAsync(url);
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
}
