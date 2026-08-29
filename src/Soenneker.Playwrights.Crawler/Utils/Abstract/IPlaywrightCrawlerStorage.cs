using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Asyncs.Locks;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

/// <summary>
/// Defines the playwright crawler storage contract.
/// </summary>
public interface IPlaywrightCrawlerStorage
{
    /// <summary>
    /// Deletes directory.
    /// </summary>
    /// <param name="directory">Directory to read from or write to.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes after the targeted files have been deleted.</returns>
    ValueTask DeleteDirectory(string directory, CancellationToken cancellationToken);

    /// <summary>
    /// Creates directory.
    /// </summary>
    /// <param name="directory">Directory to read from or write to.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>true if creates directory; otherwise, false.</returns>
    ValueTask<bool> CreateDirectory(string directory, CancellationToken cancellationToken);

    /// <summary>
    /// Saves rendered Document.
    /// </summary>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="documentUri">Document URI for the save rendered document operation.</param>
    /// <param name="html">Rendered page HTML to inspect.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Storage.</param>
    /// <param name="result">Result accumulated by the operation.</param>
    /// <param name="savedUrls">Saved Urls for the save rendered document operation.</param>
    /// <param name="resultLock">Synchronization object protecting shared crawl results.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the rendered document has been saved.</returns>
    ValueTask SaveRenderedDocument(Uri rootUri, Uri documentUri, string html, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, CancellationToken cancellationToken);

    /// <summary>
    /// Saves observed Responses.
    /// </summary>
    /// <param name="context">HTTP context containing the Authorization header.</param>
    /// <param name="responses">responses returned by the upstream operation.</param>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="mainDocumentUri">Main Document URI for the save observed responses operation.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Storage.</param>
    /// <param name="result">Result accumulated by the operation.</param>
    /// <param name="savedUrls">Saved Urls for the save observed responses operation.</param>
    /// <param name="resultLock">Synchronization object protecting shared crawl results.</param>
    /// <param name="stopwatch">Stopwatch for the save observed responses operation.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the requested read Only Dictionary.</returns>
    ValueTask<IReadOnlyDictionary<string, string>> SaveObservedResponses(IBrowserContext context, IEnumerable<IResponse> responses, Uri rootUri, Uri mainDocumentUri,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken);

    /// <summary>
    /// Saves discovered Resource Urls.
    /// </summary>
    /// <param name="context">HTTP context containing the Authorization header.</param>
    /// <param name="urls">urls to process.</param>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="mainDocumentUri">Main Document URI for the save discovered resource urls operation.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Storage.</param>
    /// <param name="result">Result accumulated by the operation.</param>
    /// <param name="savedUrls">Saved Urls for the save discovered resource urls operation.</param>
    /// <param name="resultLock">Synchronization object protecting shared crawl results.</param>
    /// <param name="stopwatch">Stopwatch for the save discovered resource urls operation.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the requested read Only Dictionary.</returns>
    ValueTask<IReadOnlyDictionary<string, string>> SaveDiscoveredResourceUrls(IBrowserContext context, IEnumerable<string> urls, Uri rootUri, Uri mainDocumentUri,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites external Resource Urls In Saved Document.
    /// </summary>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="documentUri">Document URI for the rewrite external resource urls in saved document operation.</param>
    /// <param name="html">Rendered page HTML to inspect.</param>
    /// <param name="externalResources">external Resources to process.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Storage.</param>
    /// <param name="result">Result accumulated by the operation.</param>
    /// <param name="resultLock">Synchronization object protecting shared crawl results.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the rewrite external resource urls in saved document operation is complete.</returns>
    ValueTask RewriteExternalResourceUrlsInSavedDocument(Uri rootUri, Uri documentUri, string html, IReadOnlyDictionary<string, string> externalResources,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, AsyncLock resultLock, CancellationToken cancellationToken);
}
