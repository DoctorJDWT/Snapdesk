using System.Globalization;
using System.Text.Json;

namespace LayoutProfiles.WinUI.Services;

/// <summary>Throttles GitHub release checks to at most once per 24 hours unless forced.</summary>
public sealed class AutoUpdateCheckService
{
    public const string LastUpdateCheckUtcKey = "last_update_check_utc";

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(24);

    private readonly SettingsService _settings;
    private readonly GitHubReleaseUpdateChecker _checker;

    public AutoUpdateCheckService(SettingsService settings, GitHubReleaseUpdateChecker checker)
    {
        _settings = settings;
        _checker = checker;
    }

    public bool IsDueForCheck(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var last = GetLastUpdateCheckUtc(settings);
        if (last is null)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - last.Value >= MinimumInterval;
    }

    public static DateTimeOffset? GetLastUpdateCheckUtc(IReadOnlyDictionary<string, JsonElement> settings)
    {
        if (!settings.TryGetValue(LastUpdateCheckUtcKey, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = el.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Checks GitHub for a newer release when due or <paramref name="force"/> is true.
    /// Returns (skipped: true, result: null) when throttled; otherwise persists
    /// <see cref="LastUpdateCheckUtcKey"/> after the network call completes.
    /// </summary>
    public async Task<(bool Skipped, ReleaseCheckResult? Result)> CheckAsync(
        IReadOnlyDictionary<string, JsonElement> settings,
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (!force && !IsDueForCheck(settings))
        {
            return (true, null);
        }

        var result = await _checker.CheckLatestAsync(cancellationToken).ConfigureAwait(false);
        _settings.SaveLastUpdateCheckUtc(DateTimeOffset.UtcNow);
        return (false, result);
    }
}
