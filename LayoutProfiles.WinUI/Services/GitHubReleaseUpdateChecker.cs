using System.Net.Http;
using System.Text.Json;

namespace LayoutProfiles.WinUI.Services;

public sealed record ReleaseCheckResult(
    bool Succeeded,
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    string? LatestTag,
    string ReleasePageUrl,
    string? SetupZipDownloadUrl,
    string? ErrorMessage)
{
    public static ReleaseCheckResult Failed(Version current, string message) =>
        new(false, false, current, null, null, GitHubReleaseUpdateChecker.ReleasesLatestPage, null, message);

    public string StatusMessage
    {
        get
        {
            if (!Succeeded)
            {
                return ErrorMessage ?? "Could not check for updates.";
            }

            if (IsUpdateAvailable && LatestVersion is not null)
            {
                return $"A newer version is available: {LatestVersion} (you have {CurrentVersion}).";
            }

            return $"You're on the latest version ({CurrentVersion}).";
        }
    }
}

public sealed class GitHubReleaseUpdateChecker
{
    public const string ReleasesLatestPage = "https://github.com/DoctorJDWT/Snapdesk/releases/latest";

    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/DoctorJDWT/Snapdesk/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public Version CurrentVersion => AppVersion.Current;

    public async Task<ReleaseCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApiUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ReleaseCheckResult.Failed(
                    current,
                    $"Update check failed ({(int)response.StatusCode}). Try again later.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl)
                ? tagEl.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(tag)
                || !TryParseReleaseVersion(tag, out var latest))
            {
                return ReleaseCheckResult.Failed(current, "Latest release version is unavailable.");
            }

            var pageUrl = root.TryGetProperty("html_url", out var urlEl)
                          && !string.IsNullOrWhiteSpace(urlEl.GetString())
                ? urlEl.GetString()!
                : ReleasesLatestPage;
            var zipUrl = FindSetupZipUrl(root);
            var updateAvailable = latest > current;

            return new ReleaseCheckResult(
                true,
                updateAvailable,
                current,
                latest,
                tag,
                pageUrl,
                zipUrl,
                null);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return ReleaseCheckResult.Failed(current, "Update check timed out. Check your connection and try again.");
        }
        catch (HttpRequestException)
        {
            return ReleaseCheckResult.Failed(
                current,
                "Could not reach GitHub. Check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            return ReleaseCheckResult.Failed(current, $"Update check failed: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Snapdesk/{AppVersion.Display}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string? FindSetupZipUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl)
                ? nameEl.GetString()
                : null;
            if (!string.Equals(name, "Snapdesk-Setup.zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (asset.TryGetProperty("browser_download_url", out var urlEl))
            {
                var url = urlEl.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    internal static bool TryParseReleaseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        return Version.TryParse(normalized, out version!);
    }
}
