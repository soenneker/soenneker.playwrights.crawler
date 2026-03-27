using System;
using System.Collections.Generic;

namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// Summary of a crawl run.
/// </summary>
public sealed class PlaywrightCrawlResult
{
    /// <summary>
    /// Root URL that was requested for crawling.
    /// </summary>
    public required string RootUrl { get; set; }

    /// <summary>
    /// Absolute output directory path used for saving files.
    /// </summary>
    public required string SaveDirectory { get; set; }

    /// <summary>
    /// When the crawl started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// When the crawl completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; set; }

    /// <summary>
    /// Total crawl duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Number of pages queued for crawl, including the root page.
    /// </summary>
    public int PagesDiscovered { get; set; }

    /// <summary>
    /// Number of pages that were actually visited.
    /// </summary>
    public int PagesVisited { get; set; }

    /// <summary>
    /// Number of HTML documents saved.
    /// </summary>
    public int HtmlFilesSaved { get; set; }

    /// <summary>
    /// Number of non-HTML assets saved.
    /// </summary>
    public int AssetFilesSaved { get; set; }

    /// <summary>
    /// Total bytes written to disk.
    /// </summary>
    public long BytesWritten { get; set; }

    /// <summary>
    /// True when crawling stopped because the configured storage limit was reached.
    /// </summary>
    public bool StorageLimitReached { get; set; }

    /// <summary>
    /// True when crawling stopped because the configured duration limit was reached.
    /// </summary>
    public bool DurationLimitReached { get; set; }

    /// <summary>
    /// True when crawling stopped because the configured page limit was reached.
    /// </summary>
    public bool PageLimitReached { get; set; }

    /// <summary>
    /// Files encountered during the crawl.
    /// </summary>
    public List<PlaywrightCrawlFileResult> Files { get; set; } = [];

    /// <summary>
    /// Errors captured while crawling.
    /// </summary>
    public List<PlaywrightCrawlError> Errors { get; set; } = [];
}
