using System;
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
    public required string Url { get; set; }

    public string Channel { get; set; } = "chromium";

    /// <summary>
    /// Directory where the crawled output should be written.
    /// </summary>
    public required string SaveDirectory { get; set; }

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
    /// When true, only pages on the same host as <see cref="Url"/> are queued for crawling.
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
    /// Continues crawling when an individual page fails instead of aborting the entire crawl.
    /// </summary>
    public bool ContinueOnPageError { get; set; } = true;
}
