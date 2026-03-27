using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;

namespace Soenneker.Playwrights.Crawler.Utils.Abstract;

public interface IPlaywrightCrawlerBrowserUtil
{
    Task<IBrowser> CreateBrowser(IPlaywright playwright, PlaywrightCrawlOptions options);

    ValueTask<IBrowserContext> CreateBrowserContext(IBrowser browser, PlaywrightCrawlOptions options);
}
