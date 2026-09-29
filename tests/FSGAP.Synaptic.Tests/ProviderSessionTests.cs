using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Failures;

namespace FSGAP.Synaptic.Tests;

/// <summary>Sessions of the Synaptic provider: capabilities, failures, overlay lifecycle.</summary>
public class ProviderSessionTests
{
    private static bool IsSynapticVariable(Abstractions.Simulator.SimulatorVariable v) => v.Name.StartsWith("L:A22X ", StringComparison.Ordinal);

    [Fact]
    public async Task Without_simulator_inputs_a_session_has_no_capability_and_no_failure()
    {
        await using var session = await new SynapticAircraftProvider().AttachAsync(Descriptors.AirFrance);

        Assert.Equal("synaptic", session.ProviderId);
        Assert.Equal(AircraftCapabilities.None, session.Capabilities);
        Assert.Same(UnsupportedFailureProvider.Instance, session.Failures);
        Assert.Equal("F-HZUF", session.Identity.Registration);
    }

    [Fact]
    public async Task A_full_session_declares_generic_and_overlay_sections_and_failure_capabilities_none()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        var provider = new SynapticAircraftProvider(null, clock, null, new StampedGenericTelemetry(clock), reader, new FakeDetector(Descriptors.AirFrance));

        await using var session = await provider.AttachAsync(Descriptors.AirFrance);

        var sections = session.Capabilities.Telemetry;
        Assert.True(sections.FuelPumps && sections.Apu && sections.Fire);
        Assert.True(sections.FlightState && sections.Engines && sections.LandingGear && sections.FlightControls && sections.Environment && sections.Warnings);
        Assert.False(sections.Pressurization);
        Assert.False(sections.InertialReferences);
        Assert.False(sections.Hydraulics);
        Assert.False(sections.Electrical);
        Assert.Same(FailureCapabilities.None, session.Capabilities.Failures);
        Assert.False(session.Capabilities.Failures.CanReadActiveFailures);
        Assert.Empty(session.Capabilities.Failures.Catalog);
        Assert.Same(UnsupportedFailureProvider.Instance, session.Failures);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.Failures.GetActiveFailuresAsync()); // CanReadActiveFailures is false
    }

    [Fact]
    public async Task Failure_commands_answer_not_supported()
    {
        await using var session = await new SynapticAircraftProvider().AttachAsync(Descriptors.Delta);
        var command = new FailureCommand(FailureKey.Parse("engine.1.fire"), FailureTarget.Engine(1));

        Assert.Equal(FailureCommandStatus.NotSupported, (await session.Failures.TriggerAsync(command)).Status);
        Assert.Equal(FailureCommandStatus.NotSupported, (await session.Failures.ClearAsync(command)).Status);
    }

    [Fact]
    public async Task The_overlay_is_read_on_the_shared_reader_and_published_then_stops_on_dispose()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        reader.Values["L:A22X L Boost Pump"] = 1;
        reader.Values["L:A22X R Boost Pump"] = 2;
        reader.Values["L:A22X APU Switch"] = 2;
        reader.Values["L:A22X APU Bleed Off"] = 0;
        reader.Values["L:A22X R Eng Fire"] = 1;
        var provider = new SynapticAircraftProvider(null, clock, null, new StampedGenericTelemetry(clock), reader, new FakeDetector(Descriptors.AirFrance));

        var session = await provider.AttachAsync(Descriptors.AirFrance);
        await Wait.UntilAsync(() => reader.ReadCount >= 1 && clock.TimersCreated >= 1, "the first overlay read");
        var snapshot = await session.Telemetry.GetSnapshotAsync();

        Assert.Equal([FuelPumpMode.Auto, FuelPumpMode.On], snapshot.FuelPumps.Select(p => p.Mode.Value));
        Assert.Equal(ValueState.Unavailable, snapshot.FuelPumps[0].IsOn.State);
        Assert.True(snapshot.Apu.MasterSwitchOn.Value);
        Assert.True(snapshot.Apu.BleedOn.Value);
        Assert.All(reader.Reads, r => Assert.Equal(6, r.Count));
        Assert.All(reader.Reads, r => Assert.All(r, v => Assert.True(IsSynapticVariable(v))));

        await session.DisposeAsync();
        var reads = reader.ReadCount;
        clock.Advance(TimeSpan.FromSeconds(10));
        await Wait.SettleAsync();

        Assert.Equal(reads, reader.ReadCount);
    }

    [Fact]
    public async Task One_group_read_every_two_seconds()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        await using var session = await new SynapticAircraftProvider(null, clock, null, null, reader, new FakeDetector(Descriptors.AirFrance)).AttachAsync(Descriptors.AirFrance);
        await Wait.UntilAsync(() => reader.ReadCount == 1 && clock.TimersCreated == 1, "the first read");

        for (var i = 2; i <= 5; i++)
        {
            clock.Advance(SynapticVariables_Interval);
            var expected = i;
            await Wait.UntilAsync(() => reader.ReadCount == expected && clock.TimersCreated == expected, $"read {expected}");
        }

        clock.Advance(TimeSpan.FromSeconds(1.9));
        await Wait.SettleAsync();
        Assert.Equal(5, reader.ReadCount);
    }

    private static readonly TimeSpan SynapticVariables_Interval = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Once_another_aircraft_is_loaded_the_session_stops_reading_and_publishes_nothing()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        reader.Values["L:A22X L Boost Pump"] = 2;
        var detector = new FakeDetector(Descriptors.AirFrance);
        await using var session = await new SynapticAircraftProvider(null, clock, null, new StampedGenericTelemetry(clock), reader, detector).AttachAsync(Descriptors.AirFrance);
        await Wait.UntilAsync(() => reader.ReadCount == 1 && clock.TimersCreated == 1, "the first read");

        detector.Current = Descriptors.FenixA320;
        clock.Advance(TimeSpan.FromSeconds(2));
        await Wait.UntilAsync(() => clock.TimersCreated == 2, "the next cycle");
        var snapshot = await session.Telemetry.GetSnapshotAsync();

        Assert.Equal(1, reader.ReadCount);
        Assert.Empty(snapshot.FuelPumps);
        Assert.Empty(snapshot.Engines);
    }

    [Fact]
    public async Task A_failing_reader_expires_the_overlay_values_instead_of_freezing_them()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        reader.Values["L:A22X L Boost Pump"] = 2;
        await using var session = await new SynapticAircraftProvider(null, clock, null, new StampedGenericTelemetry(clock), reader, new FakeDetector(Descriptors.AirFrance)).AttachAsync(Descriptors.AirFrance);
        await Wait.UntilAsync(() => reader.ReadCount == 1 && clock.TimersCreated == 1, "the first read");

        reader.Failure = new InvalidOperationException("connection lost");
        for (var i = 2; i <= 9; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            var expected = i;
            await Wait.UntilAsync(() => clock.TimersCreated == expected, $"cycle {expected}");
        }

        var snapshot = await session.Telemetry.GetSnapshotAsync();

        Assert.Equal(ValueState.Unknown, snapshot.FuelPumps[0].Mode.State);
    }
}
