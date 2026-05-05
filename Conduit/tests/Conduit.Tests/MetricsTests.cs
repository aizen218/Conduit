namespace Conduit.Tests;

public class MetricsTests
{
    [Fact]
    public async Task BoundedWithMetrics_TracksWrites()
    {
        var ch = Chan.BoundedWithMetrics<int>(10, out var metrics);
        await ch.WriteAsync(1);
        await ch.WriteAsync(2);
        await ch.WriteAsync(3);

        metrics.TotalWritten.Should().Be(3);
        metrics.BufferDepth.Should().Be(3);
    }

    [Fact]
    public async Task BoundedWithMetrics_TracksReads()
    {
        var ch = Chan.BoundedWithMetrics<int>(10, out var metrics);
        await ch.WriteAsync(1);
        await ch.WriteAsync(2);
        await ch.ReadAsync();

        metrics.TotalRead.Should().Be(1);
        metrics.BufferDepth.Should().Be(1);
    }

    [Fact]
    public async Task BoundedWithMetrics_TracksPeakDepth()
    {
        var ch = Chan.BoundedWithMetrics<int>(20, out var metrics);
        for (int i = 0; i < 10; i++) await ch.WriteAsync(i);

        metrics.PeakBufferDepth.Should().Be(10);

        await ch.ReadAsync();
        await ch.ReadAsync();

        metrics.BufferDepth.Should().Be(8);
        metrics.PeakBufferDepth.Should().Be(10); // peak doesn't drop
    }

    [Fact]
    public void TryWrite_OnFullChannel_RecordsDrop()
    {
        // In Wait mode, TryWrite returns false (can't write sync when full)
        // so it counts as a drop in our metrics
        var ch = Chan.BoundedWithMetrics<int>(2, out var metrics, BoundedChannelFullMode.Wait);
        ch.TryWrite(1);
        ch.TryWrite(2);
        ch.TryWrite(3); // channel full → TryWrite returns false

        metrics.TotalWritten.Should().Be(2);
        metrics.TotalDropped.Should().Be(1);
    }

    [Fact]
    public async Task UnboundedWithMetrics_TracksReadAllAsync()
    {
        var ch = Chan.UnboundedWithMetrics<int>(out var metrics);
        for (int i = 0; i < 5; i++) ch.TryWrite(i);
        ch.Complete();

        var count = 0;
        await foreach (var _ in ch.ReadAllAsync())
            count++;

        count.Should().Be(5);
        metrics.TotalRead.Should().Be(5);
        metrics.BufferDepth.Should().Be(0);
    }
}
