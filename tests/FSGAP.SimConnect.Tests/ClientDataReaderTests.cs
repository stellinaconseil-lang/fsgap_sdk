using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

public sealed class ClientDataReaderTests
{
    [Fact]
    public async Task NoConnectionMeansNoSubscriptionAndNoImplicitConnect()
    {
        await using var h = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Simulator.ObserveClientDataAsync("FSGAP_TEST_AREA", 64, _ => { }));
        Assert.Equal(0, h.Factory.Attempts);
    }

    [Fact]
    public async Task ExistingSessionOwnsSubscriptionAndDisconnectCancelsIt()
    {
        await using var h = new Harness();
        var native = h.Factory.SimulatorPresent(Identity.Of("Test"));
        Action<byte[]>? callback = null;
        native.ClientDataObservation = async (name, length, received, ct) =>
        {
            Assert.Equal("FSGAP_TEST_AREA", name); Assert.Equal(64, length);
            callback = received;
            await Task.Delay(Timeout.Infinite, ct);
        };
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        var receivedCount = 0;
        var observation = h.Simulator.ObserveClientDataAsync("FSGAP_TEST_AREA", 64, _ => Interlocked.Increment(ref receivedCount));
        await Eventually.TrueAsync(() => callback is not null, "subscription forwarding");
        callback!(new byte[64]);
        Assert.Equal(1, receivedCount);
        native.Drop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation.WaitAsync(Eventually.Timeout));
        callback(new byte[64]); // delayed callback from obsolete native generation
        Assert.Equal(1, receivedCount);
        Assert.Equal(1, h.Factory.Attempts);
        Assert.Single(h.Factory.Sessions);
    }

    [Theory]
    [InlineData(0)] [InlineData(8193)]
    public async Task BoundsAreRejectedBeforeNativeCalls(int length)
    {
        await using var h = new Harness();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Simulator.ObserveClientDataAsync("name", length, _ => { }));
        Assert.Equal(0, h.Factory.Attempts);
    }
}
