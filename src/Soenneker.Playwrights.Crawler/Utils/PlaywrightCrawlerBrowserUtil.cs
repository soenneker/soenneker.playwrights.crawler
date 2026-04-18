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
        if (options.UseStealth)
            return browser.CreateStealthContext(new BrowserNewContextOptions(), options.StealthContextOptions);

        var contextOptions = new BrowserNewContextOptions
        {
            Proxy = options.StealthContextOptions?.Proxy
        };

        return new ValueTask<IBrowserContext>(browser.NewContextAsync(contextOptions));
    }
}
