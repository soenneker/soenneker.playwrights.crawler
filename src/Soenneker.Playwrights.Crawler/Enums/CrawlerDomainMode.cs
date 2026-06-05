using Soenneker.Gen.EnumValues;

namespace Soenneker.Playwrights.Crawler.Enums;

/// <summary>
/// Represents the crawler domain mode.
/// </summary>
[EnumValue]
public sealed partial class CrawlerDomainMode
{
    /// <summary>
    /// The normal.
    /// </summary>
    public static readonly CrawlerDomainMode Normal = new(0);

    /// <summary>
    /// The slow.
    /// </summary>
    public static readonly CrawlerDomainMode Slow = new(1);

    /// <summary>
    /// The cooldown.
    /// </summary>
    public static readonly CrawlerDomainMode Cooldown = new(2);
}
