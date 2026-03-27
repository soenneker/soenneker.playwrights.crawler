using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

public interface IPlaywrightCrawlerUrlUtil
{
    Uri ValidateAndNormalizeRootUrl(string url);

    bool TryNormalizeHttpUrl(string url, out Uri? uri);

    Uri NormalizeUrl(Uri uri);

    Uri NormalizePageUrl(Uri uri, bool ignoreQueryString);

    bool UrisShareHost(Uri first, Uri second);

    string BuildRelativePath(Uri rootUri, Uri resourceUri, bool isHtmlDocument, string? contentType);

    bool ShouldQueuePage(Uri rootUri, Uri candidate, PlaywrightCrawlOptions options);

    bool ShouldSaveResource(Uri rootUri, Uri resourceUri, bool isHtmlDocument, PlaywrightCrawlOptions options);

    Task<IReadOnlyList<string>> GetPageLinks(IPage page);

    bool IsChallengePage(string? title, string html);

    Task<string> ResolveIpKey(Uri uri, CancellationToken cancellationToken);

    string GetDomainKey(Uri uri);
}
