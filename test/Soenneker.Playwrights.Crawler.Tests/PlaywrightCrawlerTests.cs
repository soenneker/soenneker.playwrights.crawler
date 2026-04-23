using System;
using System.Threading.Tasks;
using System.Diagnostics;
using Soenneker.Tests.Attributes.Local;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Playwrights.Crawler.Utils.Abstract;
using Soenneker.Tests.HostedUnit;
using AwesomeAssertions;

namespace Soenneker.Playwrights.Crawler.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class PlaywrightCrawlerTests : HostedUnitTest
{
    private readonly IPlaywrightCrawler _util;
    private readonly IPlaywrightCrawlerPolicyUtil _policyUtil;
    private readonly IPlaywrightCrawlerUrlUtil _urlUtil;

    public PlaywrightCrawlerTests(Host host) : base(host)
    {
        _util = Resolve<IPlaywrightCrawler>(true);
        _policyUtil = Resolve<IPlaywrightCrawlerPolicyUtil>(true);
        _urlUtil = Resolve<IPlaywrightCrawlerUrlUtil>(true);
    }

    [Test]
    public void Default()
    {
    }

    [LocalOnly]
    public void GetPostNavigationDelayMs_returns_0_when_throttling_is_disabled_and_no_explicit_delay_is_set()
    {
        var options = new PlaywrightCrawlOptions
        {
            Url = "https://example.com",
            SaveDirectory = @"C:\temp\crawler-test",
            ThrottleMode = PlaywrightCrawlThrottleMode.Disabled
        };

        int result = _policyUtil.GetPostNavigationDelayMs(options, new PlaywrightCrawlPolicy());

        result.Should().Be(0);
    }

    [LocalOnly]
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

        await _policyUtil.EnsureDomainRequestAllowed(domainState, new PlaywrightCrawlPolicy(), PlaywrightCrawlThrottleMode.Disabled, System.Threading.CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(250));
        domainState.LastRequestUtc.HasValue.Should().BeTrue();
    }

    [Test]
    public void TryNormalizeHttpUrl_rejects_unresolved_template_urls()
    {
        bool result = _urlUtil.TryNormalizeHttpUrl("https://localhost:7040/%BASE_URL%25", out Uri? uri);

        result.Should().BeFalse();
        Assert.Null(uri);
    }

    //[Skip("Manual")]
    [LocalOnly]
    public async ValueTask Test()
    {
        var options = new PlaywrightCrawlOptions
        {
            Url = "https://localhost:7040/", Mode = PlaywrightCrawlMode.Full, ThrottleMode = PlaywrightCrawlThrottleMode.Disabled,
            SaveDirectory = @"c:\quark", ClearSaveDirectory = true, ContinueOnPageError = true, Headless = true, SameHostOnly = true, PrettyPrintHtml = true,
            
            MaxDepth = 30
        };

        await _util.Crawl(options, System.Threading.CancellationToken.None);
    }
}

