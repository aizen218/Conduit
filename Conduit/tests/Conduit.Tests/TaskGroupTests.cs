namespace Conduit.Tests;

public class TaskGroupTests
{
    [Fact]
    public async Task WaitAsync_WaitsForAllTasks()
    {
        var counter = 0;
        var group = new TaskGroup();

        for (int i = 0; i < 5; i++)
        {
            group.Spawn(async ct =>
            {
                await Task.Delay(10, ct);
                Interlocked.Increment(ref counter);
            });
        }

        await group.WaitAsync();
        counter.Should().Be(5);
    }

    [Fact]
    public async Task WaitSafeAsync_DoesNotThrow_OnCancellation()
    {
        using var cts = new CancellationTokenSource();
        var group = new TaskGroup();

        group.Spawn(async ct =>
        {
            await Task.Delay(10_000, ct);
        }, cts.Token);

        cts.Cancel();

        var act = async () => await group.WaitSafeAsync();
        await act.Should().NotThrowAsync();
    }
}
