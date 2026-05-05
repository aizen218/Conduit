using System.Diagnostics;

namespace Conduit;

public sealed class Pipeline<T>
{
    private readonly Chan<T> _source;

    internal Pipeline(Chan<T> source) => _source = source;

    /// <summary>Transforms each item. Optionally retries on failure per <paramref name="retry"/>.</summary>
    public Pipeline<TOut> Transform<TOut>(
        Func<T, CancellationToken, ValueTask<TOut>> transform,
        RetryPolicy retry = default,
        int bufferSize = 16,
        CancellationToken ct = default)
    {
        var next = Chan.Bounded<TOut>(bufferSize);
        _ = Task.Run(async () =>
        {
            Exception? fault = null;
            try
            {
                await foreach (var item in _source.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    var result = retry.MaxAttempts > 0
                        ? await ExecuteWithRetry(transform, item, retry, ct).ConfigureAwait(false)
                        : await transform(item, ct).ConfigureAwait(false);
                    await next.WriteAsync(result, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fault = ex;
            }
            finally { next.Complete(fault); }
        }, ct);
        return new Pipeline<TOut>(next);
    }

    /// <summary>Transforms with N parallel workers. Order is NOT preserved. Optionally retries on failure.</summary>
    public Pipeline<TOut> ParallelTransform<TOut>(
        Func<T, CancellationToken, ValueTask<TOut>> transform,
        RetryPolicy retry = default,
        int workers = 4,
        int bufferSize = 64,
        CancellationToken ct = default)
    {
        var next = Chan.Bounded<TOut>(bufferSize);
        _ = Task.Run(async () =>
        {
            Exception? fault = null;
            try
            {
                var workerTasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
                {
                    await foreach (var item in _source.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        var result = retry.MaxAttempts > 0
                            ? await ExecuteWithRetry(transform, item, retry, ct).ConfigureAwait(false)
                            : await transform(item, ct).ConfigureAwait(false);
                        await next.WriteAsync(result, ct).ConfigureAwait(false);
                    }
                }, ct)).ToArray();
                await Task.WhenAll(workerTasks).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fault = ex;
            }
            finally { next.Complete(fault); }
        }, ct);
        return new Pipeline<TOut>(next);
    }

    /// <summary>Drops items that fail <paramref name="predicate"/>.</summary>
    public Pipeline<T> Filter(
        Func<T, CancellationToken, ValueTask<bool>> predicate,
        int bufferSize = 16,
        CancellationToken ct = default)
    {
        var next = Chan.Bounded<T>(bufferSize);
        _ = Task.Run(async () =>
        {
            Exception? fault = null;
            try
            {
                await foreach (var item in _source.ReadAllAsync(ct).ConfigureAwait(false))
                    if (await predicate(item, ct).ConfigureAwait(false))
                        await next.WriteAsync(item, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fault = ex;
            }
            finally { next.Complete(fault); }
        }, ct);
        return new Pipeline<T>(next);
    }

    /// <summary>Batches items into arrays of <paramref name="batchSize"/>, flushing early on <paramref name="flushTimeout"/>.</summary>
    public Pipeline<T[]> Batch(int batchSize, TimeSpan? flushTimeout = null, CancellationToken ct = default)
    {
        var next = Chan.Unbounded<T[]>();
        var timeout = flushTimeout ?? TimeSpan.FromSeconds(1);

        _ = Task.Run(async () =>
        {
            var batch = new List<T>(batchSize);

            async Task FlushAsync()
            {
                if (batch.Count > 0)
                {
                    await next.WriteAsync([.. batch], ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);
                    try
                    {
                        var item = await _source.ReadAsync(cts.Token).ConfigureAwait(false);
                        batch.Add(item);
                        if (batch.Count >= batchSize) await FlushAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await FlushAsync().ConfigureAwait(false);
                    }

                    if (_source.IsCompleted)
                    {
                        while (_source.TryRead(out var remaining))
                        {
                            batch.Add(remaining!);
                            if (batch.Count >= batchSize) await FlushAsync().ConfigureAwait(false);
                        }
                        await FlushAsync().ConfigureAwait(false);
                        break;
                    }
                }
            }
            finally { next.Complete(); }
        }, ct);

        return new Pipeline<T[]>(next);
    }

    /// <summary>
    /// Throttles throughput to at most <paramref name="permitsPerSecond"/> items per second.
    /// First item passes through immediately; subsequent items are delayed to enforce the rate.
    /// </summary>
    public Pipeline<T> RateLimit(double permitsPerSecond, int bufferSize = 16, CancellationToken ct = default)
    {
        var next = Chan.Bounded<T>(bufferSize);
        var intervalMs = 1000.0 / permitsPerSecond;

        _ = Task.Run(async () =>
        {
            Exception? fault = null;
            long count = 0;
            var startTs = Stopwatch.GetTimestamp();
            try
            {
                await foreach (var item in _source.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    if (count > 0)
                    {
                        var targetMs = count * intervalMs;
                        var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
                        var waitMs = targetMs - elapsedMs;
                        if (waitMs > 1.0)
                            await Task.Delay(TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);
                    }
                    count++;
                    await next.WriteAsync(item, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fault = ex;
            }
            finally { next.Complete(fault); }
        }, ct);

        return new Pipeline<T>(next);
    }

    /// <summary>Sends all items to <paramref name="sink"/>, propagating any upstream pipeline fault.</summary>
    public Task DrainTo(Chan<T> sink, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            await foreach (var item in _source.ReadAllAsync(ct).ConfigureAwait(false))
                await sink.WriteAsync(item, ct).ConfigureAwait(false);
            await _source.Completion.ConfigureAwait(false);
        }, ct);

    /// <summary>Consumes all items with <paramref name="sink"/>, propagating any upstream pipeline fault.</summary>
    public Task DrainAsync(Func<T, CancellationToken, ValueTask> sink, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            await foreach (var item in _source.ReadAllAsync(ct).ConfigureAwait(false))
                await sink(item, ct).ConfigureAwait(false);
            await _source.Completion.ConfigureAwait(false);
        }, ct);

    /// <summary>Exposes the underlying channel for further composition.</summary>
    public Chan<T> AsChannel() => _source;

    private static async ValueTask<TOut> ExecuteWithRetry<TOut>(
        Func<T, CancellationToken, ValueTask<TOut>> transform,
        T item,
        RetryPolicy policy,
        CancellationToken ct)
    {
        var delay = policy.BaseDelay;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await transform(item, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt < policy.MaxAttempts &&
                !ct.IsCancellationRequested &&
                (policy.ShouldRetry?.Invoke(ex) ?? true))
            {
                policy.OnRetry?.Invoke(ex, attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(
                    delay.TotalMilliseconds * Math.Max(1.0, policy.BackoffMultiplier));
            }
        }
    }
}
