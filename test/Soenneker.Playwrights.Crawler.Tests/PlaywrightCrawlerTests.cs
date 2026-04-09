using System.Threading.Tasks;
using Soenneker.Facts.Local;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Playwrights.Crawler.Utils.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Playwrights.Crawler.Tests;

[Collection("Collection")]
public sealed class PlaywrightCrawlerTests : FixturedUnitTest
{
    private readonly IPlaywrightCrawler _util;
    private readonly IPlaywrightCrawlerPolicyUtil _policyUtil;

    public PlaywrightCrawlerTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IPlaywrightCrawler>(true);
        _policyUtil = Resolve<IPlaywrightCrawlerPolicyUtil>(true);
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
            Url = "", Mode = PlaywrightCrawlMode.Full,
            SaveDirectory = @"c:\quark", ClearSaveDirectory = true, ContinueOnPageError = true, Headless = true, SameHostOnly = true, PrettyPrintHtml = true,
            MaxDepth = 30
        };

        await _util.Crawl(options, CancellationToken);
    }
}