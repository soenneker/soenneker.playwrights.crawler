using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

/// <summary>
/// Defines the playwright crawler policy util contract.
/// </summary>
public interface IPlaywrightCrawlerPolicyUtil
{
    /// <summary>
    /// Navigates with Policy.
    /// </summary>
    /// <param name="page">Browser page to inspect or control.</param>
    /// <param name="targetUri">Target URI for the navigate with policy operation.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Policy.</param>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="globalSemaphore">Global Semaphore for the navigate with policy operation.</param>
    /// <param name="ipSemaphore">Ip Semaphore for the navigate with policy operation.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the requested response.</returns>
    ValueTask<IResponse?> NavigateWithPolicy(IPage page, Uri targetUri, PlaywrightCrawlOptions options, CrawlerDomainState domainState,
        SemaphoreSlim globalSemaphore, SemaphoreSlim ipSemaphore, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures domain Request Allowed.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <param name="throttleMode">Throttling policy applied to requests for the domain.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the ensure domain request allowed operation is complete.</returns>
    ValueTask EnsureDomainRequestAllowed(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Acquires domain Concurrency.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <param name="throttleMode">Throttling policy applied to requests for the domain.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the acquire domain concurrency operation is complete.</returns>
    ValueTask AcquireDomainConcurrency(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases domain Concurrency.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the release domain concurrency operation is complete.</returns>
    ValueTask ReleaseDomainConcurrency(CrawlerDomainState domainState, CancellationToken cancellationToken);

    /// <summary>
    /// Records navigation Outcome.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <param name="throttleMode">Throttling policy applied to requests for the domain.</param>
    /// <param name="statusCode">HTTP status code associated with the result.</param>
    /// <param name="elapsedMs">Elapsed Ms for the record navigation outcome operation.</param>
    /// <param name="success">Whether success.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the record navigation outcome operation is complete.</returns>
    ValueTask RecordNavigationOutcome(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode, int? statusCode,
        long elapsedMs, bool success, CancellationToken cancellationToken);

    /// <summary>
    /// Handles the blocking signal callback.
    /// </summary>
    /// <param name="logger">Logger for the handle blocking signal operation.</param>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <param name="throttleMode">Throttling policy applied to requests for the domain.</param>
    /// <param name="statusCode">HTTP status code associated with the result.</param>
    /// <param name="reason">Reason for the handle blocking signal operation.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the handle blocking signal operation is complete.</returns>
    ValueTask HandleBlockingSignal(ILogger logger, CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        int statusCode, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Records duplicate Page.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the record duplicate page operation is complete.</returns>
    ValueTask RecordDuplicatePage(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken);

    /// <summary>
    /// Marks page Completed.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the mark page completed operation is complete.</returns>
    ValueTask MarkPageCompleted(CrawlerDomainState domainState, CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes domain Mode.
    /// </summary>
    /// <param name="domainState">Per-domain crawl state used for throttling and concurrency decisions.</param>
    /// <param name="now">Now for the refresh domain mode operation.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the refresh domain mode operation is complete.</returns>
    ValueTask RefreshDomainMode(CrawlerDomainState domainState, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Gets post navigation delay ms.
    /// </summary>
    /// <param name="options">Options to configure for the Playwright Crawler Policy.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <returns>The requested value.</returns>
    int GetPostNavigationDelayMs(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    /// <summary>
    /// Gets worker count.
    /// </summary>
    /// <param name="options">Options to configure for the Playwright Crawler Policy.</param>
    /// <param name="policy">Policy that controls the operation.</param>
    /// <returns>The requested value.</returns>
    int GetWorkerCount(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    /// <summary>
    /// Determines whether the crawl should stop because a configured limit has been reached.
    /// </summary>
    /// <param name="options">Options to configure for the Playwright Crawler Policy.</param>
    /// <param name="result">Result accumulated by the operation.</param>
    /// <param name="stopwatch">Stopwatch for the should stop operation.</param>
    /// <returns>true when crawling should stop; otherwise, false.</returns>
    bool ShouldStop(PlaywrightCrawlOptions options, PlaywrightCrawlResult result, System.Diagnostics.Stopwatch stopwatch);

    /// <summary>
    /// Validates policy for the Playwright Crawler Policy.
    /// </summary>
    /// <param name="policy">Policy that controls the operation.</param>
    void ValidatePolicy(PlaywrightCrawlPolicy policy);
}
