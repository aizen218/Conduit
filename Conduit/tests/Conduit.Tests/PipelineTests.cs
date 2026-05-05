namespace Conduit.Tests;

public class PipelineTests
{
    [Fact]
    public async Task Transform_MapsAllItems()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 1; i <= 5; i++) source.TryWrite(i);
        source.Complete();

        var results = new List<string>();
        await Flow.Pipeline(source)
            .Transform<string>(async (x, ct) => $"item-{x}")
            .DrainAsync(async (s, ct) => results.Add(s));

        results.Should().HaveCount(5);
        results.Should().Contain("item-1", "item-3", "item-5");
    }

    [Fact]
    public async Task Filter_DropsNonMatchingItems()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 10; i++) source.TryWrite(i);
        source.Complete();

        var results = new List<int>();
        await Flow.Pipeline(source)
            .Filter((x, ct) => ValueTask.FromResult(x % 2 == 0))
            .DrainAsync(async (x, ct) => results.Add(x));

        results.Should().Equal(0, 2, 4, 6, 8);
    }

    [Fact]
    public async Task Batch_GroupsItems()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 9; i++) source.TryWrite(i);
        source.Complete();

        var batches = new List<int[]>();
        await Flow.Pipeline(source)
            .Batch(batchSize: 3, flushTimeout: TimeSpan.FromMilliseconds(100))
            .DrainAsync(async (b, ct) => batches.Add(b));

        batches.Should().HaveCount(3);
        batches.SelectMany(b => b).Should().HaveCount(9);
    }

    [Fact]
    public async Task ParallelTransform_ProcessesAllItems()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 100; i++) source.TryWrite(i);
        source.Complete();

        var results = Chan.Unbounded<int>();
        await Flow.Pipeline(source)
            .ParallelTransform<int>(async (x, ct) => x * x, workers: 4)
            .DrainTo(results);
        results.Complete();

        var all = new List<int>();
        await foreach (var x in results.ReadAllAsync())
            all.Add(x);

        all.Should().HaveCount(100);
        all.Should().Contain(0).And.Contain(9801); // 0^2 and 99^2
    }

    [Fact]
    public async Task Chained_TransformFilter_Works()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 10; i++) source.TryWrite(i);
        source.Complete();

        var results = new List<string>();
        await Flow.Pipeline(source)
            .Filter((x, ct) => ValueTask.FromResult(x % 2 == 0))
            .Transform<string>(async (x, ct) => $"even-{x}")
            .DrainAsync(async (s, ct) => results.Add(s));

        results.Should().HaveCount(5);
        results.Should().AllSatisfy(s => s.Should().StartWith("even-"));
    }
}
