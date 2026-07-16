namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// A rendered page captured during a crawl.
/// </summary>
public sealed class PlaywrightCrawlPageResult
{
    /// <summary>
    /// URL requested by the crawler.
    /// </summary>
    public required string RequestedUrl { get; set; }

    /// <summary>
    /// Final URL after navigation and redirects.
    /// </summary>
    public required string FinalUrl { get; set; }

    /// <summary>
    /// Main-document HTTP status code when available.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Rendered document title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Whether the rendered page contains a Cloudflare Turnstile widget.
    /// </summary>
    public bool HasTurnstile { get; set; }

    /// <summary>
    /// Whether the rendered document is a browser challenge or CAPTCHA interstitial.
    /// A normally rendered page containing an embedded Turnstile widget is not a challenge page.
    /// </summary>
    public bool IsChallengePage { get; set; }

    /// <summary>
    /// Rendered HTML when in-memory capture is enabled.
    /// </summary>
    public string? Html { get; set; }
}
