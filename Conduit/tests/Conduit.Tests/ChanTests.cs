namespace Conduit.Tests;

public class ChanTests
{
    [Fact]
    public async Task Bounded_WriteRead_RoundTrips()
    {
        var ch = Chan.Bounded<int>(4);
        await ch.WriteAsync(42);
        var result = await ch.ReadAsync();
        result.Should().Be(42);
    }

    [Fact]
    public async Task Unbounded_WriteRead_RoundTrips()
    {
        var ch = Chan.Unbounded<string>();
        await ch.WriteAsync("hello");
        var result = await ch.ReadAsync();
        result.Should().Be("hello");
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsAllItems()
    {
        var ch = Chan.Unbounded<int>();
        for (int i = 0; i < 5; i++) ch.TryWrite(i);
        ch.Complete();

        var items = new List<int>();
        await foreach (var item in ch.ReadAllAsync())
            items.Add(item);

        items.Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public async Task Complete_StopsReaders()
    {
        var ch = Chan.Bounded<int>(4);
        ch.Complete();
        var canRead = await ch.WaitToReadAsync();
        canRead.Should().BeFalse();
    }
}
