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
    /// <param name="playwright">Playwright for the create browser operation.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Browser.</param>
    /// <returns>A task whose result is the requested browser.</returns>
    Task<IBrowser> CreateBrowser(IPlaywright playwright, PlaywrightCrawlOptions options);

    /// <summary>
    /// Creates browser context.
    /// </summary>
    /// <param name="browser">Browser for the create browser context operation.</param>
    /// <param name="options">Options to configure for the Playwright Crawler Browser.</param>
    /// <returns>A task whose result is the requested browser Context.</returns>
    ValueTask<IBrowserContext> CreateBrowserContext(IBrowser browser, PlaywrightCrawlOptions options);
}
