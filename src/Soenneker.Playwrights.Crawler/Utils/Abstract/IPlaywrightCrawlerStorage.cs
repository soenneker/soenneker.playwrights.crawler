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

public interface IPlaywrightCrawlerStorage
{
    ValueTask DeleteDirectory(string directory, CancellationToken cancellationToken);

    ValueTask<bool> CreateDirectory(string directory, CancellationToken cancellationToken);

    Task SaveRenderedDocument(Uri rootUri, Uri documentUri, string html, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> SaveObservedResponses(IEnumerable<IResponse> responses, Uri rootUri, Uri mainDocumentUri,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken);

    Task RewriteExternalResourceUrlsInSavedDocument(Uri rootUri, Uri documentUri, string html, IReadOnlyDictionary<string, string> externalResources,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, AsyncLock resultLock, CancellationToken cancellationToken);
}
