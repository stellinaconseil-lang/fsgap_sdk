using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Variables;

namespace FSGAP.Fenix.Tests;

/// <summary>
/// The Fenix system polling as a session lives it: which aircraft, when it reads, what survives a change, a
/// disconnection, a dispose. Driven with a fake reader, a fake detector and a fake clock.
/// </summary>
public class FenixSystemLifecycleTests
{
    private static readonly AircraftDescriptor A319 = new() { Title = "FenixA319 CFM WF SD", LiveryFolder = "ACA-C-GBIA-E270", Registration = "C-GBIA" };
    private static readonly AircraftDescriptor A321 = new() { Title = "FenixA321 IAE WF SC", LiveryFolder = "AEE-SX-DNH-7F2F", Registration = "SX-DNH" };
    private static readonly AircraftDescriptor C172 = new() { Title = "Cessna Skyhawk G1000 Asobo" };

    private sealed class Rig
    {
        public Rig(AircraftDescriptor? loaded, TimeSpan? staleAfter = null)
        {
            Detector = new FakeDetector(loaded);
            Provider = new FenixAircraftProvider(
                timeProvider: Clock,
                genericTelemetry: new StampedGenericTelemetry(Clock),
                simulatorVariables: Reader,
                aircraftDetector: Detector,
                telemetryOptions: new TelemetryOptions { StaleAfter = staleAfter ?? TimeSpan.FromSeconds(15) });
        }

        public CountingClock Clock { get; } = new();

        public FakeVariableReader Reader { get; } = new FakeVariableReader().WithNominalCockpit();

        public FakeDetector Detector { get; }

        public FenixAircraftProvider Provider { get; }

        public int CockpitReads => Reader.ReadsOf(FenixVariables.Cockpit);

        public int HydraulicsReads => Reader.ReadsOf(FenixVariables.Hydraulics);

