[![](https://img.shields.io/nuget/v/soenneker.playwrights.crawler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.playwrights.crawler/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.playwrights.crawler/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.playwrights.crawler/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.playwrights.crawler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.playwrights.crawler/)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Playwrights.Crawler

A configurable Playwright crawler for mirroring sites to disk with support for:

- HTML-only or full resource capture
- crawl limits by depth, page count, duration, and storage
- same-host restrictions with optional cross-origin asset capture
- DOM attribute resource discovery for lazy widgets and deferred assets
- throttling, retries, slow mode, and cooldown behavior
- optional stealth launch/context settings

## Related Repos

You might also be interested in:

- [soenneker.playwrights.installation](https://github.com/soenneker/soenneker.playwrights.installation) for ensuring Playwright browsers are installed before runtime.
- [soenneker.playwrights.extensions.stealth](https://github.com/soenneker/soenneker.playwrights.extensions.stealth) for stealth-oriented Chromium launch and browser-context extensions.

## Installation

```bash
dotnet add package Soenneker.Playwrights.Crawler
```

## Register With DI

```csharp
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Playwrights.Crawler.Registrars;

var services = new ServiceCollection();

services.AddLogging();
services.AddPlaywrightCrawlerAsSingleton();
```

Use `AddPlaywrightCrawlerAsScoped()` if you prefer a scoped lifetime.

## Basic Usage

```csharp
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;

IPlaywrightCrawler crawler = serviceProvider.GetRequiredService<IPlaywrightCrawler>();

PlaywrightCrawlResult result = await crawler.Crawl(new PlaywrightCrawlOptions
{
    Url = "https://example.com",
    SaveDirectory = @"C:\temp\example",
    Mode = PlaywrightCrawlMode.Full,
    MaxDepth = 2,
    ClearSaveDirectory = true,
    SameHostOnly = true
});
```

## Advanced Example

```csharp
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Playwrights.Extensions.Stealth.Options;

PlaywrightCrawlResult result = await crawler.Crawl(new PlaywrightCrawlOptions
{
    Url = "https://example.com",
    SaveDirectory = @"C:\temp\example",
    Mode = PlaywrightCrawlMode.Full,
    MaxDepth = 2,
    MaxPages = 50,
    MaxStorageBytes = 250_000_000,
    MaxDuration = TimeSpan.FromMinutes(10),
    SameHostOnly = true,
    IgnoreQueryStringsInDuplicateDetection = true,
    FormatHtml = true,
    IncludeCrossOriginAssets = true,
    RewriteCrossOriginAssetUrls = true,
    ClearSaveDirectory = true,
    OverwriteExistingFiles = true,
    Headless = true,
    UseStealth = true,
    ThrottleMode = PlaywrightCrawlThrottleMode.Automatic,
    NavigationTimeoutMs = 45_000,
    WaitUntil = WaitUntilState.NetworkIdle,
    PostNavigationDelayMs = 0,
    ContinueOnPageError = true,
    StealthLaunchOptions = new StealthLaunchOptions
    {
        IgnoreDetectableDefaultArguments = true
    },
    StealthContextOptions = new StealthContextOptions
    {
        NormalizeDocumentHeaders = true,
        EnableCdpDomainHardening = false
    },
    Policy = new PlaywrightCrawlPolicy
    {
        GlobalMaxConcurrency = 20,
        PerDomainMaxConcurrency = 2,
        PerIpMaxConcurrency = 2,
        MinimumDelayBetweenRequestsMs = 750,
        DelayJitterMaxMs = 500,
        RequestTimeoutMs = 30_000,
        MaxRetries = 4
    }
});
```

## In-Memory Multi-Page Capture

Capture an explicit set of rendered pages through one shared browser and browser context without writing to disk or discovering links:

```csharp
PlaywrightCrawlResult result = await crawler.Crawl(new PlaywrightCrawlOptions
{
    StartingUrls =
    [
        "https://example.com/",
        "https://example.com/about",
        "https://example.com/pricing"
    ],
    SaveToDisk = false,
    CaptureRenderedHtml = true,
    DiscoverLinks = false,
    Mode = PlaywrightCrawlMode.HtmlOnly,
    WaitUntil = WaitUntilState.DOMContentLoaded,
    ThrottleMode = PlaywrightCrawlThrottleMode.Disabled,
    ExtraHttpHeaders = new Dictionary<string, string>
    {
        ["X-Crawler-Capture"] = "1"
    },
    ReadinessExpression =
        "() => document.readyState === 'complete'" +
        " && !!document.querySelector('main')",
    PageCompletedHandler = (page, pageResult, cancellationToken) =>
    {
        Console.WriteLine($"Completed {pageResult.FinalUrl} with status {pageResult.StatusCode}");
        return ValueTask.CompletedTask;
    }
});

foreach (PlaywrightCrawlPageResult page in result.Pages)
{
    Console.WriteLine($"{page.FinalUrl}: {page.Title} ({page.Html?.Length ?? 0} characters)");
}
```

`PageReadinessHandler` can be used instead of, or after, `ReadinessExpression` when readiness requires application-specific .NET logic.
`PageCompletedHandler` runs after each successfully processed page and before its live Playwright page is closed.

## Modes

### `HtmlOnly`

Saves only rendered HTML documents discovered during the crawl.

### `Full`

Saves:

- rendered HTML documents
- same-origin network resources observed while pages load
- resource URLs discovered in DOM attributes such as `data-src` and `data-css-url`
- optional cross-origin assets under `_external` when `IncludeCrossOriginAssets = true`
- optional rewriting of cross-origin asset URLs in saved HTML when `RewriteCrossOriginAssetUrls = true`
- optional lazy-load scrolling to capture below-the-fold media
- optional rewriting of same-origin absolute URLs in saved HTML and CSS to root-relative paths when `RewriteSameOriginAbsoluteUrls = true`

## Key Options

| Option | Description |
| --- | --- |
| `Url` | Optional primary absolute `http` or `https` starting URL; at least this or one `StartingUrls` entry is required. |
| `StartingUrls` | Additional explicit starting URLs. At least `Url` or one entry is required. All seeds share one browser/context. |
| `AllowedPageUrls` | Optional exact allowlist for discovered pages; accepts absolute URLs and root-relative paths. |
| `SaveToDisk` | Writes captured output beneath `SaveDirectory`. Disable for diskless capture. |
| `CaptureRenderedHtml` | Includes rendered HTML in `result.Pages`; automatically enabled for diskless capture. |
| `DiscoverLinks` | Controls whether rendered links are queued. Disable to capture only explicit starting URLs. |
| `SaveDirectory` | Output directory for mirrored content; required when `SaveToDisk` is true. |
| `MaxDepth` | Link depth to follow from the root page. `0` crawls only the starting page. |
| `MaxPages` | Optional hard cap on visited pages. |
| `MaxStorageBytes` | Optional hard cap on bytes written to disk. |
| `MaxDuration` | Optional maximum crawl duration. |
| `SameHostOnly` | Restricts queued pages to the same host as the root URL. |
| `IgnoreQueryStringsInDuplicateDetection` | Treats query-string variants as the same page when detecting duplicates. |
| `FormatHtml` | Formats saved HTML documents with `Soenneker.Html.Formatter` when `true`. Defaults to `false`. |
| `IncludeCrossOriginAssets` | In `Full` mode, saves cross-origin resources under `_external`. |
| `RewriteCrossOriginAssetUrls` | Rewrites saved HTML so captured cross-origin asset URLs point at the local `_external` copy. Requires `IncludeCrossOriginAssets`. |
| `RewriteSameOriginAbsoluteUrls` | Rewrites same-origin absolute URLs in saved HTML and CSS to root-relative paths, such as `https://example.com/script.js` to `/script.js`. |
| `TriggerLazyLoading` | In `Full` mode, scrolls pages after navigation to trigger lazy-loaded media before resources are saved. Defaults to `true`. |
| `LazyLoadScrollStepPx` | Pixel distance for each lazy-load scroll step. |
| `LazyLoadScrollDelayMs` | Delay after each lazy-load scroll step. |
| `LazyLoadMaxScrolls` | Maximum number of lazy-load scroll steps per page. |
| `ClearSaveDirectory` | Deletes the output directory before crawling. |
| `OverwriteExistingFiles` | Controls whether existing files can be replaced. |
| `Headless` | Runs Chromium headlessly when `true`. |
| `UseStealth` | Enables the Soenneker stealth Playwright extensions. |
| `ExtraHttpHeaders` | Context-wide HTTP headers sent by every crawler page. |
| `ThrottleMode` | Controls automatic pacing and adaptive throttling. Defaults to `Automatic`; use `Disabled` to bypass automatic pacing, slow mode, cooldown waiting, and implicit post-navigation jitter. |
| `NavigationTimeoutMs` | Navigation timeout per page. |
| `WaitUntil` | Playwright load state awaited during navigation. Defaults to `NetworkIdle`. |
| `PostNavigationDelayMs` | Extra delay after navigation to allow late assets to settle. |
| `ReadinessExpression` | Optional JavaScript boolean predicate polled after navigation. |
| `ReadinessArgument` | Optional serializable argument supplied to the JavaScript readiness predicate. |
| `PageReadinessHandler` | Optional .NET readiness callback invoked after the JavaScript predicate. |
| `PageCompletedHandler` | Optional async callback invoked with the live page and crawl result after page processing and before the page closes. |
| `ReadinessTimeoutMs` | Readiness timeout; defaults to `NavigationTimeoutMs`. |
| `ReadinessPollingIntervalMs` | JavaScript readiness polling interval; defaults to 100 ms. |
| `ContinueOnPageError` | Continues crawling after an individual page fails. |
| `Policy` | Crawl throttling, retries, concurrency, slow mode, and cooldown configuration. |

## Result

`Crawl()` returns `PlaywrightCrawlResult`, which includes:

- crawl timing (`StartedAtUtc`, `CompletedAtUtc`, `Duration`)
- page counts (`PagesDiscovered`, `PagesVisited`)
- file counts (`HtmlFilesSaved`, `AssetFilesSaved`)
- total bytes written (`BytesWritten`)
- stop reasons (`StorageLimitReached`, `DurationLimitReached`, `PageLimitReached`)
- per-file details in `Files`
- rendered page details and optional in-memory HTML in `Pages`
- page-level failures in `Errors`

## Output Layout

Saved files preserve URL structure so the output can be served by a simple static web server.

Examples:

- `https://example.com/` -> `index.html`
- `https://example.com/docs/getting-started` -> `docs/getting-started/index.html`
- `https://example.com/script.js` -> `/script.js` inside saved HTML when same-origin URL rewriting is enabled
- `https://cdn.example.com/app.css` -> `_external/cdn.example.com/app.css` when cross-origin asset capture is enabled
- a saved page can reference that asset as `../../_external/cdn.example.com/app.css` when URL rewriting is enabled

## Behavior Notes

- Playwright browser installation is ensured automatically before the crawl starts.
- Multiple starting URLs use one Playwright instance, browser, and browser context.
- `PageCompletedHandler` callbacks can run concurrently when the crawl uses multiple workers.
- Setting `DiscoverLinks = false` captures only explicitly supplied starting URLs.
- Setting `SaveToDisk = false` avoids output-directory creation and returns rendered HTML through `Pages`.
- Duplicate detection ignores query strings by default.
- HTML formatting is opt-in and uses `Soenneker.Html.Formatter` when `FormatHtml = true`.
- Challenge and captcha-like pages contribute to the crawler's blocking and slow-mode signals.
- Setting `ThrottleMode = PlaywrightCrawlThrottleMode.Disabled` keeps configured concurrency limits and retries, but skips the crawler's automatic pacing and adaptive slowdown behavior.
- Cross-origin URL rewriting only applies to captured cross-origin assets that are actually available on disk.
- `Full` mode captures resources observed during page loads, but the rewrite pass is limited to captured cross-origin asset URLs rather than a full offline-mirroring transform.
- Some response types are intentionally skipped, such as empty bodies and certain framework/internal fetch endpoints.
