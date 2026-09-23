using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

public class StopAndIdempotenceTests
{
    [Fact]
    public async Task Stop_while_waiting_cancels_the_retries()
    {
        await using var h = new Harness();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);

        await h.Simulator.StopAsync();
        h.Clock.Advance(TimeSpan.FromHours(1));
        await Eventually.SettleAsync();

        Assert.Equal(SimulatorConnectionState.Disconnected, h.Simulator.Status.State);
        Assert.Equal(1, h.Factory.Attempts);
    }

    [Fact]
    public async Task Stop_while_connecting_cancels_the_pending_open()
    {
        await using var h = new Harness();
        h.Factory.Hangs();
        await h.Simulator.StartAsync();
        await Eventually.TrueAsync(() => h.Factory.Attempts == 1, "the open attempt");

        await h.Simulator.StopAsync().WaitAsync(Eventually.Timeout);

        Assert.Equal(SimulatorConnectionState.Disconnected, h.Simulator.Status.State);
    }

    [Fact]
    public async Task Stop_while_connected_releases_the_connection()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        await h.Simulator.StopAsync();

        Assert.Equal(1, session.DisposeCount);
        Assert.False(session.HasHandlers);
        Assert.Equal(SimulatorConnectionState.Disconnected, h.Simulator.Status.State);
        Assert.Null(h.Simulator.AircraftDetector.Current);
    }

    [Fact]
    public async Task Stop_while_reconnecting_stops_everything()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        session.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        var attempts = h.Factory.Attempts;

        await h.Simulator.StopAsync();
        h.Clock.Advance(TimeSpan.FromHours(1));
        await Eventually.SettleAsync();

        Assert.Equal(attempts, h.Factory.Attempts);
        Assert.Equal(SimulatorConnectionState.Disconnected, h.Simulator.Status.State);
    }

    [Fact]
    public async Task Start_twice_runs_a_single_connection_loop()
    {
        await using var h = new Harness();

        await h.Simulator.StartAsync();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);
        await Eventually.SettleAsync();

        Assert.Equal(1, h.Factory.Attempts);
        Assert.Equal(1, h.Clock.TimersCreated); // a single retry timer
    }

    [Fact]
    public async Task Stop_is_safe_when_never_started_and_when_repeated()
    {
        await using var h = new Harness();

        await h.Simulator.StopAsync();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);
        await h.Simulator.StopAsync();
        await h.Simulator.StopAsync();

        Assert.Equal(SimulatorConnectionState.Disconnected, h.Simulator.Status.State);
    }

    [Fact]
    public async Task Start_after_stop_opens_a_new_session()
    {
        await using var h = new Harness();
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        var second = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        await h.Simulator.StopAsync();

        await h.Simulator.StartAsync();

        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
    }

    [Fact]
    public async Task Disposed_transport_refuses_to_start_and_completes_its_streams()
    {
        var h = new Harness();
        await h.Simulator.StartAsync();
        var watch = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in h.Simulator.WatchStatusAsync())
            {
                count++;
            }

            return count;
        });
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);

        await h.Simulator.DisposeAsync();
        await h.Simulator.DisposeAsync();
        await h.Simulator.StopAsync();

        Assert.True(await watch.WaitAsync(Eventually.Timeout) > 0);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Simulator.StartAsync());
    }
}
