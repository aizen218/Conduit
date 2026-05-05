using System.Diagnostics;

namespace Conduit.Tests;

public class RateLimitTests
{
    [Fact]
    public async Task RateLimit_PassesAllItems()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 5; i++) source.TryWrite(i);
        source.Complete();

        var results = new List<int>();
        await Flow.Pipeline(source)
            .RateLimit(permitsPerSecond: 100) // fast limit, just verify items pass through
            .DrainAsync(async (x, ct) => results.Add(x));

        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task RateLimit_SlowsDownThroughput()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 6; i++) source.TryWrite(i);
        source.Complete();

        var sw = Stopwatch.StartNew();
        var results = new List<int>();

        await Flow.Pipeline(source)
            .RateLimit(permitsPerSecond: 10) // 10/sec → 6 items ≈ 500ms+
            .DrainAsync(async (x, ct) => results.Add(x));

        sw.Stop();
        results.Should().HaveCount(6);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(400);
    }

    [Fact]
    public async Task RateLimit_WorksInPipelineChain()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 4; i++) source.TryWrite(i);
        source.Complete();

        var results = new List<string>();
        await Flow.Pipeline(source)
            .RateLimit(permitsPerSecond: 50)
            .Transform<string>(async (x, ct) => $"item-{x}")
            .DrainAsync(async (s, ct) => results.Add(s));

        results.Should().HaveCount(4);
        results.Should().AllSatisfy(s => s.Should().StartWith("item-"));
    }

    [Fact]
    public async Task RateLimit_RespectsCancel()
    {
        var source = Chan.Unbounded<int>();
        for (int i = 0; i < 100; i++) source.TryWrite(i);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var results = new List<int>();
        var act = async () => await Flow.Pipeline(source)
            .RateLimit(permitsPerSecond: 5, ct: cts.Token)
            .DrainAsync(async (x, ct) => results.Add(x), cts.Token);

        // Cancellation surfaces either from RateLimit or DrainAsync
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
