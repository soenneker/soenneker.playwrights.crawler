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
    /// Executes the navigate with policy operation.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="targetUri">The target uri.</param>
    /// <param name="options">The options.</param>
    /// <param name="domainState">The domain state.</param>
    /// <param name="globalSemaphore">The global semaphore.</param>
    /// <param name="ipSemaphore">The ip semaphore.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<IResponse?> NavigateWithPolicy(IPage page, Uri targetUri, PlaywrightCrawlOptions options, CrawlerDomainState domainState,
        SemaphoreSlim globalSemaphore, SemaphoreSlim ipSemaphore, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the ensure domain request allowed operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="throttleMode">The throttle mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask EnsureDomainRequestAllowed(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the acquire domain concurrency operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="throttleMode">The throttle mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask AcquireDomainConcurrency(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the release domain concurrency operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask ReleaseDomainConcurrency(CrawlerDomainState domainState, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the record navigation outcome operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="throttleMode">The throttle mode.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="elapsedMs">The elapsed ms.</param>
    /// <param name="success">The success.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask RecordNavigationOutcome(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode, int? statusCode,
        long elapsedMs, bool success, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the handle blocking signal operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="domainState">The domain state.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="throttleMode">The throttle mode.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="reason">The reason.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask HandleBlockingSignal(ILogger logger, CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, PlaywrightCrawlThrottleMode throttleMode,
        int statusCode, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the record duplicate page operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask RecordDuplicatePage(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the mark page completed operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask MarkPageCompleted(CrawlerDomainState domainState, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the refresh domain mode operation.
    /// </summary>
    /// <param name="domainState">The domain state.</param>
    /// <param name="now">The now.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask RefreshDomainMode(CrawlerDomainState domainState, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Gets post navigation delay ms.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The result of the operation.</returns>
    int GetPostNavigationDelayMs(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    /// <summary>
    /// Gets worker count.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The result of the operation.</returns>
    int GetWorkerCount(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy);

    /// <summary>
    /// Executes the should stop operation.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="result">The result.</param>
    /// <param name="stopwatch">The stopwatch.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool ShouldStop(PlaywrightCrawlOptions options, PlaywrightCrawlResult result, System.Diagnostics.Stopwatch stopwatch);

    /// <summary>
    /// Executes the validate policy operation.
    /// </summary>
    /// <param name="policy">The policy.</param>
    void ValidatePolicy(PlaywrightCrawlPolicy policy);
}
