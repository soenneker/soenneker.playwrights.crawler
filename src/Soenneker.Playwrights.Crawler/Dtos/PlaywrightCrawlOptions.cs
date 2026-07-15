using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Playwrights.Extensions.Stealth.Options;

namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// Options for crawling and saving a site with Playwright.
/// </summary>
public sealed class PlaywrightCrawlOptions
{
    /// <summary>
    /// Absolute HTTP or HTTPS URL to begin crawling from.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Additional absolute HTTP or HTTPS URLs to crawl. All starting URLs share one browser and browser context.
    /// </summary>
    public List<string> StartingUrls { get; set; } = [];

    /// <summary>
    /// Optional exact allowlist for discovered pages. Entries may be absolute URLs or root-relative paths.
    /// Explicit <see cref="Url"/> and <see cref="StartingUrls"/> are always eligible.
    /// </summary>
    public List<string> AllowedPageUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets channel.
    /// </summary>
    public string Channel { get; set; } = "chromium";

    /// <summary>
    /// Directory where the crawled output should be written.
    /// </summary>
    public string? SaveDirectory { get; set; }

    /// <summary>
    /// Writes captured documents and resources to <see cref="SaveDirectory"/>. Disable for in-memory capture.
    /// </summary>
    public bool SaveToDisk { get; set; } = true;

    /// <summary>
    /// Includes rendered HTML in the returned page results. This is automatically enabled when <see cref="SaveToDisk"/> is false.
    /// </summary>
    public bool CaptureRenderedHtml { get; set; }

    /// <summary>
    /// Discovers and queues links found on rendered pages. Disable to capture only the explicitly supplied starting URLs.
    /// </summary>
    public bool DiscoverLinks { get; set; } = true;

    /// <summary>
    /// Controls whether to save just HTML documents or all observed page resources as well.
    /// </summary>
    public PlaywrightCrawlMode Mode { get; set; } = PlaywrightCrawlMode.HtmlOnly;

    /// <summary>
    /// Maximum link depth to follow from the root page. A value of 0 crawls only the starting page.
    /// </summary>
    public int MaxDepth { get; set; }

    /// <summary>
    /// Optional limit on the total number of pages to crawl.
    /// </summary>
    public int? MaxPages { get; set; }

    /// <summary>
    /// Optional limit on the total bytes written to disk.
    /// </summary>
    public long? MaxStorageBytes { get; set; }

    /// <summary>
    /// Optional maximum elapsed time for the crawl.
    /// </summary>
    public TimeSpan? MaxDuration { get; set; }

    /// <summary>
    /// When true, all starting and discovered pages must share the primary starting URL's host.
    /// </summary>
    public bool SameHostOnly { get; set; } = true;

    /// <summary>
    /// When true, query-string differences are ignored when determining whether two page URLs are duplicates.
    /// </summary>
    public bool IgnoreQueryStringsInDuplicateDetection { get; set; } = true;

    /// <summary>
    /// When true and <see cref="Mode"/> is <see cref="PlaywrightCrawlMode.Full"/>, cross-origin assets are saved under an <c>_external</c> folder.
    /// </summary>
    public bool IncludeCrossOriginAssets { get; set; }

    /// <summary>
    /// When true, cross-origin asset URLs in saved HTML are rewritten to their local <c>_external</c> paths.
    /// Requires <see cref="IncludeCrossOriginAssets"/> to be enabled.
    /// </summary>
    public bool RewriteCrossOriginAssetUrls { get; set; }

    /// <summary>
    /// When true, same-origin absolute URLs in saved HTML and CSS are rewritten to root-relative paths.
    /// For example, <c>https://example.com/script.js</c> becomes <c>/script.js</c>.
    /// </summary>
    public bool RewriteSameOriginAbsoluteUrls { get; set; }

    /// <summary>
    /// When true and <see cref="Mode"/> is <see cref="PlaywrightCrawlMode.Full"/>, scrolls pages after navigation to trigger lazy-loaded media before resources are saved.
    /// </summary>
    public bool TriggerLazyLoading { get; set; } = true;

