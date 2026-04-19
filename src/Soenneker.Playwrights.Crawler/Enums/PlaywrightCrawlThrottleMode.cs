using Soenneker.Gen.EnumValues;

namespace Soenneker.Playwrights.Crawler.Enums;

/// <summary>
/// Controls how the crawler applies automatic pacing and adaptive throttling.
/// </summary>
[EnumValue]
public sealed partial class PlaywrightCrawlThrottleMode
{
    /// <summary>
    /// Uses the crawler's built-in pacing, adaptive slow mode, cooldown handling, and implicit post-navigation jitter.
    /// </summary>
    public static readonly PlaywrightCrawlThrottleMode Automatic = new(0);

    /// <summary>
    /// Disables automatic pacing, adaptive slow mode, cooldown waiting, and implicit post-navigation jitter.
    /// Configured concurrency limits and retries still apply.
    /// </summary>
    public static readonly PlaywrightCrawlThrottleMode Disabled = new(1);
}
