using Soenneker.Gen.EnumValues;

namespace Soenneker.Playwrights.Crawler.Enums;

/// <summary>
/// Determines how much of a site should be saved.
/// </summary>
[EnumValue]
public sealed partial class PlaywrightCrawlMode
{
    /// <summary>
    /// Saves only crawled HTML documents.
    /// </summary>
    public static readonly PlaywrightCrawlMode HtmlOnly = new(0);

    /// <summary>
    /// Saves crawled HTML documents plus same-origin network resources observed during page loads.
    /// </summary>
    public static readonly PlaywrightCrawlMode Full = new(1);
}
