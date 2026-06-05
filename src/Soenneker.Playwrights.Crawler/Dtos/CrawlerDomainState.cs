using Soenneker.Asyncs.Locks;
using Soenneker.Playwrights.Crawler.Enums;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Soenneker.Playwrights.Crawler.Dtos;

/// <summary>
/// Represents the crawler domain state.
/// </summary>
public sealed class CrawlerDomainState
{
    public CrawlerDomainState(string domainKey, int maxConcurrency)
    {
        DomainKey = domainKey;
        ConcurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        Mode = CrawlerDomainMode.Normal;
    }

    /// <summary>
    /// Gets domain key.
    /// </summary>
    public string DomainKey { get; }

    /// <summary>
    /// Gets concurrency semaphore.
    /// </summary>
    public SemaphoreSlim ConcurrencySemaphore { get; }

    /// <summary>
    /// Gets state lock.
    /// </summary>
    public AsyncLock StateLock { get; } = new();

    /// <summary>
    /// Gets timing lock.
    /// </summary>
    public AsyncLock TimingLock { get; } = new();

    /// <summary>
    /// Gets or sets last request utc.
    /// </summary>
    public DateTimeOffset? LastRequestUtc { get; set; }

    /// <summary>
    /// Gets or sets last page completed utc.
    /// </summary>
    public DateTimeOffset? LastPageCompletedUtc { get; set; }

    /// <summary>
    /// Gets or sets slow mode until utc.
    /// </summary>
    public DateTimeOffset? SlowModeUntilUtc { get; set; }

    /// <summary>
    /// Gets or sets cooldown until utc.
    /// </summary>
    public DateTimeOffset? CooldownUntilUtc { get; set; }

    /// <summary>
    /// Gets or sets mode.
    /// </summary>
    public CrawlerDomainMode Mode { get; set; }

    /// <summary>
    /// Gets or sets active count.
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// Gets or sets attempts.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets failures.
    /// </summary>
    public int Failures { get; set; }

    /// <summary>
    /// Gets or sets duplicate pages.
    /// </summary>
    public int DuplicatePages { get; set; }

    /// <summary>
    /// Gets recent429s.
    /// </summary>
    public Queue<DateTimeOffset> Recent429s { get; } = new();

    /// <summary>
    /// Gets recent403s.
    /// </summary>
    public Queue<DateTimeOffset> Recent403s { get; } = new();

    /// <summary>
    /// Gets recent response times ms.
    /// </summary>
    public List<long> RecentResponseTimesMs { get; } = [];
}
