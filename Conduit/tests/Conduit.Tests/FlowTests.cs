namespace Conduit.Tests;

public class FlowTests
{
    [Fact]
    public async Task Spawn_ExecutesWork()
    {
        var ch = Chan.Unbounded<int>();
        await Flow.Spawn(async ct =>
        {
            await ch.WriteAsync(99, ct);
            ch.Complete();
        });

        var result = await ch.ReadAsync();
        result.Should().Be(99);
    }

    [Fact]
    public async Task WorkerPool_ProcessesAllItems()
    {
        var source = Chan.Unbounded<int>();
        var results = Chan.Unbounded<int>();

        for (int i = 0; i < 20; i++) source.TryWrite(i);
        source.Complete();

        await Flow.WorkerPool(workers: 4, source, async (item, ct) =>
        {
            await results.WriteAsync(item * 2, ct);
        });
        results.Complete();

        var all = new List<int>();
        await foreach (var x in results.ReadAllAsync())
            all.Add(x);

        all.Should().HaveCount(20);
        all.Sum().Should().Be(Enumerable.Range(0, 20).Sum() * 2);
    }

    [Fact]
    public async Task Merge_CombinesAllChannels()
    {
        var ch1 = Chan.Unbounded<int>();
        var ch2 = Chan.Unbounded<int>();

        ch1.TryWrite(1); ch1.TryWrite(2); ch1.Complete();
        ch2.TryWrite(3); ch2.TryWrite(4); ch2.Complete();

        var merged = Flow.Merge<int>(default, ch1, ch2);

        var results = new List<int>();
        await foreach (var item in merged.ReadAllAsync())
            results.Add(item);

        results.Should().HaveCount(4);
        results.Should().Contain([1, 2, 3, 4]);
    }
}
