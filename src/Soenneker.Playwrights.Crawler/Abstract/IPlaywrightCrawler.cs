using System.Threading;
using System.Threading.Tasks;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Abstract;

/// <summary>
/// A configurable Playwright crawler with rich stealth and control options.
/// </summary>
public interface IPlaywrightCrawler
{
    /// <summary>
    /// Crawls or captures the configured URLs and optionally writes mirrored output to disk.
    /// </summary>
    /// <param name="options">Options to configure for the Playwright Crawler.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the requested playwright Crawl Result.</returns>
    ValueTask<PlaywrightCrawlResult> Crawl(PlaywrightCrawlOptions options, CancellationToken cancellationToken = default);
}
