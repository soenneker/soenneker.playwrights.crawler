using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Asyncs.Locks;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Playwright.Installation.Abstract;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Utils.Abstract;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    public PlaywrightCrawler(ILogger<PlaywrightCrawler> logger, IPlaywrightInstallationUtil playwrightInstallationUtil, IPlaywrightCrawlerStorage storage,
        IPlaywrightCrawlerBrowserUtil browserUtil, IPlaywrightCrawlerPolicyUtil policyUtil, IPlaywrightCrawlerUrlUtil urlUtil)
    {
        _logger = logger;
        _playwrightInstallationUtil = playwrightInstallationUtil;
        _storage = storage;
        _browserUtil = browserUtil;
        _policyUtil = policyUtil;
        _urlUtil = urlUtil;
    }

    public async ValueTask<PlaywrightCrawlResult> Crawl(PlaywrightCrawlOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        PlaywrightCrawlPolicy policy = options.Policy ?? new PlaywrightCrawlPolicy();
        Uri rootUri = _urlUtil.ValidateAndNormalizeRootUrl(options.Url);
        string saveDirectory = Path.GetFullPath(options.SaveDirectory);

        if (options.MaxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth cannot be negative.");

        if (options.MaxPages is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxPages must be greater than zero when specified.");

        if (options.MaxStorageBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxStorageBytes must be greater than zero when specified.");

        if (options.MaxDuration is { } maxDuration && maxDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDuration must be greater than zero when specified.");

        if (options.NavigationTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "NavigationTimeoutMs must be greater than zero.");

        if (options.PostNavigationDelayMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PostNavigationDelayMs cannot be negative.");

        _policyUtil.ValidatePolicy(policy);

        _logger.LogInformation("Starting crawl for {Url} into {SaveDirectory} (mode: {Mode}, maxDepth: {MaxDepth})", rootUri.AbsoluteUri, saveDirectory,
            options.Mode, options.MaxDepth);

        if (options.ClearSaveDirectory)
        {
            await _storage.DeleteDirectory(saveDirectory, cancellationToken)
                          .NoSync();
        }

        await _storage.CreateDirectory(saveDirectory, cancellationToken)
                      .NoSync();

        var result = new PlaywrightCrawlResult
        {
            RootUrl = rootUri.AbsoluteUri,
            SaveDirectory = saveDirectory,
            StartedAtUtc = DateTimeOffset.UtcNow,
            PagesDiscovered = 1
        };

        var stopwatch = Stopwatch.StartNew();
        var visitedPages = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var queuedPages = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        queuedPages.TryAdd(_urlUtil.NormalizePageUrl(rootUri, options.IgnoreQueryStringsInDuplicateDetection)
                                   .AbsoluteUri, 0);
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
            Count = 1
        };

        frontier.Writer.TryWrite(new CrawlTarget(rootUri, 0));

        await _playwrightInstallationUtil.EnsureInstalled(cancellationToken)
                                         .NoSync();

        using IPlaywright playwright = await Microsoft.Playwright.Playwright.CreateAsync()
                                                      .NoSync();
        await using IBrowser browser = await _browserUtil.CreateBrowser(playwright, options)
                                                         .NoSync();

        await using IBrowserContext context = await _browserUtil.CreateBrowserContext(browser, options)
                                                                .NoSync();

        await using var resultLock = new AsyncLock();

        int workerCount = _policyUtil.GetWorkerCount(options, policy);
        var workers = new Task[workerCount];

        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                await foreach (CrawlTarget target in frontier.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (_policyUtil.ShouldStop(options, result, stopwatch))
                            continue;

                        string normalizedTarget = _urlUtil.NormalizePageUrl(target.Uri, options.IgnoreQueryStringsInDuplicateDetection)
                                                          .AbsoluteUri;

                        if (!visitedPages.TryAdd(normalizedTarget, 0))
                        {
                            CrawlerDomainState duplicateDomainState = domainStates.GetOrAdd(_urlUtil.GetDomainKey(target.Uri),
                                key => new CrawlerDomainState(key, policy.PerDomainMaxConcurrency));

                            _policyUtil.RecordDuplicatePage(duplicateDomainState, policy);
                            continue;
                        }

                        using (resultLock.LockSync(cancellationToken))
                        {
                            result.PagesVisited++;
                        }

                        await CrawlPage(context, rootUri, target, options, result, queuedPages, visitedPages, savedUrls, frontier.Writer, pendingCounter,
                                stopwatch, globalSemaphore, domainStates, ipSemaphores, resultLock, cancellationToken)
                            .NoSync();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to crawl page {Url}", target.Uri.AbsoluteUri);

                        using (await resultLock.Lock(cancellationToken)
                                               .NoSync())
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
            }, cancellationToken);
        }

        await Task.WhenAll(workers)
                  .NoSync();

        stopwatch.Stop();

        result.CompletedAtUtc = DateTimeOffset.UtcNow;
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Completed crawl for {Url}. PagesVisited: {PagesVisited}, HtmlFilesSaved: {HtmlFilesSaved}, AssetFilesSaved: {AssetFilesSaved}, BytesWritten: {BytesWritten}",
            result.RootUrl, result.PagesVisited, result.HtmlFilesSaved, result.AssetFilesSaved, result.BytesWritten);

        return result;
    }

    private async Task CrawlPage(IBrowserContext context, Uri rootUri, CrawlTarget target, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> queuedPages, ConcurrentDictionary<string, byte> visitedPages, ConcurrentDictionary<string, byte> savedUrls,
        ChannelWriter<CrawlTarget> writer, PendingCounter pendingCounter, Stopwatch stopwatch, SemaphoreSlim globalSemaphore,
        ConcurrentDictionary<string, CrawlerDomainState> domainStates, ConcurrentDictionary<string, SemaphoreSlim> ipSemaphores, AsyncLock resultLock,
        CancellationToken cancellationToken)
    {
        PlaywrightCrawlPolicy policy = options.Policy ?? new PlaywrightCrawlPolicy();
        string domainKey = _urlUtil.GetDomainKey(target.Uri);
        CrawlerDomainState domainState = domainStates.GetOrAdd(domainKey, key => new CrawlerDomainState(key, policy.PerDomainMaxConcurrency));
        string ipKey = await _urlUtil.ResolveIpKey(target.Uri, cancellationToken)
                                     .NoSync();
        SemaphoreSlim ipSemaphore = ipSemaphores.GetOrAdd(ipKey, _ => new SemaphoreSlim(policy.PerIpMaxConcurrency, policy.PerIpMaxConcurrency));

        IPage page = await context.NewPageAsync()
                                  .NoSync();
        page.SetDefaultNavigationTimeout(options.NavigationTimeoutMs);
        page.SetDefaultTimeout(policy.RequestTimeoutMs);

        var responses = new List<IResponse>();
        await using var responsesLock = new AsyncLock();

        EventHandler<IResponse> responseHandler = async (_, response) =>
        {
            if (response is not null)
            {
                using (await responsesLock.Lock(cancellationToken))
                {
                    responses.Add(response);
                }
            }
        };

        page.Response += responseHandler;

        try
        {
            _logger.LogDebug("Navigating to {Url} at depth {Depth}", target.Uri.AbsoluteUri, target.Depth);

            IResponse? navigationResponse = await _policyUtil.NavigateWithPolicy(page, target.Uri, options, domainState, globalSemaphore, ipSemaphore,
                                                                 cancellationToken)
                                                             .NoSync();

            int postNavigationDelayMs = _policyUtil.GetPostNavigationDelayMs(options, policy);

            if (postNavigationDelayMs > 0)
                await page.WaitForTimeoutAsync(postNavigationDelayMs)
                          .NoSync();

            Uri finalUri = _urlUtil.ValidateAndNormalizeRootUrl(page.Url);
            visitedPages.TryAdd(_urlUtil.NormalizePageUrl(finalUri, options.IgnoreQueryStringsInDuplicateDetection)
                                        .AbsoluteUri, 0);
            string title = await page.TitleAsync()
                                     .NoSync();
            string html = await page.ContentAsync()
                                    .NoSync();

            if (_urlUtil.IsChallengePage(title, html))
            {
                _policyUtil.HandleBlockingSignal(_logger, domainState, policy, 429, "challenge/captcha page detected");
            }
            else if (navigationResponse?.Status == 403)
            {
                _policyUtil.HandleBlockingSignal(_logger, domainState, policy, 403, "403 response received");
            }

            await _storage.SaveRenderedDocument(rootUri, finalUri, html, options, result, savedUrls, resultLock, cancellationToken)
                          .NoSync();

            if (options.Mode == PlaywrightCrawlMode.Full)
            {
                List<IResponse> responseSnapshot;

                using (await responsesLock.Lock(cancellationToken)
                                          .NoSync())
                {
                    responseSnapshot = [.. responses];
                }

                IReadOnlyDictionary<string, string> externalResources = await _storage
                                                                              .SaveObservedResponses(responseSnapshot, rootUri, finalUri, options, result,
                                                                                  savedUrls, resultLock, stopwatch, cancellationToken)
                                                                              .NoSync();

                if (options.RewriteCrossOriginAssetUrls && externalResources.Count > 0)
                {
                    await _storage.RewriteExternalResourceUrlsInSavedDocument(rootUri, finalUri, html, externalResources, options, result, resultLock,
                                      cancellationToken)
                                  .NoSync();
                }
            }

            if (target.Depth < options.MaxDepth && !_policyUtil.ShouldStop(options, result, stopwatch))
            {
                IReadOnlyList<string> discoveredLinks = await _urlUtil.GetPageLinks(page)
                                                                      .NoSync();

                foreach (string link in discoveredLinks)
                {
                    if (_policyUtil.ShouldStop(options, result, stopwatch))
                        break;

                    if (!_urlUtil.TryNormalizeHttpUrl(link, out Uri? linkUri) || linkUri is null)
                        continue;

                    if (!_urlUtil.ShouldQueuePage(rootUri, linkUri, options))
                        continue;

                    string normalizedLink = _urlUtil.NormalizePageUrl(linkUri, options.IgnoreQueryStringsInDuplicateDetection)
                                                    .AbsoluteUri;

                    if (visitedPages.ContainsKey(normalizedLink) || !queuedPages.TryAdd(normalizedLink, 0))
                    {
                        _policyUtil.RecordDuplicatePage(domainState, policy);
                        continue;
                    }

                    Interlocked.Increment(ref pendingCounter.Count);

                    await writer.WriteAsync(new CrawlTarget(linkUri, target.Depth + 1), cancellationToken)
                                .NoSync();

                    using (await resultLock.Lock(cancellationToken)
                                           .NoSync())
                    {
                        result.PagesDiscovered++;
                    }
                }
            }

            _policyUtil.MarkPageCompleted(domainState);
        }
        finally
        {
            page.Response -= responseHandler;
            await page.CloseAsync()
                      .NoSync();
        }
    }
}