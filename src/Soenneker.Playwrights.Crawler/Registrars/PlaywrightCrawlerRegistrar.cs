using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Playwright.Installation.Registrars;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Utils;
using Soenneker.Playwrights.Crawler.Utils.Abstract;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.File.Registrars;

namespace Soenneker.Playwrights.Crawler.Registrars;

/// <summary>
/// A configurable Playwright crawler with rich stealth and control options.
/// </summary>
public static class PlaywrightCrawlerRegistrar
{
    /// <summary>
    /// Adds <see cref="IPlaywrightCrawler"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddPlaywrightCrawlerAsSingleton(this IServiceCollection services)
    {
        services.AddFileUtilAsSingleton()
                .AddDirectoryUtilAsSingleton()
                .AddPlaywrightInstallationUtilAsSingleton();

        services.TryAddSingleton<IPlaywrightCrawlerBrowserUtil, PlaywrightCrawlerBrowserUtil>();
        services.TryAddSingleton<IPlaywrightCrawlerPolicyUtil, PlaywrightCrawlerPolicyUtil>();
        services.TryAddSingleton<IPlaywrightCrawlerUrlUtil, PlaywrightCrawlerUrlUtil>();
        services.TryAddSingleton<IPlaywrightCrawlerStorage, PlaywrightCrawlerStorage>();
        services.TryAddSingleton<IPlaywrightCrawler, PlaywrightCrawler>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IPlaywrightCrawler"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddPlaywrightCrawlerAsScoped(this IServiceCollection services)
    {
        services.AddFileUtilAsScoped()
                .AddDirectoryUtilAsScoped()
                .AddPlaywrightInstallationUtilAsSingleton();

        services.TryAddScoped<IPlaywrightCrawlerBrowserUtil, PlaywrightCrawlerBrowserUtil>();
        services.TryAddScoped<IPlaywrightCrawlerPolicyUtil, PlaywrightCrawlerPolicyUtil>();
        services.TryAddScoped<IPlaywrightCrawlerUrlUtil, PlaywrightCrawlerUrlUtil>();
        services.TryAddScoped<IPlaywrightCrawlerStorage, PlaywrightCrawlerStorage>();
        services.TryAddScoped<IPlaywrightCrawler, PlaywrightCrawler>();

        return services;
    }
}
