using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

/// <summary>
/// Defines the playwright crawler url util contract.
/// </summary>
public interface IPlaywrightCrawlerUrlUtil
{
    /// <summary>
    /// Executes the validate and normalize root url operation.
    /// </summary>
    /// <param name="url">The url.</param>
    /// <returns>The result of the operation.</returns>
    Uri ValidateAndNormalizeRootUrl(string url);

    /// <summary>
    /// Attempts to execute normalize http url.
    /// </summary>
    /// <param name="url">The url.</param>
    /// <param name="uri">The uri.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool TryNormalizeHttpUrl(string url, out Uri? uri);

    /// <summary>
    /// Executes the normalize url operation.
    /// </summary>
    /// <param name="uri">The uri.</param>
    /// <returns>The result of the operation.</returns>
    Uri NormalizeUrl(Uri uri);

    /// <summary>
    /// Executes the normalize page url operation.
    /// </summary>
    /// <param name="uri">The uri.</param>
    /// <param name="ignoreQueryString">The ignore query string.</param>
    /// <returns>The result of the operation.</returns>
    Uri NormalizePageUrl(Uri uri, bool ignoreQueryString);

    /// <summary>
    /// Executes the uris share host operation.
    /// </summary>
    /// <param name="first">The first.</param>
    /// <param name="second">The second.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool UrisShareHost(Uri first, Uri second);

    /// <summary>
    /// Builds relative path.
    /// </summary>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="resourceUri">The resource uri.</param>
    /// <param name="isHtmlDocument">The is html document.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The result of the operation.</returns>
    string BuildRelativePath(Uri rootUri, Uri resourceUri, bool isHtmlDocument, string? contentType);

    /// <summary>
    /// Executes the should queue page operation.
    /// </summary>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="candidate">The candidate.</param>
    /// <param name="options">The options.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool ShouldQueuePage(Uri rootUri, Uri candidate, PlaywrightCrawlOptions options);

    /// <summary>
    /// Executes the should save resource operation.
    /// </summary>
    /// <param name="rootUri">The root uri.</param>
    /// <param name="resourceUri">The resource uri.</param>
    /// <param name="isHtmlDocument">The is html document.</param>
    /// <param name="options">The options.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool ShouldSaveResource(Uri rootUri, Uri resourceUri, bool isHtmlDocument, PlaywrightCrawlOptions options);

    /// <summary>
    /// Gets page links.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>A task containing the result of the operation.</returns>
    Task<IReadOnlyList<string>> GetPageLinks(IPage page);

    /// <summary>
    /// Gets page resource urls.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>A task containing the result of the operation.</returns>
    Task<IReadOnlyList<string>> GetPageResourceUrls(IPage page);

    /// <summary>
    /// Executes the is challenge page operation.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="html">The html.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool IsChallengePage(string? title, string html);

    /// <summary>
    /// Executes the resolve ip key operation.
    /// </summary>
    /// <param name="uri">The uri.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    Task<string> ResolveIpKey(Uri uri, CancellationToken cancellationToken);

    /// <summary>
    /// Gets domain key.
    /// </summary>
    /// <param name="uri">The uri.</param>
    /// <returns>The result of the operation.</returns>
    string GetDomainKey(Uri uri);
}