    /// <summary>
    /// Pixel distance for each lazy-load scroll step.
    /// </summary>
    public int LazyLoadScrollStepPx { get; set; } = 900;

    /// <summary>
    /// Delay after each lazy-load scroll step.
    /// </summary>
    public int LazyLoadScrollDelayMs { get; set; } = 150;

    /// <summary>
    /// Maximum number of lazy-load scroll steps per page.
    /// </summary>
    public int LazyLoadMaxScrolls { get; set; } = 40;

    /// <summary>
    /// Formats saved HTML documents with Soenneker.Html.Formatter when true.
    /// </summary>
    public bool PrettyPrintHtml { get; set; }

    /// <summary>
    /// Deletes any existing output directory before crawling.
    /// </summary>
    public bool ClearSaveDirectory { get; set; }

    /// <summary>
    /// Overwrites existing files when true.
    /// </summary>
    public bool OverwriteExistingFiles { get; set; } = true;

    /// <summary>
    /// Launches the browser in headless mode when true.
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Enables the Soenneker stealth launch/context extensions when true.
    /// </summary>
    public bool UseStealth { get; set; } = true;

    /// <summary>
    /// Additional HTTP headers sent by every page in the shared browser context.
    /// </summary>
    public Dictionary<string, string> ExtraHttpHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional launch settings passed to the stealth extension when <see cref="UseStealth"/> is enabled.
    /// </summary>
    public StealthLaunchOptions? StealthLaunchOptions { get; set; }

    /// <summary>
    /// Optional context settings passed to the stealth extension when <see cref="UseStealth"/> is enabled.
    /// </summary>
    public StealthContextOptions? StealthContextOptions { get; set; }

    /// <summary>
    /// Baseline crawl-policy settings for concurrency, timing, retries, slowdown, and cooldown.
    /// </summary>
    public PlaywrightCrawlPolicy Policy { get; set; } = new();

    /// <summary>
    /// Controls automatic crawler pacing and adaptive throttling behavior.
    /// <see cref="PlaywrightCrawlThrottleMode.Disabled"/> skips automatic pacing, adaptive slow mode, cooldown waiting, and implicit post-navigation jitter.
    /// Configured concurrency limits and retries still apply.
    /// </summary>
    public PlaywrightCrawlThrottleMode ThrottleMode { get; set; } = PlaywrightCrawlThrottleMode.Automatic;

    /// <summary>
    /// Maximum navigation and selector timeout in milliseconds.
    /// </summary>
    public int NavigationTimeoutMs { get; set; } = 45_000;

    /// <summary>
    /// Load state to await during navigation.
    /// </summary>
    public WaitUntilState WaitUntil { get; set; } = WaitUntilState.NetworkIdle;

    /// <summary>
    /// Additional delay to wait after navigation completes so late-loading assets can settle.
    /// </summary>
    public int PostNavigationDelayMs { get; set; }

    /// <summary>
    /// Optional JavaScript predicate evaluated after navigation. It must return a boolean and may consume <see cref="ReadinessArgument"/>.
    /// The crawler polls it until it returns true.
    /// </summary>
    public string? ReadinessExpression { get; set; }

    /// <summary>
    /// Optional serializable argument passed to <see cref="ReadinessExpression"/>.
    /// </summary>
    public object? ReadinessArgument { get; set; }

    /// <summary>
    /// Optional application-specific readiness callback invoked after the JavaScript predicate succeeds.
    /// </summary>
    public Func<IPage, CancellationToken, ValueTask>? PageReadinessHandler { get; set; }

    /// <summary>
    /// Maximum time spent waiting for application readiness. Defaults to <see cref="NavigationTimeoutMs"/>.
    /// </summary>
    public int? ReadinessTimeoutMs { get; set; }

    /// <summary>
    /// Delay between JavaScript readiness predicate evaluations.
    /// </summary>
    public int ReadinessPollingIntervalMs { get; set; } = 100;

    /// <summary>
    /// Continues crawling when an individual page fails instead of aborting the entire crawl.
    /// </summary>
    public bool ContinueOnPageError { get; set; } = true;
}
