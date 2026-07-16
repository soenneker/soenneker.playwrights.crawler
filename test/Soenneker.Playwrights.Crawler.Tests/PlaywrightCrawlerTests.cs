using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Soenneker.Asyncs.Locks;
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
    private readonly IPlaywrightCrawlerStorage _storage;
    private readonly IPlaywrightCrawlerUrlUtil _urlUtil;

    public PlaywrightCrawlerTests(Host host) : base(host)
    {
        _util = Resolve<IPlaywrightCrawler>(true);
        _policyUtil = Resolve<IPlaywrightCrawlerPolicyUtil>(true);
        _storage = Resolve<IPlaywrightCrawlerStorage>(true);
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

        await _policyUtil.EnsureDomainRequestAllowed(domainState, new PlaywrightCrawlPolicy(),
            PlaywrightCrawlThrottleMode.Disabled, System.Threading.CancellationToken.None);
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

    [Test]
    public void ShouldQueuePage_honors_root_relative_allowlist()
    {
        Uri rootUri = _urlUtil.ValidateAndNormalizeRootUrl("https://example.com");
        var options = new PlaywrightCrawlOptions
        {
            Url = rootUri.AbsoluteUri,
            SaveDirectory = Path.GetTempPath(),
            AllowedPageUrls = ["/about", "/pricing"]
        };

        _urlUtil.ShouldQueuePage(rootUri, new Uri(rootUri, "/about"), options).Should().BeTrue();
        _urlUtil.ShouldQueuePage(rootUri, new Uri(rootUri, "/contact"), options).Should().BeFalse();
    }

    [Test]
    public void ShouldQueuePage_honors_absolute_allowlist_and_query_normalization()
    {
        Uri rootUri = _urlUtil.ValidateAndNormalizeRootUrl("https://example.com");
        var options = new PlaywrightCrawlOptions
        {
            Url = rootUri.AbsoluteUri,
            SaveDirectory = Path.GetTempPath(),
            IgnoreQueryStringsInDuplicateDetection = true,
            AllowedPageUrls = ["https://example.com/about?source=allowlist"]
        };

        _urlUtil.ShouldQueuePage(rootUri, new Uri("https://example.com/about?source=page"), options).Should().BeTrue();
        _urlUtil.ShouldQueuePage(rootUri, new Uri("https://other.example.com/about"), options).Should().BeFalse();
    }

    [Test]
    public void IsChallengePage_does_not_treat_application_captcha_components_as_a_challenge()
    {
        const string html = """
                            <html>
                            <head><title>Acceptable Use | Leadping</title></head>
                            <body><script>self.__next_f.push([1, "CaptchaProvider"]);</script></body>
                            </html>
                            """;

        _urlUtil.IsChallengePage("Acceptable Use | Leadping", html).Should().BeFalse();
    }

    [Test]
    public void IsChallengePage_does_not_treat_normal_verification_pages_as_a_challenge()
    {
        _urlUtil.IsChallengePage("Verify your email", "<html><body>Check your inbox.</body></html>").Should().BeFalse();
    }

    [Test]
    public void IsChallengePage_detects_cloudflare_challenge_runtime_markup()
    {
        const string html = """
                            <html>
                            <head><title>Just a moment...</title></head>
                            <body><script src="/cdn-cgi/challenge-platform/h/g/orchestrate/chl_page/v1"></script></body>
                            </html>
                            """;

        _urlUtil.IsChallengePage("Just a moment...", html).Should().BeTrue();
    }

    [Test]
    public void Css_text_resource_rewrites_same_origin_absolute_urls_to_root_relative()
    {
        Uri rootUri = _urlUtil.ValidateAndNormalizeRootUrl("https://firstfamilyinsurance.com");
        Uri cssUri = _urlUtil.ValidateAndNormalizeRootUrl("https://firstfamilyinsurance.com/css/site.css");
        const string css = """
                           .hero { background-image: url("https://firstfamilyinsurance.com/images/hero.jpg"); }
                           @font-face { src: url(//firstfamilyinsurance.com/fonts/site.woff2) format("woff2"); }
                           .logo { background-image: url("https://cdn.firstfamilyinsurance.com/logo.png"); }
                           """;

        var options = new PlaywrightCrawlOptions
        {
            Url = rootUri.AbsoluteUri,
            SaveDirectory = Path.GetTempPath(),
            RewriteSameOriginAbsoluteUrls = true
        };

        MethodInfo? method = _storage.GetType().GetMethod("PrepareTextResourceForSave",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var rewrittenBytes = (byte[])method!.Invoke(null, [
            rootUri, cssUri, Encoding.UTF8.GetBytes(css), false, "text/css", options
        ])!;

        string rewrittenCss = Encoding.UTF8.GetString(rewrittenBytes);

        rewrittenCss.Should().Contain("url(\"/images/hero.jpg\")");
        rewrittenCss.Should().Contain("url(/fonts/site.woff2)");
        rewrittenCss.Should().Contain("https://cdn.firstfamilyinsurance.com/logo.png");
        rewrittenCss.Should().NotContain("https://firstfamilyinsurance.com/images/hero.jpg");
        rewrittenCss.Should().NotContain("//firstfamilyinsurance.com/fonts/site.woff2");
    }

    [Test]
    public async ValueTask SaveRenderedDocument_rewrites_same_origin_absolute_urls_to_root_relative()
    {
        Uri rootUri = _urlUtil.ValidateAndNormalizeRootUrl("https://firstfamilyinsurance.com");
        string saveDirectory = Path.Combine(Path.GetTempPath(), "soenneker-playwright-crawler-tests",
            Guid.NewGuid().ToString("N"));
        const string html = """
                            <script src="https://firstfamilyinsurance.com/script.js"></script>
                            <a href="//firstfamilyinsurance.com/path?x=1#section"></a>
                            <a href="https://firstfamilyinsurance.com"></a>
                            <img src="https://cdn.firstfamilyinsurance.com/logo.png" />
                            """;

        var options = new PlaywrightCrawlOptions
        {
            Url = rootUri.AbsoluteUri,
            SaveDirectory = saveDirectory,
            RewriteSameOriginAbsoluteUrls = true
        };

        var result = new PlaywrightCrawlResult
        {
            RootUrl = rootUri.AbsoluteUri,
            SaveDirectory = saveDirectory,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        await using var resultLock = new AsyncLock();

        try
        {
            await _storage.CreateDirectory(saveDirectory, CancellationToken.None);
            await _storage.SaveRenderedDocument(rootUri, rootUri, html, options, result,
                new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase), resultLock,
                CancellationToken.None);

            string savedHtml = await File.ReadAllTextAsync(Path.Combine(saveDirectory, "index.html"));

            savedHtml.Should().Contain("src=\"/script.js\"");
            savedHtml.Should().Contain("href=\"/path?x=1#section\"");
            savedHtml.Should().Contain("href=\"/\"");
            savedHtml.Should().Contain("https://cdn.firstfamilyinsurance.com/logo.png");
            savedHtml.Should().NotContain("https://firstfamilyinsurance.com/script.js");
            savedHtml.Should().NotContain("//firstfamilyinsurance.com/path");
        }
        finally
        {
            if (Directory.Exists(saveDirectory))
                Directory.Delete(saveDirectory, true);
        }
    }

    //[Skip("Manual")]
     [LocalOnly]
    [Test]
    public async ValueTask Test()
    {
        var options = new PlaywrightCrawlOptions
        {
            Url = "", Mode = PlaywrightCrawlMode.Full,
            ThrottleMode = PlaywrightCrawlThrottleMode.Disabled,
            RewriteSameOriginAbsoluteUrls = true,
            TriggerLazyLoading = true,
            SaveDirectory = @"c:\", ClearSaveDirectory = true, ContinueOnPageError = true, Headless = true,
            SameHostOnly = true, PrettyPrintHtml = true,

            MaxDepth = 30
        };

        await _util.Crawl(options);
    }
}
