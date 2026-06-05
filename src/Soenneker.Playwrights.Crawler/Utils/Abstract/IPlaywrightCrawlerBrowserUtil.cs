using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

/// <summary>
/// Defines the playwright crawler browser util contract.
/// </summary>
public interface IPlaywrightCrawlerBrowserUtil
{
    /// <summary>
    /// Creates browser.
    /// </summary>
    /// <param name="playwright">The playwright.</param>
    /// <param name="options">The options.</param>
    /// <returns>A task containing the result of the operation.</returns>
    Task<IBrowser> CreateBrowser(IPlaywright playwright, PlaywrightCrawlOptions options);

    /// <summary>
    /// Creates browser context.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="options">The options.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<IBrowserContext> CreateBrowserContext(IBrowser browser, PlaywrightCrawlOptions options);
}
