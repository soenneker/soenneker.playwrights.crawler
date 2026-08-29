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
    /// Validates and Normalize Root URL for the Playwright Crawler URL.
    /// </summary>
    /// <param name="url">URL of the resource to target.</param>
    /// <returns>The resulting URI.</returns>
    Uri ValidateAndNormalizeRootUrl(string url);

    /// <summary>
    /// Attempts to normalize an HTTP or HTTPS URL into an absolute URI.
    /// </summary>
    /// <param name="url">URL of the resource to target.</param>
    /// <param name="uri">Receives the normalized absolute URI when parsing succeeds.</param>
    /// <returns>true if the URL was valid and the normalized URI was assigned; otherwise, false.</returns>
    bool TryNormalizeHttpUrl(string url, out Uri? uri);

    /// <summary>
    /// Normalizes URL.
    /// </summary>
    /// <param name="uri">Receives the normalized absolute URI when parsing succeeds.</param>
    /// <returns>The resulting URI.</returns>
    Uri NormalizeUrl(Uri uri);

    /// <summary>
    /// Normalizes page URL.
    /// </summary>
    /// <param name="uri">Receives the normalized absolute URI when parsing succeeds.</param>
    /// <param name="ignoreQueryString">Whether ignore query string.</param>
    /// <returns>The resulting URI.</returns>
    Uri NormalizePageUrl(Uri uri, bool ignoreQueryString);

    /// <summary>
    /// Determines whether two URIs identify the same host.
    /// </summary>
    /// <param name="first">First URI to compare.</param>
    /// <param name="second">Second URI to compare.</param>
    /// <returns>true if both URIs have the same host; otherwise, false.</returns>
    bool UrisShareHost(Uri first, Uri second);

    /// <summary>
    /// Builds relative path.
    /// </summary>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="resourceUri">Discovered resource URI to evaluate.</param>
    /// <param name="isHtmlDocument">Whether the resource is the rendered HTML document.</param>
    /// <param name="contentType">Media type describing the supplied content.</param>
    /// <returns>The text produced by build Relative Path.</returns>
    string BuildRelativePath(Uri rootUri, Uri resourceUri, bool isHtmlDocument, string? contentType);

    /// <summary>
    /// Determines whether a discovered page URI is eligible to enter the crawl queue.
    /// </summary>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="candidate">Discovered page URI to evaluate.</param>
    /// <param name="options">Options to configure for the Playwright Crawler URL.</param>
    /// <returns>true if the candidate may be queued; otherwise, false.</returns>
    bool ShouldQueuePage(Uri rootUri, Uri candidate, PlaywrightCrawlOptions options);

    /// <summary>
    /// Determines whether a discovered resource should be persisted under the crawl options.
    /// </summary>
    /// <param name="rootUri">Root URI that defines the crawl boundary.</param>
    /// <param name="resourceUri">Discovered resource URI to evaluate.</param>
    /// <param name="isHtmlDocument">Whether the resource is the rendered HTML document.</param>
    /// <param name="options">Options to configure for the Playwright Crawler URL.</param>
    /// <returns>true if the resource should be saved; otherwise, false.</returns>
    bool ShouldSaveResource(Uri rootUri, Uri resourceUri, bool isHtmlDocument, PlaywrightCrawlOptions options);

    /// <summary>
    /// Gets page links.
    /// </summary>
    /// <param name="page">Browser page to inspect or control.</param>
    /// <returns>A task whose result is the collection returned by get Page Links.</returns>
    Task<IReadOnlyList<string>> GetPageLinks(IPage page);

    /// <summary>
    /// Gets page resource urls.
    /// </summary>
    /// <param name="page">Browser page to inspect or control.</param>
    /// <returns>A task whose result is the collection returned by get Page Resource Urls.</returns>
    Task<IReadOnlyList<string>> GetPageResourceUrls(IPage page);

    /// <summary>
    /// Determines whether rendered HTML contains a Cloudflare Turnstile widget.
    /// </summary>
    /// <param name="html">The rendered HTML.</param>
    /// <returns>Whether Turnstile is present.</returns>
    bool HasTurnstile(string html);

    /// <summary>
    /// Determines whether the Playwright Crawler URL challenge Page.
    /// </summary>
    /// <param name="title">Page title, when available.</param>
    /// <param name="html">Rendered page HTML to inspect.</param>
    /// <returns>true if challenge-page markers were detected; otherwise, false.</returns>
    bool IsChallengePage(string? title, string html);

    /// <summary>
    /// Resolves ip Key.
    /// </summary>
    /// <param name="uri">Receives the normalized absolute URI when parsing succeeds.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the text returned by resolve Ip Key.</returns>
    Task<string> ResolveIpKey(Uri uri, CancellationToken cancellationToken);

    /// <summary>
    /// Gets domain key.
    /// </summary>
    /// <param name="uri">Receives the normalized absolute URI when parsing succeeds.</param>
    /// <returns>The requested text.</returns>
    string GetDomainKey(Uri uri);
}
