using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Asyncs.Locks;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Playwrights.Installation.Abstract;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Utils.Abstract;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;

namespace Soenneker.Playwrights.Crawler;

/// <inheritdoc cref="IPlaywrightCrawler"/>
public sealed class PlaywrightCrawler : IPlaywrightCrawler
{
    private readonly ILogger<PlaywrightCrawler> _logger;
    private readonly IPlaywrightInstallationUtil _playwrightInstallationUtil;
    private readonly IPlaywrightCrawlerStorage _storage;
    private readonly IPlaywrightCrawlerBrowserUtil _browserUtil;
    private readonly IPlaywrightCrawlerPolicyUtil _policyUtil;
    private readonly IPlaywrightCrawlerUrlUtil _urlUtil;

    public PlaywrightCrawler(ILogger<PlaywrightCrawler> logger, IPlaywrightInstallationUtil playwrightInstallationUtil,
        IPlaywrightCrawlerStorage storage, IPlaywrightCrawlerBrowserUtil browserUtil,
        IPlaywrightCrawlerPolicyUtil policyUtil, IPlaywrightCrawlerUrlUtil urlUtil)
    {
        _logger = logger;
        _playwrightInstallationUtil = playwrightInstallationUtil;
        _storage = storage;
        _browserUtil = browserUtil;
        _policyUtil = policyUtil;
        _urlUtil = urlUtil;
    }

    public async ValueTask<PlaywrightCrawlResult> Crawl(PlaywrightCrawlOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        PlaywrightCrawlPolicy policy = options.Policy ?? new PlaywrightCrawlPolicy();
        IReadOnlyList<Uri> startingUris = ResolveStartingUris(options);
        Uri rootUri = startingUris[0];
        string? saveDirectory = null;

        if (options.SameHostOnly && startingUris.Any(uri => !_urlUtil.UrisShareHost(rootUri, uri)))
            throw new ArgumentException("All starting URLs must share a host when SameHostOnly is true.", nameof(options));

        if (options.SaveToDisk)
        {
            if (string.IsNullOrWhiteSpace(options.SaveDirectory))
                throw new ArgumentException("SaveDirectory is required when SaveToDisk is true.", nameof(options));

            saveDirectory = Path.GetFullPath(options.SaveDirectory);
        }

        if (!options.SaveToDisk && options.Mode == PlaywrightCrawlMode.Full)
            throw new ArgumentException("Full resource capture requires SaveToDisk to be true.", nameof(options));

        if (options.MaxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth cannot be negative.");

        if (options.MaxPages is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options),
                "MaxPages must be greater than zero when specified.");

        if (options.MaxStorageBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options),
                "MaxStorageBytes must be greater than zero when specified.");

