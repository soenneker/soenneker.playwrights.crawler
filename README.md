[![](https://img.shields.io/nuget/v/soenneker.playwrights.crawler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.playwrights.crawler/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.playwrights.crawler/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.playwrights.crawler/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.playwrights.crawler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.playwrights.crawler/)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Playwrights.Crawler

A configurable Playwright crawler for mirroring sites to disk with support for:

- HTML-only or full resource capture
- crawl limits by depth, page count, duration, and storage
- same-host restrictions with optional cross-origin asset capture
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

## Modes

### `HtmlOnly`

Saves only rendered HTML documents discovered during the crawl.

### `Full`

Saves:

- rendered HTML documents
- same-origin network resources observed while pages load
- optional cross-origin assets under `_external` when `IncludeCrossOriginAssets = true`
- optional rewriting of cross-origin asset URLs in saved HTML when `RewriteCrossOriginAssetUrls = true`

## Key Options

| Option | Description |
| --- | --- |
| `Url` | Required absolute `http` or `https` root URL. |
| `SaveDirectory` | Required output directory for mirrored content. |
| `MaxDepth` | Link depth to follow from the root page. `0` crawls only the starting page. |
| `MaxPages` | Optional hard cap on visited pages. |
| `MaxStorageBytes` | Optional hard cap on bytes written to disk. |
| `MaxDuration` | Optional maximum crawl duration. |
| `SameHostOnly` | Restricts queued pages to the same host as the root URL. |
| `IgnoreQueryStringsInDuplicateDetection` | Treats query-string variants as the same page when detecting duplicates. |
| `FormatHtml` | Formats saved HTML documents with `Soenneker.Html.Formatter` when `true`. Defaults to `false`. |
| `IncludeCrossOriginAssets` | In `Full` mode, saves cross-origin resources under `_external`. |
| `RewriteCrossOriginAssetUrls` | Rewrites saved HTML so captured cross-origin asset URLs point at the local `_external` copy. Requires `IncludeCrossOriginAssets`. |
| `ClearSaveDirectory` | Deletes the output directory before crawling. |
| `OverwriteExistingFiles` | Controls whether existing files can be replaced. |
| `Headless` | Runs Chromium headlessly when `true`. |
| `UseStealth` | Enables the Soenneker stealth Playwright extensions. |
| `ThrottleMode` | Controls automatic pacing and adaptive throttling. Defaults to `Automatic`; use `Disabled` to bypass automatic pacing, slow mode, cooldown waiting, and implicit post-navigation jitter. |
| `NavigationTimeoutMs` | Navigation timeout per page. |
| `PostNavigationDelayMs` | Extra delay after navigation to allow late assets to settle. |
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
- page-level failures in `Errors`

## Output Layout

Saved files preserve URL structure so the output can be served by a simple static web server.

Examples:

- `https://example.com/` -> `index.html`
- `https://example.com/docs/getting-started` -> `docs/getting-started/index.html`
- `https://cdn.example.com/app.css` -> `_external/cdn.example.com/app.css` when cross-origin asset capture is enabled
- a saved page can reference that asset as `../../_external/cdn.example.com/app.css` when URL rewriting is enabled

## Behavior Notes

- Playwright browser installation is ensured automatically before the crawl starts.
- Duplicate detection ignores query strings by default.
- HTML formatting is opt-in and uses `Soenneker.Html.Formatter` when `FormatHtml = true`.
- Challenge and captcha-like pages contribute to the crawler's blocking and slow-mode signals.
- Setting `ThrottleMode = PlaywrightCrawlThrottleMode.Disabled` keeps configured concurrency limits and retries, but skips the crawler's automatic pacing and adaptive slowdown behavior.
- Cross-origin URL rewriting only applies to captured cross-origin assets that are actually available on disk.
- `Full` mode captures resources observed during page loads, but the rewrite pass is limited to captured cross-origin asset URLs rather than a full offline-mirroring transform.
- Some response types are intentionally skipped, such as empty bodies and certain framework/internal fetch endpoints.