        /// <summary>Advances simulated time one second at a time, letting the loops run after each step.</summary>
        public async Task SecondsAsync(int seconds)
        {
            for (var i = 0; i < seconds; i++)
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(25);
            }
        }
    }

    [Fact]
    public async Task A_fenix_session_reads_its_systems_and_declares_only_what_it_feeds()
    {
        var rig = new Rig(A319);

        await using var session = await rig.Provider.AttachAsync(A319);
        await Ready(session, t => t.InertialReferences.Count == 3 && t.HydraulicSystems.Count == 3);
        var t = await session.Telemetry.GetSnapshotAsync();

        Assert.Equal([InertialReferenceMode.Navigation, InertialReferenceMode.Navigation, InertialReferenceMode.Navigation], t.InertialReferences.Select(i => i.Mode.Value));
        Assert.All(t.FuelPumps, p => Assert.True(p.IsOn.Value));
        Assert.All(t.Engines, e => Assert.False(e.FireHandlePulled.Value));
        Assert.False(t.Apu.FireHandlePulled.Value);
        Assert.Equal(3000.0, t.HydraulicSystems[0].PressurePsi.Value);

        var declared = session.Capabilities.Telemetry;
        Assert.True(declared.FlightState && declared.Engines && declared.InertialReferences && declared.FuelPumps && declared.Hydraulics && declared.Fire);
        Assert.False(declared.Apu);
        Assert.False(declared.Electrical);
        Assert.Same(FailureCapabilities.None, session.Capabilities.Failures);
    }

    [Theory]
    [InlineData("PMDG 737-800")]
    [InlineData("Airbus A320 Neo Asobo")]
    [InlineData("iniBuilds A310-300")]
    [InlineData("Cessna Skyhawk G1000 Asobo")]
    [InlineData("A320")]
    public async Task No_fenix_variable_is_ever_read_for_another_aircraft(string title)
    {
        var aircraft = new AircraftDescriptor { Title = title };
        var rig = new Rig(aircraft);

        Assert.False(rig.Provider.Match(aircraft).IsSupported);
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Provider.AttachAsync(aircraft));
        await rig.SecondsAsync(3);

        Assert.Equal(0, rig.Reader.ReadCount);
    }

    [Fact]
    public async Task No_read_happens_while_no_aircraft_is_loaded()
    {
        var rig = new Rig(loaded: null);

        await using var session = await rig.Provider.AttachAsync(A319);
        await rig.SecondsAsync(3);

        Assert.Equal(0, rig.Reader.ReadCount);
    }

    [Fact]
    public async Task Cockpit_is_read_every_second_and_hydraulics_every_five()
    {
        var rig = new Rig(A319);
        await using var session = await rig.Provider.AttachAsync(A319);
        await Wait.UntilAsync(() => rig.CockpitReads >= 1 && rig.HydraulicsReads >= 1, "first reads");

        await rig.SecondsAsync(10);

        Assert.InRange(rig.CockpitReads, 10, 12);
        Assert.InRange(rig.HydraulicsReads, 2, 4);
    }

    [Fact]
    public async Task Changing_to_another_fenix_discards_the_previous_values_and_stops_the_old_session()
    {
        var rig = new Rig(A319);
        var old = await rig.Provider.AttachAsync(A319);
        await Wait.UntilAsync(() => rig.CockpitReads >= 1, "A319 read");

        rig.Detector.Current = A321;
        rig.Reader.Values["L:S_OH_NAV_IR1_MODE"] = 2;
        await rig.SecondsAsync(2);
        var oldSnapshot = await old.Telemetry.GetSnapshotAsync();
        var readsAfterChange = rig.CockpitReads;
        await rig.SecondsAsync(3);

        Assert.Empty(oldSnapshot.InertialReferences);
        Assert.Empty(oldSnapshot.FuelPumps);
        Assert.Equal(ValueState.Unavailable, oldSnapshot.Apu.FireHandlePulled.State);
        Assert.Equal(readsAfterChange, rig.CockpitReads);

        await using var session = await rig.Provider.AttachAsync(A321);
        await Ready(session, t => t.InertialReferences.Count == 3);
        var t = await session.Telemetry.GetSnapshotAsync();
        Assert.Equal(InertialReferenceMode.Attitude, t.InertialReferences[0].Mode.Value);
        await old.DisposeAsync();
    }

    [Fact]
    public async Task Changing_to_a_non_fenix_leaves_nothing_fenix_behind()
    {
        var rig = new Rig(A319);
        await using var session = await rig.Provider.AttachAsync(A319);
        await Wait.UntilAsync(() => rig.CockpitReads >= 1, "A319 read");

        rig.Detector.Current = C172;
        await rig.SecondsAsync(2);
        var reads = rig.Reader.ReadCount;
        await rig.SecondsAsync(3);
        var t = await session.Telemetry.GetSnapshotAsync();

        Assert.Equal(reads, rig.Reader.ReadCount);
        Assert.Empty(t.InertialReferences);
        Assert.Empty(t.FuelPumps);
        Assert.Empty(t.HydraulicSystems);
        Assert.Empty(t.Engines);
    }

    [Fact]
    public async Task From_a_non_fenix_to_a_fenix_polling_starts_with_the_fenix_session()
    {
        var rig = new Rig(C172);
        await rig.SecondsAsync(2);
        Assert.Equal(0, rig.Reader.ReadCount);

        rig.Detector.Current = A320();
        await using var session = await rig.Provider.AttachAsync(A320());
        await Ready(session, t => t.InertialReferences.Count == 3);

        Assert.NotEmpty((await session.Telemetry.GetSnapshotAsync()).InertialReferences);
    }

    [Fact]
    public async Task A_disconnection_ages_the_values_and_the_reconnection_restarts_one_loop()
    {
        var rig = new Rig(A319, staleAfter: TimeSpan.FromSeconds(5));
        await using var session = await rig.Provider.AttachAsync(A319);
        await Ready(session, t => t.InertialReferences.Count == 3 && t.FuelPumps.Count == 6);

        rig.Detector.Current = null;
        await rig.SecondsAsync(1);
        var readsWhileAway = rig.CockpitReads;
        await rig.SecondsAsync(6);
        var away = await session.Telemetry.GetSnapshotAsync();

        Assert.Equal(readsWhileAway, rig.CockpitReads);
        Assert.Equal(ValueState.Unknown, away.InertialReferences[0].Mode.State);
        Assert.Equal(ValueState.Unknown, away.FuelPumps[0].IsOn.State);

        rig.Detector.Current = A319;
        await rig.SecondsAsync(1);
        await Wait.UntilAsync(() => rig.CockpitReads > readsWhileAway, "reads after reconnection");
        var before = rig.CockpitReads;
        await rig.SecondsAsync(4);

        Assert.InRange(rig.CockpitReads - before, 3, 5);
        Assert.True((await session.Telemetry.GetSnapshotAsync()).InertialReferences[0].Mode.IsKnown);
    }

    [Fact]
    public async Task Failing_reads_let_the_values_expire_and_recovery_brings_them_back()
    {
        var rig = new Rig(A319, staleAfter: TimeSpan.FromSeconds(5));
        await using var session = await rig.Provider.AttachAsync(A319);
        await Ready(session, t => t.InertialReferences.Count == 3);

        rig.Reader.Failure = new InvalidOperationException("The simulator is not connected.");
        await rig.SecondsAsync(7);
        Assert.Equal(ValueState.Unknown, (await session.Telemetry.GetSnapshotAsync()).InertialReferences[0].Mode.State);

        rig.Reader.Failure = null;
        await rig.SecondsAsync(1);
        await Wait.UntilAsync(() => session.Telemetry.GetSnapshotAsync().Result.InertialReferences[0].Mode.IsKnown, "recovery");
    }

    [Fact]
    public async Task Disposing_the_session_during_a_read_ends_the_polling()
    {
        var rig = new Rig(A319);
        rig.Reader.Gate = new TaskCompletionSource();
        var session = await rig.Provider.AttachAsync(A319);
        await Wait.UntilAsync(() => rig.Reader.ReadCount >= 2, "reads in flight");

        await session.DisposeAsync().AsTask().WaitAsync(Wait.Timeout);
        rig.Reader.Gate = null;
        var reads = rig.Reader.ReadCount;
        await rig.SecondsAsync(3);

        Assert.Equal(reads, rig.Reader.ReadCount);
    }

    [Fact]
    public async Task Slow_throwing_and_multiple_subscribers_do_not_disturb_the_polling()
    {
        var rig = new Rig(A319);
        await using var session = await rig.Provider.AttachAsync(A319);
        using var cts = new CancellationTokenSource(Wait.Timeout);

        var throwing = Task.Run(async () =>
        {
            await foreach (var _ in session.Telemetry.StreamAsync(cancellationToken: cts.Token))
            {
                throw new InvalidOperationException("consumer bug");
            }
        });
        var slow = Task.Run(async () =>
        {
            await foreach (var _ in session.Telemetry.StreamAsync(cancellationToken: cts.Token))
            {
                await Task.Delay(200, cts.Token);
                return;
            }
        });
        AircraftTelemetry? seen = null;
        var healthy = Task.Run(async () =>
        {
            await foreach (var t in session.Telemetry.StreamAsync(cancellationToken: cts.Token))
            {
                if (t.InertialReferences.Count == 3)
                {
                    seen = t;
                    return;
                }
            }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => throwing);
        await slow;
        await healthy;
        var reads = rig.CockpitReads;
        await rig.SecondsAsync(2);

        Assert.NotNull(seen);
        Assert.True(rig.CockpitReads > reads);
    }

    [Fact]
    public async Task Without_a_variable_reader_the_session_is_the_block_5_one()
    {
        var clock = new CountingClock();
        var provider = new FenixAircraftProvider(timeProvider: clock, genericTelemetry: new StampedGenericTelemetry(clock));

        await using var session = await provider.AttachAsync(A319);

        Assert.False(session.Capabilities.Telemetry.InertialReferences);
        Assert.False(session.Capabilities.Telemetry.Fire);
        Assert.Empty((await session.Telemetry.GetSnapshotAsync()).InertialReferences);
    }

    [Fact]
    public async Task Failure_commands_stay_unsupported()
    {
        var rig = new Rig(A319);
        await using var session = await rig.Provider.AttachAsync(A319);

        var result = await session.Failures.TriggerAsync(new FailureCommand(FailureKey.Parse("engine.fire"), FailureTarget.Engine(1)));

        Assert.Equal(FailureCommandStatus.NotSupported, result.Status);
    }

    private static AircraftDescriptor A320() => new() { Title = "FenixA320 CFM WF SL", LiveryFolder = "AFR-F-HTST" };

    /// <summary>Waits until the session snapshot satisfies the condition (a read counted is not yet a read applied).</summary>
    private static Task Ready(IAircraftSession session, Func<AircraftTelemetry, bool> condition) =>
        Wait.UntilAsync(() => condition(session.Telemetry.GetSnapshotAsync().GetAwaiter().GetResult()), "Fenix state applied");
}
