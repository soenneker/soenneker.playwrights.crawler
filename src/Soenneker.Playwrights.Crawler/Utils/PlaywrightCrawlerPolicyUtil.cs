using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Asyncs.Locks;
using Soenneker.Extensions.Task;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Playwrights.Crawler.Utils.Abstract;

namespace Soenneker.Playwrights.Crawler.Utils;

internal sealed class PlaywrightCrawlerPolicyUtil : IPlaywrightCrawlerPolicyUtil
{
    private readonly ILogger<PlaywrightCrawlerPolicyUtil> _logger;

    public PlaywrightCrawlerPolicyUtil(ILogger<PlaywrightCrawlerPolicyUtil> logger)
    {
        _logger = logger;
    }

    public async Task<IResponse?> NavigateWithPolicy(IPage page, Uri targetUri, PlaywrightCrawlOptions options, CrawlerDomainState domainState,
        SemaphoreSlim globalSemaphore, SemaphoreSlim ipSemaphore, CancellationToken cancellationToken)
    {
        PlaywrightCrawlPolicy policy = options.Policy ?? new PlaywrightCrawlPolicy();

        for (var attempt = 0;; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                _logger.LogDebug("Retrying navigation to {Url} on attempt {Attempt} of {TotalAttempts}", targetUri.AbsoluteUri, attempt + 1,
                    policy.MaxRetries + 1);
            }

            await EnsureDomainRequestAllowed(domainState, policy, cancellationToken)
                .NoSync();

            var globalAcquired = false;
            var domainAcquired = false;
            var ipAcquired = false;

            try
            {
                await globalSemaphore.WaitAsync(cancellationToken)
                                     .NoSync();
                globalAcquired = true;

                await ipSemaphore.WaitAsync(cancellationToken)
                                 .NoSync();
                ipAcquired = true;

                await AcquireDomainConcurrency(domainState, policy, cancellationToken)
                    .NoSync();
                domainAcquired = true;

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    IResponse? response = await page.GotoAsync(targetUri.AbsoluteUri, new PageGotoOptions
                                                    {
                                                        Timeout = options.NavigationTimeoutMs,
                                                        WaitUntil = options.WaitUntil
                                                    })
                                                    .NoSync();

                    stopwatch.Stop();

                    RecordNavigationOutcome(domainState, policy, response?.Status, stopwatch.ElapsedMilliseconds, response?.Ok ?? false);

                    if (response is not null && IsRetryableStatusCode(response.Status) && attempt < policy.MaxRetries)
                    {
                        _logger.LogDebug("Navigation to {Url} returned retryable status {StatusCode} after {ElapsedMs}ms; backing off before retry {NextAttempt} of {TotalAttempts}",
                            targetUri.AbsoluteUri, response.Status, stopwatch.ElapsedMilliseconds, attempt + 2, policy.MaxRetries + 1);

                        await DelayRetry(targetUri, attempt, policy, cancellationToken)
                            .NoSync();
                        continue;
                    }

                    return response;
                }
                catch (PlaywrightException ex) when (IsTransientNetworkFailure(ex) && attempt < policy.MaxRetries)
                {
                    stopwatch.Stop();
                    RecordNavigationOutcome(domainState, policy, null, stopwatch.ElapsedMilliseconds, success: false);

                    _logger.LogDebug(ex,
                        "Navigation to {Url} failed transiently after {ElapsedMs}ms; backing off before retry {NextAttempt} of {TotalAttempts}",
                        targetUri.AbsoluteUri, stopwatch.ElapsedMilliseconds, attempt + 2, policy.MaxRetries + 1);

                    await DelayRetry(targetUri, attempt, policy, cancellationToken)
                        .NoSync();
                }
            }
            finally
            {
                if (ipAcquired)
                    ipSemaphore.Release();

                if (domainAcquired)
                    ReleaseDomainConcurrency(domainState);

                if (globalAcquired)
                    globalSemaphore.Release();
            }
        }
    }

    public async Task EnsureDomainRequestAllowed(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken)
    {
        using Releaser timingReleaser = await domainState.TimingLock.Lock(cancellationToken);

        while (true)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RefreshDomainMode(domainState, now);

            TimeSpan pageWait;
            TimeSpan wait;
            TimeSpan cooldownWait;

            using (domainState.StateLock.LockSync(cancellationToken))
            {
                cooldownWait = domainState.Mode == CrawlerDomainMode.Cooldown && domainState.CooldownUntilUtc.HasValue && domainState.CooldownUntilUtc > now
                    ? domainState.CooldownUntilUtc.Value - now
                    : TimeSpan.Zero;

                int requestDelayMs = domainState.Mode == CrawlerDomainMode.Slow
                    ? policy.SlowModeMinimumDelayBetweenRequestsMs + Random.Shared.Next(policy.SlowModeDelayJitterMaxMs + 1)
                    : policy.MinimumDelayBetweenRequestsMs + Random.Shared.Next(policy.DelayJitterMaxMs + 1);

                int pageDelayMs = Random.Shared.Next(policy.SameHostPageToPageDelayMinMs, policy.SameHostPageToPageDelayMaxMs + 1);
                wait = ComputeRequiredWait(domainState.LastRequestUtc, requestDelayMs, now);
                pageWait = ComputeRequiredWait(domainState.LastPageCompletedUtc, pageDelayMs, now);
            }

            TimeSpan effectiveWait = Max(cooldownWait, Max(wait, pageWait));

            if (effectiveWait > TimeSpan.Zero)
            {
                _logger.LogDebug(
                    "Throttling domain {DomainKey} for {DelayMs}ms (mode: {Mode}, cooldownMs: {CooldownMs}, requestDelayMs: {RequestDelayMs}, pageDelayMs: {PageDelayMs})",
                    domainState.DomainKey, (int)effectiveWait.TotalMilliseconds, domainState.Mode, (int)cooldownWait.TotalMilliseconds,
                    (int)wait.TotalMilliseconds, (int)pageWait.TotalMilliseconds);

                await Task.Delay(effectiveWait, cancellationToken)
                          .NoSync();
                continue;
            }

            using (domainState.StateLock.LockSync(cancellationToken))
            {
                domainState.LastRequestUtc = DateTimeOffset.UtcNow;
            }

            break;
        }
    }

    public async Task AcquireDomainConcurrency(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken)
    {
        await domainState.ConcurrencySemaphore.WaitAsync(cancellationToken)
                         .NoSync();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (domainState.StateLock.LockSync(cancellationToken))
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    RefreshDomainModeUnsafe(domainState, now);

                    int allowedConcurrency = domainState.Mode == CrawlerDomainMode.Slow
                        ? policy.SlowModePerDomainMaxConcurrency
                        : policy.PerDomainMaxConcurrency;

                    if (domainState.Mode != CrawlerDomainMode.Cooldown && domainState.ActiveCount < allowedConcurrency)
                    {
                        domainState.ActiveCount++;
                        return;
                    }

                    _logger.LogDebug("Waiting for domain concurrency on {DomainKey} (mode: {Mode}, active: {ActiveCount}, allowed: {AllowedConcurrency})",
                        domainState.DomainKey, domainState.Mode, domainState.ActiveCount, allowedConcurrency);
                }

                await Task.Delay(100, cancellationToken)
                          .NoSync();
            }
        }
        catch
        {
            domainState.ConcurrencySemaphore.Release();
            throw;
        }
    }

    public void ReleaseDomainConcurrency(CrawlerDomainState domainState)
    {
        using (domainState.StateLock.LockSync())
        {
            if (domainState.ActiveCount > 0)
                domainState.ActiveCount--;
        }

        domainState.ConcurrencySemaphore.Release();
    }

    public void RecordNavigationOutcome(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, int? statusCode, long elapsedMs, bool success)
    {
        using (domainState.StateLock.LockSync())
        {
            domainState.Attempts++;

            if (!success)
                domainState.Failures++;

            domainState.RecentResponseTimesMs.Add(elapsedMs);

            if (domainState.RecentResponseTimesMs.Count > 20)
                domainState.RecentResponseTimesMs.RemoveAt(0);

            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (statusCode == 429)
            {
                domainState.Recent429s.Enqueue(now);
                TrimSignalQueue(domainState.Recent429s, policy.SlowModeSignalWindowMs, now);
            }
            else if (statusCode == 403)
            {
                domainState.Recent403s.Enqueue(now);
                TrimSignalQueue(domainState.Recent403s, policy.SlowModeSignalWindowMs, now);
            }

            EvaluateDomainHealth(domainState, policy, now);
        }
    }

    public void HandleBlockingSignal(ILogger logger, CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, int statusCode, string reason)
    {
        logger.LogWarning("Domain blocking signal detected for {DomainKey}: {Reason} (status {StatusCode})", domainState.DomainKey, reason, statusCode);

        using (domainState.StateLock.LockSync())
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (statusCode == 429)
            {
                domainState.Recent429s.Enqueue(now);
                TrimSignalQueue(domainState.Recent429s, policy.SlowModeSignalWindowMs, now);
            }
            else if (statusCode == 403)
            {
                domainState.Recent403s.Enqueue(now);
                TrimSignalQueue(domainState.Recent403s, policy.SlowModeSignalWindowMs, now);
            }

            PromoteToSlowModeOrCooldown(domainState, policy, now, reason);
        }
    }

    public void RecordDuplicatePage(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy)
    {
        using (domainState.StateLock.LockSync())
        {
            domainState.DuplicatePages++;
        }

        if (policy.DuplicatePageThreshold > 0 && domainState.DuplicatePages == policy.DuplicatePageThreshold)
        {
            _logger.LogWarning("Domain {DomainKey} reached duplicate page threshold ({DuplicatePages}). Duplicate pages no longer trigger slow mode automatically.",
                domainState.DomainKey, domainState.DuplicatePages);
        }
    }

    public void MarkPageCompleted(CrawlerDomainState domainState)
    {
        using (domainState.StateLock.LockSync())
        {
            domainState.LastPageCompletedUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RefreshDomainMode(CrawlerDomainState domainState, DateTimeOffset now)
    {
        using (domainState.StateLock.LockSync())
        {
            RefreshDomainModeUnsafe(domainState, now);
        }
    }

    public int GetPostNavigationDelayMs(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy)
    {
        if (options.PostNavigationDelayMs > 0)
            return options.PostNavigationDelayMs;

        return Random.Shared.Next(policy.PostNavigationJitterMinMs, policy.PostNavigationJitterMaxMs + 1);
    }

    public int GetWorkerCount(PlaywrightCrawlOptions options, PlaywrightCrawlPolicy policy)
    {
        int max = policy.GlobalMaxConcurrency;

        if (options.MaxPages.HasValue)
            max = Math.Min(max, options.MaxPages.Value);

        return Math.Max(1, max);
    }

    public bool ShouldStop(PlaywrightCrawlOptions options, PlaywrightCrawlResult result, System.Diagnostics.Stopwatch stopwatch)
    {
        if (options.MaxPages.HasValue && result.PagesVisited >= options.MaxPages.Value)
        {
            result.PageLimitReached = true;
            return true;
        }

        if (options.MaxDuration.HasValue && stopwatch.Elapsed >= options.MaxDuration.Value)
        {
            result.DurationLimitReached = true;
            return true;
        }

        return result.StorageLimitReached;
    }

    public void ValidatePolicy(PlaywrightCrawlPolicy policy)
    {
        if (policy.GlobalMaxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "GlobalMaxConcurrency must be greater than zero.");

        if (policy.PerDomainMaxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "PerDomainMaxConcurrency must be greater than zero.");

        if (policy.PerIpMaxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "PerIpMaxConcurrency must be greater than zero.");

        if (policy.RequestTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "RequestTimeoutMs must be greater than zero.");

        if (policy.MaxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "MaxRetries cannot be negative.");
    }

    private static void RefreshDomainModeUnsafe(CrawlerDomainState domainState, DateTimeOffset now)
    {
        if (domainState.Mode == CrawlerDomainMode.Cooldown && domainState.CooldownUntilUtc.HasValue && domainState.CooldownUntilUtc <= now)
        {
            domainState.Mode = CrawlerDomainMode.Normal;
            domainState.CooldownUntilUtc = null;
        }

        if (domainState.Mode == CrawlerDomainMode.Slow && domainState.SlowModeUntilUtc.HasValue && domainState.SlowModeUntilUtc <= now)
        {
            domainState.Mode = CrawlerDomainMode.Normal;
            domainState.SlowModeUntilUtc = null;
        }
    }

    private void EvaluateDomainHealth(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, DateTimeOffset now)
    {
        TrimSignalQueue(domainState.Recent429s, policy.SlowModeSignalWindowMs, now);
        TrimSignalQueue(domainState.Recent403s, policy.SlowModeSignalWindowMs, now);

        if (domainState.Recent429s.Count >= 2 || domainState.Recent403s.Count >= 2)
        {
            PromoteToSlowModeOrCooldown(domainState, policy, now, "repeated 429/403 signals");
            return;
        }

        if (domainState.RecentResponseTimesMs.Count > 0)
        {
            List<long> ordered = [.. domainState.RecentResponseTimesMs.OrderBy(static value => value)];
            long median = ordered[ordered.Count / 2];

            if (median > policy.SlowModeMedianResponseThresholdMs)
            {
                PromoteToSlowModeOrCooldown(domainState, policy, now,
                    $"median response time {median}ms exceeded threshold {policy.SlowModeMedianResponseThresholdMs}ms");
                return;
            }
        }

        if (domainState.Attempts >= 4)
        {
            double errorRate = domainState.Failures / (double)domainState.Attempts;

            if (errorRate > policy.ErrorRateThreshold)
                PromoteToSlowModeOrCooldown(domainState, policy, now,
                    $"error rate {errorRate:P1} exceeded threshold {policy.ErrorRateThreshold:P1}");
        }
    }

    private void PromoteToSlowModeOrCooldown(CrawlerDomainState domainState, PlaywrightCrawlPolicy policy, DateTimeOffset now, string reason)
    {
        if (domainState.Mode == CrawlerDomainMode.Slow)
        {
            domainState.Mode = CrawlerDomainMode.Cooldown;
            domainState.CooldownUntilUtc = now.AddMilliseconds(policy.CooldownDurationMs);
            _logger.LogWarning("Domain {DomainKey} entered cooldown for {DurationMs}ms: {Reason}", domainState.DomainKey, policy.CooldownDurationMs, reason);
            return;
        }

        if (domainState.Mode != CrawlerDomainMode.Cooldown)
        {
            domainState.Mode = CrawlerDomainMode.Slow;
            domainState.SlowModeUntilUtc = now.AddMilliseconds(policy.SlowModeDurationMs);
            _logger.LogWarning("Domain {DomainKey} entered slow mode for {DurationMs}ms: {Reason}", domainState.DomainKey, policy.SlowModeDurationMs, reason);
        }
    }

    private static void TrimSignalQueue(Queue<DateTimeOffset> queue, int windowMs, DateTimeOffset now)
    {
        TimeSpan window = TimeSpan.FromMilliseconds(windowMs);

        while (queue.Count > 0 && now - queue.Peek() > window)
        {
            queue.Dequeue();
        }
    }

    private static TimeSpan ComputeRequiredWait(DateTimeOffset? lastTimestamp, int requiredDelayMs, DateTimeOffset now)
    {
        if (!lastTimestamp.HasValue)
            return TimeSpan.Zero;

        TimeSpan elapsed = now - lastTimestamp.Value;
        TimeSpan required = TimeSpan.FromMilliseconds(requiredDelayMs);

        return elapsed >= required ? TimeSpan.Zero : required - elapsed;
    }

    private static bool IsRetryableStatusCode(int statusCode)
    {
        return statusCode is 408 or 500 or 502 or 503 or 504;
    }

    private static bool IsTransientNetworkFailure(PlaywrightException ex)
    {
        string message = ex.Message;

        return message.Contains("ERR_", StringComparison.OrdinalIgnoreCase) || message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("socket", StringComparison.OrdinalIgnoreCase) || message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DelayRetry(Uri targetUri, int attempt, PlaywrightCrawlPolicy policy, CancellationToken cancellationToken)
    {
        int multiplier = 1 << attempt;
        int baseDelayMs = policy.RetryBaseDelayMs * multiplier;
        int jitterMaxMs = Math.Max(1, baseDelayMs / 2);
        int jitterMs = Random.Shared.Next(jitterMaxMs + 1);
        int delayMs = baseDelayMs + jitterMs;

        _logger.LogDebug("Delaying retry for {Url} by {DelayMs}ms (attempt index {AttemptIndex})", targetUri.AbsoluteUri, delayMs, attempt);

        await Task.Delay(delayMs, cancellationToken)
                  .NoSync();
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second)
    {
        return first >= second ? first : second;
    }
}