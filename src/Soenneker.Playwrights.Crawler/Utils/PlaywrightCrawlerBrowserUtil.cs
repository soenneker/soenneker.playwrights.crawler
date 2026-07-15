using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Extensions.Stealth;
using Soenneker.Playwrights.Crawler.Utils.Abstract;

namespace Soenneker.Playwrights.Crawler.Utils;

internal sealed class PlaywrightCrawlerBrowserUtil : IPlaywrightCrawlerBrowserUtil
{
    public Task<IBrowser> CreateBrowser(IPlaywright playwright, PlaywrightCrawlOptions options)
    {
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = options.Headless,
            Channel = options.Channel
        };

        if (options.UseStealth)
            return playwright.LaunchStealthChromium(launchOptions, options.StealthLaunchOptions);

        return playwright.Chromium.LaunchAsync(launchOptions);
    }

    public ValueTask<IBrowserContext> CreateBrowserContext(IBrowser browser, PlaywrightCrawlOptions options)
    {
        var contextOptions = new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = options.ExtraHttpHeaders.Count > 0 ? options.ExtraHttpHeaders : null,
            Proxy = options.StealthContextOptions?.Proxy
        };

        if (options.UseStealth)
            return browser.CreateStealthContext(contextOptions, options.StealthContextOptions);

        return new ValueTask<IBrowserContext>(browser.NewContextAsync(contextOptions));
    }
}
