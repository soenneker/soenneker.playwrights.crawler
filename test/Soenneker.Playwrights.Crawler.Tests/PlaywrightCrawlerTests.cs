using System.Threading.Tasks;
using Soenneker.Facts.Local;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Playwrights.Crawler.Tests;

[Collection("Collection")]
public sealed class PlaywrightCrawlerTests : FixturedUnitTest
{
    private readonly IPlaywrightCrawler _util;

    public PlaywrightCrawlerTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IPlaywrightCrawler>(true);
    }

    [Fact]
    public void Default()
    {
    }

    //[ManualFact]
    [LocalFact]
    public async ValueTask Test()
    {
        var options = new PlaywrightCrawlOptions
        {
            Url = "https://example.com", Mode = PlaywrightCrawlMode.Full,
            SaveDirectory = @"c:\example", ClearSaveDirectory = true, ContinueOnPageError = true, Headless = true, SameHostOnly = true,
            MaxDepth = 30
        };

        await _util.Crawl(options, CancellationToken);
    }
}