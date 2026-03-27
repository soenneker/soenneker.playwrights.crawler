namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// Baseline crawl policy for concurrency, timing, retries, slowdown, and cooldown behavior.
/// </summary>
public sealed class PlaywrightCrawlPolicy
{
    /// <summary>
    /// Maximum total concurrent crawl operations across all domains.
    /// </summary>
    public int GlobalMaxConcurrency { get; set; } = 20;

    /// <summary>
    /// Maximum concurrent crawl operations per domain during normal mode.
    /// </summary>
    public int PerDomainMaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Maximum concurrent crawl operations per resolved IP during normal mode.
    /// </summary>
    public int PerIpMaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Base minimum delay between requests to the same domain during normal mode.
    /// </summary>
    public int MinimumDelayBetweenRequestsMs { get; set; } = 750;

    /// <summary>
    /// Maximum random jitter added to the normal same-domain delay.
    /// </summary>
    public int DelayJitterMaxMs { get; set; } = 500;

    /// <summary>
    /// Default non-navigation Playwright timeout used for waits and selectors.
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Number of retry attempts for retryable statuses and transient network failures.
    /// Defaults to 0 so retries are opt-in rather than automatic.
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Base retry delay in milliseconds before exponential backoff is applied.
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 2_000;

    /// <summary>
    /// Sliding window used for repeated blocking signals such as 429 and 403 responses.
    /// </summary>
    public int SlowModeSignalWindowMs { get; set; } = 60_000;

    /// <summary>
    /// Per-domain concurrency while in slow mode.
    /// </summary>
    public int SlowModePerDomainMaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Base minimum delay between requests to the same domain while in slow mode.
    /// </summary>
    public int SlowModeMinimumDelayBetweenRequestsMs { get; set; } = 3_000;

    /// <summary>
    /// Maximum random jitter added to slow-mode same-domain delays.
    /// </summary>
    public int SlowModeDelayJitterMaxMs { get; set; } = 2_000;

    /// <summary>
    /// How long a domain stays in slow mode after a trigger.
    /// </summary>
    public int SlowModeDurationMs { get; set; } = 900_000;

    /// <summary>
    /// How long a domain stays in cooldown when blocking continues during slow mode.
    /// </summary>
    public int CooldownDurationMs { get; set; } = 1_800_000;

    /// <summary>
    /// Median navigation duration threshold that triggers slow mode.
    /// </summary>
    public int SlowModeMedianResponseThresholdMs { get; set; } = 8_000;

    /// <summary>
    /// Minimum post-navigation jitter applied between page transitions when no explicit fixed delay is configured.
    /// </summary>
    public int PostNavigationJitterMinMs { get; set; } = 300;

    /// <summary>
    /// Maximum post-navigation jitter applied between page transitions when no explicit fixed delay is configured.
    /// </summary>
    public int PostNavigationJitterMaxMs { get; set; } = 1_200;

    /// <summary>
    /// Minimum same-host page-to-page delay target.
    /// </summary>
    public int SameHostPageToPageDelayMinMs { get; set; } = 1_000;

    /// <summary>
    /// Maximum same-host page-to-page delay target.
    /// </summary>
    public int SameHostPageToPageDelayMaxMs { get; set; } = 2_500;

    /// <summary>
    /// Error-rate threshold that triggers slow mode once enough attempts have been made.
    /// </summary>
    public double ErrorRateThreshold { get; set; } = 0.25d;

    /// <summary>
    /// Duplicate-page threshold used for diagnostics only.
    /// Duplicate pages no longer trigger slow mode or cooldown automatically.
    /// </summary>
    public int DuplicatePageThreshold { get; set; } = 100;
}
