using Soenneker.Asyncs.Locks;
using Soenneker.Playwrights.Crawler.Enums;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Soenneker.Playwrights.Crawler.Dtos;

public sealed class CrawlerDomainState
{
    public CrawlerDomainState(string domainKey, int maxConcurrency)
    {
        DomainKey = domainKey;
        ConcurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public string DomainKey { get; }

    public SemaphoreSlim ConcurrencySemaphore { get; }

    public AsyncLock StateLock { get; } = new();

    public AsyncLock TimingLock { get; } = new();

    public DateTimeOffset? LastRequestUtc { get; set; }

    public DateTimeOffset? LastPageCompletedUtc { get; set; }

    public DateTimeOffset? SlowModeUntilUtc { get; set; }

    public DateTimeOffset? CooldownUntilUtc { get; set; }

    public CrawlerDomainMode Mode { get; set; }

    public int ActiveCount { get; set; }

    public int Attempts { get; set; }

    public int Failures { get; set; }

    public int DuplicatePages { get; set; }

    public Queue<DateTimeOffset> Recent429s { get; } = new();

    public Queue<DateTimeOffset> Recent403s { get; } = new();

    public List<long> RecentResponseTimesMs { get; } = [];
}
