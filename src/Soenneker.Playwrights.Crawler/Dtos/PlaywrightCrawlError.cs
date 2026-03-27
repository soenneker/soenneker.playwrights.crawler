using System;

namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// Represents a page-level crawl error.
/// </summary>
public sealed class PlaywrightCrawlError
{
    /// <summary>
    /// URL being processed when the error occurred.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Short error message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Captured exception when available.
    /// </summary>
    public Exception? Exception { get; set; }
}
