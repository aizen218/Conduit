namespace Conduit;

public static class Select
{
    /// <summary>
    /// Reads from whichever channel has data first.
    /// Returns the value and the index of the winning channel.
    /// </summary>
    public static async ValueTask<SelectResult<T>> ReadAsync<T>(
        CancellationToken ct,
        params Chan<T>[] channels)
    {
        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i].TryRead(out var item))
                    return new SelectResult<T>(item!, i);
            }

            var waits = new Task[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                var idx = i;
                waits[idx] = channels[idx].WaitToReadAsync(ct).AsTask()
                    .ContinueWith(_ => idx, TaskScheduler.Default);
            }

            await Task.WhenAny(waits).ConfigureAwait(false);

            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i].TryRead(out var item))
                    return new SelectResult<T>(item!, i);
            }
        }

        ct.ThrowIfCancellationRequested();
        return default;
    }

    /// <summary>
    /// Reads from whichever channel has data first, with a timeout fallback.
    /// Returns null value and index -1 on timeout.
    /// </summary>
    public static async ValueTask<SelectResult<T>> ReadAsync<T>(
        TimeSpan timeout,
        CancellationToken ct,
        params Chan<T>[] channels)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await ReadAsync<T>(timeoutCts.Token, channels).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SelectResult<T>.Timeout;
        }
    }
}

public readonly struct SelectResult<T>
{
    public T Value { get; }
    public int Index { get; }
    public bool IsTimeout => Index == -1;

    public SelectResult(T value, int index)
    {
        Value = value;
        Index = index;
    }

    public static SelectResult<T> Timeout => new(default!, -1);

    public void Deconstruct(out T value, out int index)
    {
        value = Value;
        index = Index;
    }
}
