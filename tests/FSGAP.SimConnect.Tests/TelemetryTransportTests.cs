using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>Telemetry through the real transport loop, over the fake native layer and the fake clock.</summary>
public class TelemetryTransportTests
{
    private static readonly FastGroupVars Cruise = new() { IndicatedAirspeedKnots = 280.0, AltitudeFeet = 35000.0, VerticalSpeedFeetPerSecond = 0.0 };

    [Fact]
    public async Task Groups_are_read_on_the_single_native_session_and_published()
    {
        await using var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner", liveryFolder: "livery-a"));
        session.SetGroup(Cruise);
        session.SetGroup(new NormalGroupVars { GearHandlePercent = 0.0, FlapsHandlePercent = 0.0 });
        session.SetGroup(new SlowGroupVars { Engine1Combustion = 1.0, Engine1N1Percent = 84.0 });

        await h.Simulator.StartAsync();
        await AdvanceUntilAsync(h, t => t.Flight.IndicatedAirspeedKnots.IsKnown && t.Engines.Count == 2 && t.LandingGear.HandleDown.IsKnown);

        var snapshot = await h.Simulator.Telemetry.GetSnapshotAsync();
        Assert.Equal(280.0, snapshot.Flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(84.0, snapshot.Engines[0].N1Percent.Value);
        Assert.False(snapshot.LandingGear.HandleDown.Value);
        Assert.Equal(1, h.Factory.Attempts);
        Assert.Single(h.Factory.Sessions);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
    }

    [Fact]
    public async Task Fast_group_is_read_about_once_per_second_and_slow_group_about_every_five()
    {
        await using var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        await AdvanceUntilAsync(h, _ => session.GroupReads<FastGroupVars>() > 0);
        var fastBefore = session.GroupReads<FastGroupVars>();
        var slowBefore = session.GroupReads<SlowGroupVars>();

        for (var i = 0; i < 10; i++)
        {
            await StepAsync(h, TimeSpan.FromSeconds(1));
        }

        Assert.InRange(session.GroupReads<FastGroupVars>() - fastBefore, 9, 11);
        Assert.InRange(session.GroupReads<SlowGroupVars>() - slowBefore, 1, 3);
    }

    [Fact]
    public async Task Environment_group_is_read_about_every_ten_seconds_and_published()
    {
        await using var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.SetGroup(new EnvironmentGroupVars { OutsideAirTemperatureCelsius = -40.0, WindDirectionDegreesTrue = 250.0, WindSpeedKnots = 60.0, PrecipitationMask = 8.0 });
        await h.Simulator.StartAsync();
        await AdvanceUntilAsync(h, t => t.Environment.Precipitation.IsKnown);
        var before = session.GroupReads<EnvironmentGroupVars>();

        for (var i = 0; i < 20; i++)
        {
            await StepAsync(h, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(TimeSpan.FromSeconds(10), SimConnectSimulator.EnvironmentGroupInterval);
        Assert.InRange(session.GroupReads<EnvironmentGroupVars>() - before, 1, 3);
        var snapshot = await h.Simulator.Telemetry.GetSnapshotAsync();
        Assert.Equal(-40.0, snapshot.Environment.OutsideAirTemperatureCelsius.Value);
        Assert.Equal(PrecipitationType.Snow, snapshot.Environment.Precipitation.Value);
        Assert.Single(h.Factory.Sessions);
    }

    [Fact]
    public async Task A_failing_group_is_isolated_and_its_values_expire_while_the_others_continue()
    {
        await using var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.SetGroup(Cruise);
        session.SetGroup(new SlowGroupVars { Engine1N1Percent = 84.0 });
        await h.Simulator.StartAsync();
        await AdvanceUntilAsync(h, t => t.Flight.IndicatedAirspeedKnots.IsKnown && t.Engines.Count == 2);

        session.GroupFailures[typeof(SlowGroupVars)] = new InvalidOperationException("unrecognized SimVar");
        await AdvanceUntilAsync(h, t => t.Engines[0].N1Percent.State == ValueState.Unknown, maxSteps: 40);

        var snapshot = await h.Simulator.Telemetry.GetSnapshotAsync();
        Assert.True(snapshot.Flight.IndicatedAirspeedKnots.IsKnown);
        Assert.Equal(SimulatorConnectionState.Connected, h.Simulator.Status.State);
        Assert.Single(h.Factory.Sessions);
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("telemetry group Slow", StringComparison.Ordinal));

        session.GroupFailures.TryRemove(typeof(SlowGroupVars), out _);
        await AdvanceUntilAsync(h, t => t.Engines[0].N1Percent.IsKnown, maxSteps: 10);
    }

    [Fact]
    public async Task Nothing_is_read_while_no_aircraft_is_loaded()
    {
        await using var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of(null));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        for (var i = 0; i < 5; i++)
        {
            await StepAsync(h, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(0, session.GroupReads<FastGroupVars>());
        Assert.Equal(ValueState.Unavailable, (await h.Simulator.Telemetry.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.State);
    }

    [Fact]
    public async Task An_aircraft_change_drops_the_previous_aircraft_telemetry()
    {
        await using var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of("Aircraft A"));
        session.SetGroup(new SlowGroupVars { Engine1N1Percent = 84.0 });
        session.SetGroup(Cruise);
        await h.Simulator.StartAsync();
        await AdvanceUntilAsync(h, t => t.Engines.Count == 2);
        using var cts = new CancellationTokenSource(Eventually.Timeout);
        var cleared = WaitForStreamAsync(h, t => t.Engines.Count == 0, cts.Token);

        session.Identity = Identity.Of("Aircraft B");
        session.SetGroup(new SlowGroupVars { Engine1N1Percent = 20.0 });
        await AdvanceUntilAsync(h, _ => cleared.IsCompleted, maxSteps: 20);
        await cleared;

        // Aircraft B's own values arrive with its own reads, never the leftovers of A.
        await AdvanceUntilAsync(h, t => t.Engines.Count == 2, maxSteps: 20);
        Assert.Equal(20.0, (await h.Simulator.Telemetry.GetSnapshotAsync()).Engines[0].N1Percent.Value);
    }

    [Fact]
    public async Task After_a_disconnection_the_values_turn_unknown_once_stale_even_without_polling()
    {
        var staleAfter = TimeSpan.FromSeconds(10);
        await using var h = new Harness(pollTelemetry: true, staleAfter: staleAfter);
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.SetGroup(Cruise);
        await h.Simulator.StartAsync();
        await AdvanceUntilAsync(h, t => t.Flight.IndicatedAirspeedKnots.IsKnown);
        using var cts = new CancellationTokenSource(Eventually.Timeout);
        var expired = WaitForStreamAsync(h, t => t.Flight.IndicatedAirspeedKnots.State == ValueState.Unknown, cts.Token);

        session.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        Assert.True((await h.Simulator.Telemetry.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.IsKnown);

        await AdvanceUntilAsync(h, _ => expired.IsCompleted, maxSteps: 20);
        await expired;
        Assert.Single(h.Factory.Sessions);
    }

    [Fact]
    public async Task Stop_resets_telemetry_and_dispose_ends_its_streams()
    {
        var h = new Harness(pollTelemetry: true);
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.SetGroup(Cruise);
        await h.Simulator.StartAsync();
        await AdvanceUntilAsync(h, t => t.Flight.IndicatedAirspeedKnots.IsKnown);

        await h.Simulator.StopAsync();
        Assert.Equal(ValueState.Unavailable, (await h.Simulator.Telemetry.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.State);

        await using var stream = h.Simulator.Telemetry.StreamAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        await h.DisposeAsync();
        Assert.False(await stream.MoveNextAsync().AsTask().WaitAsync(Eventually.Timeout));
    }

    private static async Task StepAsync(Harness h, TimeSpan step)
    {
        h.Clock.Advance(step);
        await Task.Delay(10);
    }

    /// <summary>Advances simulated time a second at a time until the telemetry satisfies the condition.</summary>
    private static async Task AdvanceUntilAsync(Harness h, Func<AircraftTelemetry, bool> condition, int maxSteps = 15)
    {
        for (var i = 0; i <= maxSteps; i++)
        {
            // Let the background loops reach their next delay before moving time.
            var deadline = DateTime.UtcNow.AddMilliseconds(200);
            while (DateTime.UtcNow < deadline)
            {
                if (condition(await h.Simulator.Telemetry.GetSnapshotAsync()))
                {
                    return;
                }

                await Task.Delay(5);
            }

            h.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("Telemetry never reached the expected state.");
    }

    private static Task WaitForStreamAsync(Harness h, Func<AircraftTelemetry, bool> condition, CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource();
        var task = Task.Run(async () =>
        {
            await foreach (var t in h.Simulator.Telemetry.StreamAsync(new TelemetryStreamOptions { Interval = TimeSpan.FromTicks(1) }, cancellationToken))
            {
                started.TrySetResult();
                if (condition(t))
                {
                    return;
                }
            }
        });
        started.Task.Wait(Eventually.Timeout);
        return task;
    }
}
