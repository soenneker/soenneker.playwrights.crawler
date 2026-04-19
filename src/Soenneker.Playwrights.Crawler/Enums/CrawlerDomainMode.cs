using Soenneker.Gen.EnumValues;

namespace Soenneker.Playwrights.Crawler.Enums;

[EnumValue]
public sealed partial class CrawlerDomainMode
{
    public static readonly CrawlerDomainMode Normal = new(0);

    public static readonly CrawlerDomainMode Slow = new(1);

    public static readonly CrawlerDomainMode Cooldown = new(2);
}
