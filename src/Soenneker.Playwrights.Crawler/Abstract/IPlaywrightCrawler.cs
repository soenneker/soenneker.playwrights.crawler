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
    ValueTask<PlaywrightCrawlResult> Crawl(PlaywrightCrawlOptions options, CancellationToken cancellationToken = default);
}
