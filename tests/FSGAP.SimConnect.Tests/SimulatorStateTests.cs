using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

public class SimulatorStateTests
{
    [Fact]
    public async Task Pause_is_unavailable_until_connected_then_unknown_until_first_notification()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        Assert.Equal(ValueState.Unavailable, h.Simulator.State.Current.Paused.State);

        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        Assert.Equal(ValueState.Unknown, h.Simulator.State.Current.Paused.State);
        Assert.Contains(SimulatorSystemEvent.Pause, session.Subscriptions);
        Assert.Contains(SimulatorSystemEvent.Crashed, session.Subscriptions);
    }

    [Fact]
    public async Task Pause_notifications_update_the_state()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        session.RaisePause(true);
        Assert.True(h.Simulator.State.Current.Paused.Value);
        Assert.Equal(TestClock.Start, h.Simulator.State.Current.Paused.ObservedAt);

        var before = h.Simulator.State.Current;
        session.RaisePause(true); // repeated notification: no change
        Assert.Same(before, h.Simulator.State.Current);

        session.RaisePause(false);
        Assert.False(h.Simulator.State.Current.Paused.Value);
    }

    [Fact]
    public async Task Failed_pause_subscription_leaves_pause_unavailable_but_stays_connected()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.FailingSubscriptions.Add(SimulatorSystemEvent.Pause);

        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        Assert.Equal(ValueState.Unavailable, h.Simulator.State.Current.Paused.State);
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("Pause", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Crashes_are_counted_and_timestamped()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        session.RaiseCrash();
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        session.RaiseCrash();

        var state = h.Simulator.State.Current;
        Assert.Equal(2, state.CrashCount);
        Assert.Equal(TestClock.Start.AddSeconds(30), state.LastCrashAt);
    }

    [Fact]
    public async Task Crash_count_survives_reconnection_and_resets_with_the_session()
    {
        await using var h = new Harness();
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        var second = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        first.RaiseCrash();

        first.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        Assert.Equal(ValueState.Unavailable, h.Simulator.State.Current.Paused.State);
        h.Clock.Advance(h.RetryDelay);
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        second.RaiseCrash();
        Assert.Equal(2, h.Simulator.State.Current.CrashCount);

        await h.Simulator.StopAsync();
        Assert.Equal(SimulatorState.Initial, h.Simulator.State.Current);
    }

    [Fact]
    public async Task A_slow_consumer_still_sees_every_crash_through_the_count()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        await using var slow = h.Simulator.State.WatchAsync().GetAsyncEnumerator();
        Assert.True(await slow.MoveNextAsync()); // then stops reading for a while

        session.RaiseCrash();
        session.RaisePause(true);
        session.RaiseCrash();
        session.RaisePause(false);

        Assert.True(await slow.MoveNextAsync().AsTask().WaitAsync(Eventually.Timeout));
        Assert.Equal(2, slow.Current.CrashCount);
    }

    [Fact]
    public async Task Callbacks_from_a_closed_connection_are_ignored()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        session.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);

        // Late native callbacks for the dead connection (the fake still lets us raise them).
        session.RaiseCrashIgnoringUnsubscription();
        session.RaisePauseIgnoringUnsubscription(true);

        Assert.Equal(0, h.Simulator.State.Current.CrashCount);
        Assert.Equal(ValueState.Unavailable, h.Simulator.State.Current.Paused.State);
    }

    [Fact]
    public async Task Session_clock_is_monotonic_across_reconnections_and_resets_on_stop()
    {
        await using var h = new Harness();
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        Assert.Equal(TimeSpan.Zero, h.Simulator.SessionElapsed);

        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        h.Clock.Advance(TimeSpan.FromSeconds(3));
        var beforeLoss = h.Simulator.SessionElapsed;
        first.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        h.Clock.Advance(h.RetryDelay);
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        var afterReconnect = h.Simulator.SessionElapsed;

        Assert.Equal(TimeSpan.FromSeconds(3), beforeLoss);
        Assert.Equal(TimeSpan.FromSeconds(3) + h.RetryDelay, afterReconnect);

        await h.Simulator.StopAsync();
        Assert.Equal(TimeSpan.Zero, h.Simulator.SessionElapsed);
        await h.Simulator.StartAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), h.Simulator.SessionElapsed);
    }
}
