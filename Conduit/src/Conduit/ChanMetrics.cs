namespace Conduit;

public sealed class ChanMetrics
{
    private long _written, _read, _dropped, _peakDepth;

    public long TotalWritten => Volatile.Read(ref _written);
    public long TotalRead => Volatile.Read(ref _read);
    public long TotalDropped => Volatile.Read(ref _dropped);
    public long PeakBufferDepth => Volatile.Read(ref _peakDepth);
    public long BufferDepth => Math.Max(0L, TotalWritten - TotalRead);

    internal void RecordWrite()
    {
        Interlocked.Increment(ref _written);
        UpdatePeak();
    }

    internal void RecordRead() => Interlocked.Increment(ref _read);
    internal void RecordDrop() => Interlocked.Increment(ref _dropped);

    private void UpdatePeak()
    {
        long depth = BufferDepth;
        long peak = Volatile.Read(ref _peakDepth);
        while (depth > peak)
        {
            long prev = Interlocked.CompareExchange(ref _peakDepth, depth, peak);
            if (prev == peak) break;
            peak = prev;
        }
    }
}
