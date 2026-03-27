using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

public interface IPlaywrightCrawlerPolicyUtil
{
    Task<IResponse?> NavigateWithPolicy(IPage page, Uri targetUri, PlaywrightCrawlOptions options, CrawlerDomainState domainState,
        SemaphoreSlim globalSemaphore, SemaphoreSlim ipSemaphore, CancellationToken cancellationToken);

    Task EnsureDomainRequestAllowed(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken);

    Task AcquireDomainConcurrency(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken);

    void ReleaseDomainConcurrency(CrawlerDomainState domainState);

    void RecordNavigationOutcome(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, int? statusCode, long elapsedMs, bool success);

    void HandleBlockingSignal(ILogger logger, CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, int statusCode, string reason);

    void RecordDuplicatePage(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy);

    void MarkPageCompleted(CrawlerDomainState domainState);

    void RefreshDomainMode(CrawlerDomainState domainState, DateTimeOffset now);

    int GetPostNavigationDelayMs(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    int GetWorkerCount(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    bool ShouldStop(PlaywrightCrawlOptions options, PlaywrightCrawlResult result, System.Diagnostics.Stopwatch stopwatch);

    void ValidatePolicy(PlaywrightCrawlPolicy policy);
}
