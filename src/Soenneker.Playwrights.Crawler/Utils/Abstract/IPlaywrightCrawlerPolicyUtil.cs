using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

public interface IPlaywrightCrawlerPolicyUtil
{
    ValueTask<IResponse?> NavigateWithPolicy(IPage page, Uri targetUri, PlaywrightCrawlOptions options, CrawlerDomainState domainState,
        SemaphoreSlim globalSemaphore, SemaphoreSlim ipSemaphore, CancellationToken cancellationToken);

    ValueTask EnsureDomainRequestAllowed(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        CancellationToken cancellationToken);

    ValueTask AcquireDomainConcurrency(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        CancellationToken cancellationToken);

    ValueTask ReleaseDomainConcurrency(CrawlerDomainState domainState, CancellationToken cancellationToken);

    ValueTask RecordNavigationOutcome(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode, int? statusCode,
        long elapsedMs, bool success, CancellationToken cancellationToken);

    ValueTask HandleBlockingSignal(ILogger logger, CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        int statusCode, string reason, CancellationToken cancellationToken);

    ValueTask RecordDuplicatePage(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken);

    ValueTask MarkPageCompleted(CrawlerDomainState domainState, CancellationToken cancellationToken);

    ValueTask RefreshDomainMode(CrawlerDomainState domainState, DateTimeOffset now, CancellationToken cancellationToken);

    int GetPostNavigationDelayMs(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    int GetWorkerCount(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    bool ShouldStop(PlaywrightCrawlOptions options, PlaywrightCrawlResult result, System.Diagnostics.Stopwatch stopwatch);

    void ValidatePolicy(PlaywrightCrawlPolicy policy);
}
