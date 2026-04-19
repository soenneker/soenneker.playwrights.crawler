using System;
using System.Threading.Tasks;
using System.Diagnostics;
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
    private readonly IPlaywrightCrawlerUrlUtil _urlUtil;

    public PlaywrightCrawlerTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IPlaywrightCrawler>(true);
        _policyUtil = Resolve<IPlaywrightCrawlerPolicyUtil>(true);
        _urlUtil = Resolve<IPlaywrightCrawlerUrlUtil>(true);
    }

    [Fact]
    public void Default()
    {
    }

    [LocalFact]
    public void GetPostNavigationDelayMs_returns_0_when_throttling_is_disabled_and_no_explicit_delay_is_set()
    {
        var options = new PlaywrightCrawlOptions
        {
            Url = "https://example.com",
            SaveDirectory = @"C:\temp\crawler-test",
            ThrottleMode = PlaywrightCrawlThrottleMode.Disabled
        };

        int result = _policyUtil.GetPostNavigationDelayMs(options, new PlaywrightCrawlPolicy());

        Assert.Equal(0, result);
    }

    [LocalFact]
    public async Task EnsureDomainRequestAllowed_does_not_wait_when_throttling_is_disabled()
    {
        var domainState = new CrawlerDomainState("example.com", maxConcurrency: 1)
        {
            LastRequestUtc = DateTimeOffset.UtcNow,
            LastPageCompletedUtc = DateTimeOffset.UtcNow,
            Mode = CrawlerDomainMode.Cooldown,
            CooldownUntilUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        };

        var stopwatch = Stopwatch.StartNew();

        await _policyUtil.EnsureDomainRequestAllowed(domainState, new PlaywrightCrawlPolicy(), PlaywrightCrawlThrottleMode.Disabled, CancellationToken);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.True(domainState.LastRequestUtc.HasValue);
    }

    [Fact]
    public void TryNormalizeHttpUrl_rejects_unresolved_template_urls()
    {
        bool result = _urlUtil.TryNormalizeHttpUrl("https://localhost:7040/%BASE_URL%25", out Uri? uri);

        Assert.False(result);
        Assert.Null(uri);
    }

    //[ManualFact]
    [LocalFact]
    public async ValueTask Test()
    {
        var options = new PlaywrightCrawlOptions
        {
            Url = "https://localhost:7040/", Mode = PlaywrightCrawlMode.HtmlOnly, ThrottleMode = PlaywrightCrawlThrottleMode.Disabled,
            SaveDirectory = @"c:\quark", ClearSaveDirectory = true, ContinueOnPageError = true, Headless = true, SameHostOnly = true, PrettyPrintHtml = true,
            
            MaxDepth = 30
        };

        await _util.Crawl(options, CancellationToken);
    }
}
