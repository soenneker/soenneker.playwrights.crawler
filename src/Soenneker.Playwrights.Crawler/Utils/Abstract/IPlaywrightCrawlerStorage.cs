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
    /// <param name="directory">The directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask DeleteDirectory(string directory, CancellationToken cancellationToken);

    /// <summary>
    /// Creates directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<bool> CreateDirectory(string directory, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the save rendered document operation.
    /// </summary>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="documentUri">The document uri.</param>
    /// <param name="html">The html.</param>
    /// <param name="options">The options.</param>
    /// <param name="result">The result.</param>
    /// <param name="savedUrls">The saved urls.</param>
    /// <param name="resultLock">The result lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask SaveRenderedDocument(Uri rootUri, Uri documentUri, string html, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the save observed responses operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="responses">The responses.</param>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="mainDocumentUri">The main document uri.</param>
    /// <param name="options">The options.</param>
    /// <param name="result">The result.</param>
    /// <param name="savedUrls">The saved urls.</param>
    /// <param name="resultLock">The result lock.</param>
    /// <param name="stopwatch">The stopwatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<IReadOnlyDictionary<string, string>> SaveObservedResponses(IBrowserContext context, IEnumerable<IResponse> responses, Uri rootUri, Uri mainDocumentUri,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the save discovered resource urls operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="urls">The urls.</param>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="mainDocumentUri">The main document uri.</param>
    /// <param name="options">The options.</param>
    /// <param name="result">The result.</param>
    /// <param name="savedUrls">The saved urls.</param>
    /// <param name="resultLock">The result lock.</param>
    /// <param name="stopwatch">The stopwatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<IReadOnlyDictionary<string, string>> SaveDiscoveredResourceUrls(IBrowserContext context, IEnumerable<string> urls, Uri rootUri, Uri mainDocumentUri,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the rewrite external resource urls in saved document operation.
    /// </summary>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="documentUri">The document uri.</param>
    /// <param name="html">The html.</param>
    /// <param name="externalResources">The external resources.</param>
    /// <param name="options">The options.</param>
    /// <param name="result">The result.</param>
    /// <param name="resultLock">The result lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask RewriteExternalResourceUrlsInSavedDocument(Uri rootUri, Uri documentUri, string html, IReadOnlyDictionary<string, string> externalResources,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, AsyncLock resultLock, CancellationToken cancellationToken);
}
