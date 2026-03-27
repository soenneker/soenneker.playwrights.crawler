namespace Soenneker.Playwrights.Crawler.Enums;

/// <summary>
/// Determines how much of a site should be saved.
/// </summary>

public enum PlaywrightCrawlMode
{
    /// <summary>
    /// Saves only crawled HTML documents.
    /// </summary>
    HtmlOnly = 0,

    /// <summary>
    /// Saves crawled HTML documents plus same-origin network resources observed during page loads.
    /// </summary>
    Full = 1
}