        if (options.MaxDuration is { } maxDuration && maxDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options),
                "MaxDuration must be greater than zero when specified.");

        if (options.NavigationTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "NavigationTimeoutMs must be greater than zero.");

        if (options.PostNavigationDelayMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PostNavigationDelayMs cannot be negative.");

        if (options.ReadinessTimeoutMs is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ReadinessTimeoutMs must be greater than zero when specified.");

        if (options.ReadinessPollingIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ReadinessPollingIntervalMs must be greater than zero.");

        if (options.LazyLoadScrollStepPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "LazyLoadScrollStepPx must be greater than zero.");

        if (options.LazyLoadScrollDelayMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "LazyLoadScrollDelayMs cannot be negative.");

        if (options.LazyLoadMaxScrolls < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "LazyLoadMaxScrolls cannot be negative.");

        _policyUtil.ValidatePolicy(policy);

        _logger.LogInformation("Starting crawl for {UrlCount} starting URL(s) into {SaveDirectory} (mode: {Mode}, maxDepth: {MaxDepth})",
            startingUris.Count, saveDirectory, options.Mode, options.MaxDepth);

        if (options.SaveToDisk && options.ClearSaveDirectory)
        {
            await _storage.DeleteDirectory(saveDirectory!, cancellationToken).NoSync();
        }

        if (options.SaveToDisk)
            await _storage.CreateDirectory(saveDirectory!, cancellationToken).NoSync();

        var result = new PlaywrightCrawlResult
        {
            RootUrl = rootUri.AbsoluteUri,
            SaveDirectory = saveDirectory ?? string.Empty,
            StartedAtUtc = DateTimeOffset.UtcNow,
            PagesDiscovered = startingUris.Count
        };

        var stopwatch = Stopwatch.StartNew();
        var visitedPages = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var queuedPages = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (Uri startingUri in startingUris)
        {
            queuedPages.TryAdd(
                _urlUtil.NormalizePageUrl(startingUri, options.IgnoreQueryStringsInDuplicateDetection).AbsoluteUri, 0);
        }

        var savedUrls = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var globalSemaphore = new SemaphoreSlim(policy.GlobalMaxConcurrency, policy.GlobalMaxConcurrency);
        var domainStates = new ConcurrentDictionary<string, CrawlerDomainState>(StringComparer.OrdinalIgnoreCase);
        var ipSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        var frontier = Channel.CreateUnbounded<CrawlTarget>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var pendingCounter = new PendingCounter
        {
            Count = startingUris.Count
        };

        foreach (Uri startingUri in startingUris)
        {
            frontier.Writer.TryWrite(new CrawlTarget(startingUri, 0));
        }

        await _playwrightInstallationUtil.EnsureInstalled(cancellationToken).NoSync();

        using IPlaywright playwright = await Playwright.CreateAsync().NoSync();

        await using IBrowser browser = await _browserUtil.CreateBrowser(playwright, options).NoSync();

        await using IBrowserContext context = await _browserUtil.CreateBrowserContext(browser, options).NoSync();

        await using var resultLock = new AsyncLock();

        int workerCount = _policyUtil.GetWorkerCount(options, policy);
        var workers = new Task[workerCount];

        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = RunCrawlerWorker(context, rootUri, options, policy, result, queuedPages, visitedPages,
                savedUrls, frontier, pendingCounter, stopwatch, globalSemaphore, domainStates, ipSemaphores, resultLock,
                cancellationToken);
        }

        await Task.WhenAll(workers).NoSync();

        stopwatch.Stop();

        result.CompletedAtUtc = DateTimeOffset.UtcNow;
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Completed crawl for {Url}. PagesVisited: {PagesVisited}, HtmlFilesSaved: {HtmlFilesSaved}, AssetFilesSaved: {AssetFilesSaved}, BytesWritten: {BytesWritten}",
            result.RootUrl, result.PagesVisited, result.HtmlFilesSaved, result.AssetFilesSaved, result.BytesWritten);

        return result;
    }

    private async Task RunCrawlerWorker(IBrowserContext context, Uri rootUri, PlaywrightCrawlOptions options,
        PlaywrightCrawlPolicy policy, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> queuedPages,
        ConcurrentDictionary<string, byte> visitedPages, ConcurrentDictionary<string, byte> savedUrls,
        Channel<CrawlTarget> frontier, PendingCounter pendingCounter, Stopwatch stopwatch,
        SemaphoreSlim globalSemaphore, ConcurrentDictionary<string, CrawlerDomainState> domainStates,
        ConcurrentDictionary<string, SemaphoreSlim> ipSemaphores, AsyncLock resultLock,
        CancellationToken cancellationToken)
    {
        await foreach (CrawlTarget target in frontier.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_policyUtil.ShouldStop(options, result, stopwatch))
                    continue;

                string normalizedTarget = _urlUtil
                                          .NormalizePageUrl(target.Uri, options.IgnoreQueryStringsInDuplicateDetection)
                                          .AbsoluteUri;

                // Once a worker owns it, it is no longer merely queued.
                queuedPages.TryRemove(normalizedTarget, out _);

                if (!visitedPages.TryAdd(normalizedTarget, 0))
                {
                    CrawlerDomainState duplicateDomainState = domainStates.GetOrAdd(_urlUtil.GetDomainKey(target.Uri),
                        key => new CrawlerDomainState(key, policy.PerDomainMaxConcurrency));

                    await _policyUtil.RecordDuplicatePage(duplicateDomainState, policy, cancellationToken).NoSync();
                    continue;
                }

                using (await resultLock.Lock(cancellationToken).NoSync())
                {
                    result.PagesVisited++;
                }

                await CrawlPage(context, rootUri, target, options, result, queuedPages, visitedPages, savedUrls,
                    frontier.Writer, pendingCounter, stopwatch, globalSemaphore, domainStates, ipSemaphores, resultLock,
                    cancellationToken).NoSync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to crawl page {Url}", target.Uri.AbsoluteUri);

                using (await resultLock.Lock(cancellationToken).NoSync())
                {
                    result.Errors.Add(new PlaywrightCrawlError
                    {
                        Url = target.Uri.AbsoluteUri,
                        Message = ex.Message,
                        Exception = ex
                    });
                }

                if (!options.ContinueOnPageError)
                {
                    frontier.Writer.TryComplete(ex);
                    throw;
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref pendingCounter.Count) == 0)
                    frontier.Writer.TryComplete();
            }
        }
    }

    private async Task CrawlPage(IBrowserContext context, Uri rootUri, CrawlTarget target,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> queuedPages,
        ConcurrentDictionary<string, byte> visitedPages, ConcurrentDictionary<string, byte> savedUrls,
        ChannelWriter<CrawlTarget> writer, PendingCounter pendingCounter, Stopwatch stopwatch,
        SemaphoreSlim globalSemaphore, ConcurrentDictionary<string, CrawlerDomainState> domainStates,
        ConcurrentDictionary<string, SemaphoreSlim> ipSemaphores, AsyncLock resultLock,
        CancellationToken cancellationToken)
    {
        PlaywrightCrawlPolicy policy = options.Policy ?? new PlaywrightCrawlPolicy();
        string domainKey = _urlUtil.GetDomainKey(target.Uri);
        CrawlerDomainState domainState = domainStates.GetOrAdd(domainKey,
            key => new CrawlerDomainState(key, policy.PerDomainMaxConcurrency));

        string ipKey = await _urlUtil.ResolveIpKey(target.Uri, cancellationToken).NoSync();

        SemaphoreSlim ipSemaphore = ipSemaphores.GetOrAdd(ipKey,
            _ => new SemaphoreSlim(policy.PerIpMaxConcurrency, policy.PerIpMaxConcurrency));

        IPage page = await context.NewPageAsync().NoSync();

        page.SetDefaultNavigationTimeout(options.NavigationTimeoutMs);
        page.SetDefaultTimeout(policy.RequestTimeoutMs);

        var responses = new List<IResponse>(128);
        var responsesGate = new object();

        void ResponseHandler(object? _, IResponse response)
        {
            if (response is null)
                return;

            lock (responsesGate)
            {
                responses.Add(response);
            }
        }

        page.Response += ResponseHandler;

        try
        {
            _logger.LogDebug("Navigating to {Url} at depth {Depth}", target.Uri.AbsoluteUri, target.Depth);

            IResponse? navigationResponse = await _policyUtil.NavigateWithPolicy(page, target.Uri, options, domainState,
                globalSemaphore, ipSemaphore, cancellationToken).NoSync();

            await WaitForPageReadiness(page, options, cancellationToken).NoSync();

            int postNavigationDelayMs = _policyUtil.GetPostNavigationDelayMs(options, policy);

            if (postNavigationDelayMs > 0)
                await page.WaitForTimeoutAsync(postNavigationDelayMs).NoSync();

            if (options.Mode == PlaywrightCrawlMode.Full && options.TriggerLazyLoading)
                await TriggerLazyLoadedResources(page, options, cancellationToken).NoSync();

            Uri finalUri = _urlUtil.ValidateAndNormalizeRootUrl(page.Url);

            visitedPages.TryAdd(
                _urlUtil.NormalizePageUrl(finalUri, options.IgnoreQueryStringsInDuplicateDetection).AbsoluteUri, 0);

            string title = await page.TitleAsync().NoSync();

            string html = await page.ContentAsync().NoSync();

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.Pages.Add(new PlaywrightCrawlPageResult
                {
                    RequestedUrl = target.Uri.AbsoluteUri,
                    FinalUrl = finalUri.AbsoluteUri,
                    StatusCode = navigationResponse?.Status,
                    Title = title,
                    Html = options.CaptureRenderedHtml || !options.SaveToDisk ? html : null
                });
            }

            if (_urlUtil.IsChallengePage(title, html))
            {
                await _policyUtil.HandleBlockingSignal(_logger, domainState, policy, options.ThrottleMode, 429,
                    "challenge/captcha page detected", cancellationToken).NoSync();
            }
            else if (navigationResponse?.Status == 403)
            {
                await _policyUtil.HandleBlockingSignal(_logger, domainState, policy, options.ThrottleMode, 403,
                    "403 response received", cancellationToken).NoSync();
            }

            if (options.SaveToDisk)
            {
                await _storage.SaveRenderedDocument(rootUri, finalUri, html, options, result, savedUrls, resultLock,
                    cancellationToken).NoSync();
            }

            if (options.SaveToDisk && options.Mode == PlaywrightCrawlMode.Full)
            {
                List<IResponse> responseSnapshot;

                lock (responsesGate)
                {
                    responseSnapshot = [.. responses];
                }

                IReadOnlyList<string> discoveredResourceUrls = await _urlUtil.GetPageResourceUrls(page).NoSync();

                IReadOnlyDictionary<string, string> externalResources = await _storage
                                                                               .SaveObservedResponses(context, responseSnapshot,
                                                                                   rootUri, finalUri, options, result,
                                                                                   savedUrls, resultLock, stopwatch,
                                                                                   cancellationToken).NoSync();

                IReadOnlyDictionary<string, string> discoveredExternalResources = await _storage
                                                                                       .SaveDiscoveredResourceUrls(context,
                                                                                           discoveredResourceUrls, rootUri,
                                                                                           finalUri, options, result,
                                                                                           savedUrls, resultLock, stopwatch,
                                                                                           cancellationToken).NoSync();

                if (options.RewriteCrossOriginAssetUrls &&
                    (externalResources.Count > 0 || discoveredExternalResources.Count > 0))
                {
                    var resourcesToRewrite = new Dictionary<string, string>(externalResources, StringComparer.OrdinalIgnoreCase);

                    foreach ((string url, string relativePath) in discoveredExternalResources)
                    {
                        resourcesToRewrite.TryAdd(url, relativePath);
                    }

                    await _storage.RewriteExternalResourceUrlsInSavedDocument(rootUri, finalUri, html,
                        resourcesToRewrite, options, result, resultLock, cancellationToken).NoSync();
                }
            }

            if (options.DiscoverLinks && target.Depth < options.MaxDepth && !_policyUtil.ShouldStop(options, result, stopwatch))
            {
                IReadOnlyList<string> discoveredLinks = await _urlUtil.GetPageLinks(page).NoSync();

                foreach (string link in discoveredLinks)
                {
                    if (_policyUtil.ShouldStop(options, result, stopwatch))
                        break;

                    if (!_urlUtil.TryNormalizeHttpUrl(link, out Uri? linkUri) || linkUri is null)
                        continue;

                    if (!_urlUtil.ShouldQueuePage(rootUri, linkUri, options))
                        continue;

                    string normalizedLink = _urlUtil
                                            .NormalizePageUrl(linkUri, options.IgnoreQueryStringsInDuplicateDetection)
                                            .AbsoluteUri;

                    if (visitedPages.ContainsKey(normalizedLink) || !queuedPages.TryAdd(normalizedLink, 0))
                    {
                        await _policyUtil.RecordDuplicatePage(domainState, policy, cancellationToken).NoSync();
                        continue;
                    }

                    var crawlTarget = new CrawlTarget(linkUri, target.Depth + 1);

                    Interlocked.Increment(ref pendingCounter.Count);

                    try
                    {
                        await writer.WriteAsync(crawlTarget, cancellationToken).NoSync();
                    }
                    catch
                    {
                        Interlocked.Decrement(ref pendingCounter.Count);
                        queuedPages.TryRemove(normalizedLink, out _);
                        throw;
                    }

                    using (await resultLock.Lock(cancellationToken).NoSync())
                    {
                        result.PagesDiscovered++;
                    }
                }
            }

            await _policyUtil.MarkPageCompleted(domainState, cancellationToken).NoSync();
        }
        finally
        {
            page.Response -= ResponseHandler;

            await page.CloseAsync().NoSync();
        }
    }

    private IReadOnlyList<Uri> ResolveStartingUris(PlaywrightCrawlOptions options)
    {
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Url))
            urls.Add(options.Url);

        if (options.StartingUrls is { Count: > 0 })
            urls.AddRange(options.StartingUrls);

        if (urls.Count == 0)
            throw new ArgumentException("Url or at least one StartingUrls entry is required.", nameof(options));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Uri>(urls.Count);

        foreach (string url in urls)
        {
            Uri uri = _urlUtil.ValidateAndNormalizeRootUrl(url);
            string normalized = _urlUtil.NormalizePageUrl(uri, options.IgnoreQueryStringsInDuplicateDetection).AbsoluteUri;

            if (seen.Add(normalized))
                result.Add(uri);
        }

        return result;
    }

    private static async ValueTask WaitForPageReadiness(IPage page, PlaywrightCrawlOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ReadinessExpression) && options.PageReadinessHandler is null)
            return;

        int timeoutMs = options.ReadinessTimeoutMs ?? options.NavigationTimeoutMs;
        var stopwatch = Stopwatch.StartNew();

        if (!string.IsNullOrWhiteSpace(options.ReadinessExpression))
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int remainingMs = timeoutMs - (int)stopwatch.ElapsedMilliseconds;

                if (remainingMs <= 0)
                    throw new TimeoutException($"Page readiness predicate did not complete within {timeoutMs}ms for {page.Url}.");

                bool ready = await page.EvaluateAsync<bool>(options.ReadinessExpression, options.ReadinessArgument)
                                       .WaitAsync(TimeSpan.FromMilliseconds(remainingMs), cancellationToken);

                if (ready)
                    break;

                await Task.Delay(Math.Min(options.ReadinessPollingIntervalMs, remainingMs), cancellationToken).NoSync();
            }
        }

        if (options.PageReadinessHandler is null)
            return;

        int handlerRemainingMs = timeoutMs - (int)stopwatch.ElapsedMilliseconds;

        if (handlerRemainingMs <= 0)
            throw new TimeoutException($"Page readiness handler did not complete within {timeoutMs}ms for {page.Url}.");

        await options.PageReadinessHandler(page, cancellationToken)
                     .AsTask()
                     .WaitAsync(TimeSpan.FromMilliseconds(handlerRemainingMs), cancellationToken)
                     .NoSync();
    }

    private async ValueTask TriggerLazyLoadedResources(IPage page, PlaywrightCrawlOptions options,
        CancellationToken cancellationToken)
    {
        if (options.LazyLoadMaxScrolls == 0)
            return;

        try
        {
            await page.EvaluateAsync("""
                                     () => {
                                         const srcAttributes = ['data-src', 'data-original', 'data-lazy-src', 'data-url', 'data-hires'];
                                         const srcSetAttributes = ['data-srcset', 'data-lazy-srcset'];
                                         const backgroundAttributes = ['data-bg', 'data-background', 'data-background-image', 'data-bg-src'];

                                         for (const image of document.querySelectorAll('img')) {
                                             image.loading = 'eager';

                                             if (!image.getAttribute('src')) {
                                                 for (const attribute of srcAttributes) {
                                                     const value = image.getAttribute(attribute);

                                                     if (value) {
                                                         image.setAttribute('src', value);
                                                         break;
                                                     }
                                                 }
                                             }

                                             if (!image.getAttribute('srcset')) {
                                                 for (const attribute of srcSetAttributes) {
                                                     const value = image.getAttribute(attribute);

                                                     if (value) {
                                                         image.setAttribute('srcset', value);
                                                         break;
                                                     }
                                                 }
                                             }
                                         }

                                         for (const source of document.querySelectorAll('source')) {
                                             if (!source.getAttribute('srcset')) {
                                                 for (const attribute of srcSetAttributes) {
                                                     const value = source.getAttribute(attribute);

                                                     if (value) {
                                                         source.setAttribute('srcset', value);
                                                         break;
                                                     }
                                                 }
                                             }
                                         }

                                         const backgroundSelector = backgroundAttributes
                                             .map(attribute => `[${attribute}]`)
                                             .join(',');

                                         for (const element of document.querySelectorAll(backgroundSelector)) {
                                             if (element.style.backgroundImage && element.style.backgroundImage !== 'none') {
                                                 continue;
                                             }

                                             for (const attribute of backgroundAttributes) {
                                                 const value = element.getAttribute(attribute);

                                                 if (value) {
                                                     element.style.backgroundImage = `url("${value.replaceAll('"', '\\"')}")`;
                                                     break;
                                                 }
                                             }
                                         }
                                     }
                                     """).NoSync();

            double previousScrollHeight = -1;

            for (var i = 0; i < options.LazyLoadMaxScrolls; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double[] state = await page.EvaluateAsync<double[]>(
                    """
                    step => {
                        const scrollingElement = document.scrollingElement || document.documentElement || document.body;
                        window.scrollBy(0, step);

                        return [
                            window.scrollY || scrollingElement.scrollTop || 0,
                            window.innerHeight || 0,
                            scrollingElement.scrollHeight || 0
                        ];
                    }
                    """, options.LazyLoadScrollStepPx).NoSync();

                if (options.LazyLoadScrollDelayMs > 0)
                    await page.WaitForTimeoutAsync(options.LazyLoadScrollDelayMs).NoSync();

                double scrollY = state.Length > 0 ? state[0] : 0;
                double viewportHeight = state.Length > 1 ? state[1] : 0;
                double scrollHeight = state.Length > 2 ? state[2] : 0;

                if (viewportHeight <= 0 || scrollHeight <= 0)
                    break;

                if (scrollY + viewportHeight >= scrollHeight - 2 && Math.Abs(scrollHeight - previousScrollHeight) < 1)
                    break;

                previousScrollHeight = scrollHeight;
            }

            await page.EvaluateAsync("() => window.scrollTo(0, 0)").NoSync();

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                {
                    Timeout = Math.Min(options.NavigationTimeoutMs, 2_000)
                }).NoSync();
            }
            catch (TimeoutException)
            {
                // Some pages keep background requests open; already observed responses are still saved.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to trigger lazy-loaded resources for {Url}", page.Url);
        }
    }
}
