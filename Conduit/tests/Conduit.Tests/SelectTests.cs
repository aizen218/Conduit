namespace Conduit.Tests;

public class SelectTests
{
    [Fact]
    public async Task ReadAsync_ReturnsFirstAvailable()
    {
        var ch1 = Chan.Unbounded<int>();
        var ch2 = Chan.Unbounded<int>();

        ch2.TryWrite(42);

        var (value, index) = await Select.ReadAsync(CancellationToken.None, ch1, ch2);

        value.Should().Be(42);
        index.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_TimesOut_WhenNoData()
    {
        var ch = Chan.Bounded<int>(1);

        var result = await Select.ReadAsync<int>(
            timeout: TimeSpan.FromMilliseconds(50),
            ct: CancellationToken.None,
            channels: ch);

        result.IsTimeout.Should().BeTrue();
        result.Index.Should().Be(-1);
    }

    [Fact]
    public async Task ReadAsync_Respects_CancellationToken()
    {
        var ch = Chan.Bounded<int>(1);
        using var cts = new CancellationTokenSource(50);

        var act = async () => await Select.ReadAsync(cts.Token, ch);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
