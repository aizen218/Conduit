namespace Conduit;

/// <summary>Tracks a group of spawned tasks and waits for all to complete — equivalent to Go's sync.WaitGroup.</summary>
public sealed class TaskGroup
{
    private readonly List<Task> _tasks = [];
    private readonly object _lock = new();

    public void Spawn(Func<CancellationToken, ValueTask> work, CancellationToken ct = default)
    {
        var task = Task.Run(async () => await work(ct).ConfigureAwait(false), ct);
        lock (_lock) _tasks.Add(task);
    }

    public void Spawn(Func<ValueTask> work, CancellationToken ct = default)
    {
        var task = Task.Run(async () => await work().ConfigureAwait(false), ct);
        lock (_lock) _tasks.Add(task);
    }

    /// <summary>Waits for all spawned tasks to complete. Propagates the first exception.</summary>
    public Task WaitAsync(CancellationToken ct = default)
    {
        Task[] snapshot;
        lock (_lock) snapshot = [.. _tasks];
        return Task.WhenAll(snapshot).WaitAsync(ct);
    }

    /// <summary>Waits, swallowing cancellation exceptions from individual tasks.</summary>
    public async Task WaitSafeAsync(CancellationToken ct = default)
    {
        Task[] snapshot;
        lock (_lock) snapshot = [.. _tasks];

        try
        {
            await Task.WhenAll(snapshot).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
}
