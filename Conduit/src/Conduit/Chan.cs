using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Conduit;

public static class Chan
{
    public static Chan<T> Bounded<T>(int capacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait) =>
        new(Channel.CreateBounded<T>(MakeBoundedOptions(capacity, fullMode)));

    public static Chan<T> BoundedWithMetrics<T>(int capacity, out ChanMetrics metrics,
        BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
    {
        metrics = new ChanMetrics();
        return new Chan<T>(Channel.CreateBounded<T>(MakeBoundedOptions(capacity, fullMode)), metrics);
    }

    public static Chan<T> Unbounded<T>() =>
        new(Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        }));

    public static Chan<T> UnboundedWithMetrics<T>(out ChanMetrics metrics)
    {
        metrics = new ChanMetrics();
        return new Chan<T>(Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        }), metrics);
    }

    private static BoundedChannelOptions MakeBoundedOptions(int capacity, BoundedChannelFullMode fullMode) =>
        new(capacity)
        {
            FullMode = fullMode,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };
}

public sealed class Chan<T>
{
    private readonly Channel<T> _inner;
    private readonly ChanMetrics? _metrics;

    internal Chan(Channel<T> inner, ChanMetrics? metrics = null)
    {
        _inner = inner;
        _metrics = metrics;
    }

    public ValueTask WriteAsync(T item, CancellationToken ct = default)
    {
        _metrics?.RecordWrite();
        return _inner.Writer.WriteAsync(item, ct);
    }

    public bool TryWrite(T item)
    {
        if (_inner.Writer.TryWrite(item))
        {
            _metrics?.RecordWrite();
            return true;
        }
        _metrics?.RecordDrop();
        return false;
    }

    public ValueTask<T> ReadAsync(CancellationToken ct = default)
    {
        if (_metrics is null) return _inner.Reader.ReadAsync(ct);
        return ReadAsyncTracked(ct);
    }

    public bool TryRead(out T? item)
    {
        if (_inner.Reader.TryRead(out item))
        {
            _metrics?.RecordRead();
            return true;
        }
        return false;
    }

    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct = default)
    {
        if (_metrics is null) return _inner.Reader.ReadAllAsync(ct);
        return ReadAllTracked(ct);
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct = default) =>
        _inner.Reader.WaitToReadAsync(ct);

    public ValueTask<bool> WaitToWriteAsync(CancellationToken ct = default) =>
        _inner.Writer.WaitToWriteAsync(ct);

    public void Complete(Exception? error = null) =>
        _inner.Writer.Complete(error);

    public Task Completion => _inner.Reader.Completion;

    public bool IsCompleted => _inner.Reader.Completion.IsCompleted;

    private async ValueTask<T> ReadAsyncTracked(CancellationToken ct)
    {
        var item = await _inner.Reader.ReadAsync(ct).ConfigureAwait(false);
        _metrics!.RecordRead();
        return item;
    }

    private async IAsyncEnumerable<T> ReadAllTracked([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in _inner.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            _metrics!.RecordRead();
            yield return item;
        }
    }
}
