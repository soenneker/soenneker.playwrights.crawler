namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// Represents a file encountered during a crawl, whether it was written or skipped.
/// </summary>
public sealed class PlaywrightCrawlFileResult
{
    /// <summary>
    /// Source URL for the file.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Relative path underneath the configured save directory.
    /// </summary>
    public required string RelativePath { get; set; }

    /// <summary>
    /// Indicates whether the file was an HTML document.
    /// </summary>
    public bool IsHtmlDocument { get; set; }

    /// <summary>
    /// Response content type when available.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Number of bytes written for the file. Zero when skipped.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Whether the file was written to disk.
    /// </summary>
    public bool Saved { get; set; }

    /// <summary>
    /// Optional explanation for why the file was skipped.
    /// </summary>
    public string? SkipReason { get; set; }
}
