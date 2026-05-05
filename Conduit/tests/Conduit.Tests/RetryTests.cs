namespace Conduit.Tests;

public class RetryTests
{
    [Fact]
    public async Task Transform_WithRetry_SucceedsAfterFailures()
    {
        var source = Chan.Unbounded<int>();
        source.TryWrite(42);
        source.Complete();

        var attempts = 0;
        var results = new List<int>();

        await Flow.Pipeline(source)
            .Transform<int>(async (x, ct) =>
            {
                if (Interlocked.Increment(ref attempts) < 3)
                    throw new InvalidOperationException("Simulated failure");
                return x;
            }, retry: RetryPolicy.Immediate(maxAttempts: 3))
            .DrainAsync(async (x, ct) => results.Add(x));

        attempts.Should().Be(3);
        results.Should().Equal(42);
    }

    [Fact]
    public async Task Transform_WithRetry_ThrowsAfterMaxAttempts()
    {
        var source = Chan.Unbounded<int>();
        source.TryWrite(1);
        source.Complete();

        var act = async () => await Flow.Pipeline(source)
            .Transform<int>(
                (x, ct) => throw new InvalidOperationException("always fail"),
                retry: RetryPolicy.Immediate(maxAttempts: 3))
            .DrainAsync(async (x, ct) => { });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Transform_WithRetry_InvokesOnRetryCallback()
    {
        var source = Chan.Unbounded<int>();
        source.TryWrite(99);
        source.Complete();

        var retryCalled = 0;
        var results = new List<int>();

        await Flow.Pipeline(source)
            .Transform<int>(async (x, ct) =>
            {
                if (Volatile.Read(ref retryCalled) < 2)
                    throw new InvalidOperationException();
                return x * 10;
            }, retry: RetryPolicy.Immediate(
                maxAttempts: 5,
                onRetry: (_, _) => Interlocked.Increment(ref retryCalled)))
            .DrainAsync(async (x, ct) => results.Add(x));

        retryCalled.Should().Be(2);
        results.Should().Equal(990);
    }

    [Fact]
    public async Task Transform_WithRetry_ShouldRetry_SkipsNonRetriableExceptions()
    {
        var source = Chan.Unbounded<int>();
        source.TryWrite(1);
        source.Complete();

        var attempts = 0;
        var act = async () => await Flow.Pipeline(source)
            .Transform<int>(async (x, ct) =>
            {
                Interlocked.Increment(ref attempts);
                throw new ArgumentException("not retriable");
            }, retry: RetryPolicy.Immediate(
                maxAttempts: 5,
                shouldRetry: ex => ex is not ArgumentException))
            .DrainAsync(async (x, ct) => { });

        await act.Should().ThrowAsync<ArgumentException>();
        attempts.Should().Be(1); // no retries for ArgumentException
    }

    [Fact]
    public async Task Transform_WithExponentialRetry_AppliesBackoff()
    {
        var source = Chan.Unbounded<int>();
        source.TryWrite(1);
        source.Complete();

        var attempts = 0;
        var results = new List<int>();

        await Flow.Pipeline(source)
            .Transform<int>(async (x, ct) =>
            {
                if (Interlocked.Increment(ref attempts) < 3)
                    throw new IOException("transient");
                return x;
            }, retry: RetryPolicy.Exponential(
                maxAttempts: 3,
                baseDelay: TimeSpan.FromMilliseconds(10),
                shouldRetry: ex => ex is IOException))
            .DrainAsync(async (x, ct) => results.Add(x));

        attempts.Should().Be(3);
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task Transform_NoRetry_PropagatesFirstFailure()
    {
        var source = Chan.Unbounded<int>();
        source.TryWrite(1);
        source.Complete();

        var act = async () => await Flow.Pipeline(source)
            .Transform<int>((x, ct) => throw new InvalidOperationException("boom"))
            .DrainAsync(async (x, ct) => { });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
