using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

public class ConnectionLifecycleTests
{
    [Fact]
    public async Task Start_goes_through_connecting_to_connected_when_the_simulator_runs()
    {
        await using var h = new Harness();
        var open = new TaskCompletionSource();
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"), open.Task);
        Assert.Equal(SimulatorConnectionState.Disconnected, h.Simulator.Status.State);

        await h.Simulator.StartAsync();

        Assert.Equal(SimulatorConnectionState.Connecting, h.Simulator.Status.State);
        open.SetResult();
        var connected = await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Null(connected.Detail);
        Assert.Equal("FsgapTests", h.Factory.LastApplicationName);
    }

    [Fact]
    public async Task Start_waits_for_the_simulator_when_it_is_absent()
    {
        await using var h = new Harness();

        await h.Simulator.StartAsync();

        var status = await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);
        Assert.Equal(SimConnectSimulator.SimulatorNotRunning, status.Detail);
        Assert.Equal(1, h.Factory.Attempts);
    }

    [Fact]
    public async Task Waiting_connects_as_soon_as_the_simulator_appears()
    {
        await using var h = new Harness();
        h.Factory.SimulatorAbsent().SimulatorAbsent();
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));

        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);
        h.Clock.Advance(h.RetryDelay); // attempt 2: still absent
        await h.ElapseRetryAsync(timersBefore: 1); // attempt 3: present

        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Equal(3, h.Factory.Attempts);
    }

    [Fact]
    public async Task Lost_connection_reconnects_and_stays_reconnecting_while_the_simulator_is_away()
    {
        await using var h = new Harness();
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        h.Factory.SimulatorAbsent();
        var second = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        first.Drop(); // MSFS closed
        var lost = await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        Assert.Equal(SimConnectSimulator.ConnectionLost, lost.Detail);
        var timers = h.Clock.TimersCreated;
        h.Clock.Advance(h.RetryDelay); // attempt: MSFS still closed
        await h.Clock.WaitForTimersAsync(timers + 1);
        Assert.Equal(SimulatorConnectionState.Reconnecting, h.Simulator.Status.State);
        h.Clock.Advance(h.RetryDelay); // MSFS relaunched

        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public async Task Failed_read_on_a_closed_connection_counts_as_a_loss()
    {
        // Safety net for the case where the library does not raise its disconnection event.
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        await h.Clock.WaitForTimersAsync(1);

        session.Connected = false;
        session.IdentityFailure = new InvalidOperationException("pipe closed");
        h.Clock.Advance(SimConnectSimulator.IdentityFastInterval);

        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task Unexpected_connection_errors_are_retried_not_terminal()
    {
        await using var h = new Harness();
        h.Factory.Throws(new InvalidOperationException("transient native error"));
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));

        await h.Simulator.StartAsync();
        var waiting = await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);
        Assert.Equal("transient native error", waiting.Detail);
        h.Clock.Advance(h.RetryDelay);

        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("Unexpected error", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(DllNotFoundException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(EntryPointNotFoundException))]
    public async Task Missing_native_library_is_terminal_until_restarted(Type exceptionType)
    {
        await using var h = new Harness();
        h.Factory.Throws((Exception)Activator.CreateInstance(exceptionType, "SimConnect.dll")!);

        await h.Simulator.StartAsync();
        var faulted = await h.WaitForAsync(SimulatorConnectionState.Faulted);
        Assert.Equal("SimConnect.dll", faulted.Detail);
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Simulator.StartAsync(); // no effect while faulted
        await Eventually.SettleAsync();
        Assert.Equal(1, h.Factory.Attempts);

        await h.Simulator.StopAsync();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);
        Assert.Equal(2, h.Factory.Attempts);
    }

    [Fact]
    public void Invalid_options_are_rejected_up_front()
    {
        var options = new Harness().Options with { ApplicationName = " " };

        Assert.ThrowsAny<ArgumentException>(() => new SimConnectSimulator(options));
    }
}
