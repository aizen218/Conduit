namespace Conduit;

public static class Flow
{
    /// <summary>Spawns a background task — fire-and-forget with structured error propagation.</summary>
    public static Task Spawn(Func<CancellationToken, ValueTask> work, CancellationToken ct = default) =>
        Task.Run(async () => await work(ct).ConfigureAwait(false), ct);

    public static Task Spawn(Func<ValueTask> work, CancellationToken ct = default) =>
        Task.Run(async () => await work().ConfigureAwait(false), ct);

    /// <summary>Fans out N workers consuming from <paramref name="source"/>.</summary>
    public static async Task WorkerPool<T>(
        int workers,
        Chan<T> source,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken ct = default)
    {
        var tasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await foreach (var item in source.ReadAllAsync(ct).ConfigureAwait(false))
                    await handler(item, ct).ConfigureAwait(false);
            }, ct);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>Merges multiple source channels into one output channel (fan-in).</summary>
    public static Chan<T> Merge<T>(CancellationToken ct = default, params Chan<T>[] sources)
    {
        var output = Chan.Unbounded<T>();
        var tasks = sources.Select(src => Task.Run(async () =>
        {
            await foreach (var item in src.ReadAllAsync(ct).ConfigureAwait(false))
                await output.WriteAsync(item, ct).ConfigureAwait(false);
        }, ct)).ToArray();

        _ = Task.WhenAll(tasks).ContinueWith(_ => output.Complete(), TaskScheduler.Default);
        return output;
    }

    /// <summary>Starts a typed pipeline from a source channel.</summary>
    public static Pipeline<T> Pipeline<T>(Chan<T> source) => new(source);
}
