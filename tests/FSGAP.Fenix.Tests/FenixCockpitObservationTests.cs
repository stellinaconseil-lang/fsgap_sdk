using System.Reflection;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Cockpit;
using FSGAP.Abstractions.Simulator;
using FSGAP.Fenix.Cockpit;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Fenix.Tests;

/// <summary>
/// The Fenix cockpit observation capability: a fixed, normalized catalog of keys read in one batch on the shared
/// simulator connection, with no raw vendor variable name reaching the consumer and no connection of its own.
/// </summary>
public class FenixCockpitObservationTests
{
    private static readonly AircraftDescriptor Fenix =
        new() { Title = "FenixA321 IAE WF SC", LiveryFolder = "AEE-SX-DNH-7F2F", Registration = "SX-DNH" };

    // The intended initial catalog. This list is the contract: adding or removing a key must be a deliberate edit here.
    private static readonly string[] ExpectedKeys =
    [
        "fire-test.engine-1",
        "fire-test.engine-2",
        "fire-test.apu",
        "smoke-test.cargo",
        "fire.engine-1.agent-1.discharge",
        "fire.engine-1.agent-2.discharge",
        "fire.engine-2.agent-1.discharge",
        "fire.apu.agent.discharge",
        "master-warning.captain.input",
        "adirs.ir-1.mode",
        "adirs.ir-2.mode",
        "adirs.ir-3.mode",
        "fuel-pump.left-1",
        "fuel-pump.left-2",
        "fuel-pump.center-1",
        "fuel-pump.center-2",
        "fuel-pump.right-1",
        "fuel-pump.right-2",
        "fire.engine-1.indication",
        "fire.engine-2.indication",
        "fire.engine-1.button.input",
        "fire.engine-1.button.light",
        "fire.engine-2.button.input",
        "fire.engine-2.button.light",
        "fire.engine-1.agent-1.light-lower",
        "fire.engine-1.agent-1.light-upper",
        "fire.engine-1.agent-2.light-lower",
        "fire.engine-1.agent-2.light-upper",
        "fire.engine-2.agent-1.light-lower",
        "fire.engine-2.agent-1.light-upper",
        "fire.engine-2.agent-2.discharge",
        "fire.engine-2.agent-2.light-lower",
        "fire.engine-2.agent-2.light-upper",
        "fire.apu.button.input",
        "master-warning.captain.light",
        "master-warning.captain.light-l",
        "master-warning.first-officer.input",
        "master-warning.first-officer.light",
        "master-warning.first-officer.light-l",
    ];

    [Fact]
    public void The_catalog_is_exactly_the_intended_keys_in_order()
    {
        Assert.Equal(ExpectedKeys, FenixCockpitObservations.Keys.Select(k => k.Value));
        Assert.Equal(39, FenixCockpitObservations.Keys.Count);
    }

    [Fact]
    public void Every_catalog_key_is_a_valid_normalized_key_and_unique()
    {
        Assert.All(FenixCockpitObservations.Keys, k => Assert.True(CockpitObservationKey.TryParse(k.Value, out _)));
        Assert.Equal(FenixCockpitObservations.Keys.Count, FenixCockpitObservations.Keys.Distinct().Count());
    }

    [Fact]
    public void The_read_batch_lines_up_one_variable_per_control_in_catalog_order()
    {
        Assert.Equal(FenixCockpitObservations.Controls.Count, FenixCockpitObservations.Variables.Count);
        Assert.Equal(FenixCockpitObservations.Controls.Count, FenixCockpitObservations.Keys.Count);
        for (var i = 0; i < FenixCockpitObservations.Controls.Count; i++)
        {
            Assert.Same(FenixCockpitObservations.Controls[i].Variable, FenixCockpitObservations.Variables[i]);
            Assert.Equal(FenixCockpitObservations.Controls[i].Key, FenixCockpitObservations.Keys[i]);
        }
    }

    [Fact]
    public void The_published_capability_lists_the_catalog()
    {
        var capability = FenixCockpitObservations.Capabilities;

        Assert.True(capability.CanObserve);
        Assert.Equal(FenixCockpitObservations.Keys, capability.Keys);
        Assert.True(capability.Supports(CockpitObservationKey.Parse("fire-test.engine-1")));
        Assert.False(capability.Supports(CockpitObservationKey.Parse("not.a.fenix.key")));
    }

    [Fact]
    public async Task A_read_normalizes_each_raw_value_to_its_declared_kind()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var reader = new FakeVariableReader();
        SetRaw(reader, "fuel-pump.left-1", 1);      // boolean on
        SetRaw(reader, "fuel-pump.left-2", 0);      // boolean off
        SetRaw(reader, "adirs.ir-1.mode", 2);       // integer selector
        SetRaw(reader, "fire-test.engine-1", 2.9);  // integer counter, rounds away from zero
        var provider = new FenixCockpitObservationProvider(reader, () => false, clock);

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal(clock.GetUtcNow(), snapshot.Timestamp);
        Assert.True(snapshot.Get(Key("fuel-pump.left-1")).TryGetBoolean(out var on) && on);
        Assert.True(snapshot.Get(Key("fuel-pump.left-2")).TryGetBoolean(out var off) && !off);
        Assert.True(snapshot.Get(Key("adirs.ir-1.mode")).TryGetInteger(out var mode) && mode == 2);
        Assert.True(snapshot.Get(Key("fire-test.engine-1")).TryGetInteger(out var count) && count == 3);
    }

    [Fact]
    public async Task When_another_aircraft_is_loaded_every_key_is_unknown_and_nothing_is_read()
    {
        var reader = new FakeVariableReader().WithNominalCockpit();
        var provider = new FenixCockpitObservationProvider(reader, aircraftReplaced: () => true, TimeProvider.System);

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal(0, reader.ReadCount); // no stale read against the other aircraft
        Assert.Equal(FenixCockpitObservations.Keys.Count, snapshot.Values.Count);
        Assert.All(FenixCockpitObservations.Keys, k => Assert.Equal(CockpitObservationState.Unknown, snapshot.Get(k).State));
    }

    [Fact]
    public async Task A_read_failure_yields_unknown_values_rather_than_a_throw_or_a_stale_value()
    {
        var reader = new FakeVariableReader().WithNominalCockpit();
        reader.Failure = new InvalidOperationException("simulator away");
        var provider = new FenixCockpitObservationProvider(reader, () => false, TimeProvider.System);

        var snapshot = await provider.GetSnapshotAsync();

        Assert.All(FenixCockpitObservations.Keys, k => Assert.Equal(CockpitObservationState.Unknown, snapshot.Get(k).State));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var reader = new FakeVariableReader();
        reader.Failure = new OperationCanceledException();
        var provider = new FenixCockpitObservationProvider(reader, () => false, TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSnapshotAsync());
    }

    [Fact]
    public async Task The_read_goes_through_the_shared_reader_as_one_batch_of_the_catalog_variables()
    {
        var reader = new FakeVariableReader().WithNominalCockpit();
        var provider = new FenixCockpitObservationProvider(reader, () => false, TimeProvider.System);

        await provider.GetSnapshotAsync();

        Assert.Equal(1, reader.ReadCount); // one batched read, not one request per key
        Assert.True(reader.Reads.TryPeek(out var group));
        Assert.Same(FenixCockpitObservations.Variables, group);
    }

    [Fact]
    public void The_cockpit_observation_provider_depends_only_on_the_shared_variable_reader()
    {
        // It must not open a connection of its own: its only transport field is the injected ISimulatorVariableReader,
        // and it holds no HTTP client or other connection object.
        var fields = typeof(FenixCockpitObservationProvider)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Single(fields, f => f.FieldType == typeof(ISimulatorVariableReader));
        Assert.DoesNotContain(fields, f => f.FieldType == typeof(HttpClient));
        Assert.DoesNotContain(fields, f => f.FieldType.Name.Contains("SimConnect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_fenix_session_with_a_variable_reader_publishes_and_serves_cockpit_observations()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var reader = new FakeVariableReader().WithNominalCockpit();
        var provider = new FenixAircraftProvider(
            timeProvider: clock,
            genericTelemetry: new StampedGenericTelemetry(clock),
            simulatorVariables: reader);

        await using var session = await provider.AttachAsync(Fenix);
        var capability = session.Capabilities.CockpitObservations;
        var snapshot = await session.CockpitObservations.GetSnapshotAsync();

        Assert.True(capability.CanObserve);
        Assert.Equal(FenixCockpitObservations.Keys, capability.Keys);
        // The nominal cockpit has all six pumps on and the IRs in NAV (mode 1).
        Assert.True(snapshot.Get(Key("fuel-pump.left-1")).TryGetBoolean(out var on) && on);
        Assert.True(snapshot.Get(Key("adirs.ir-1.mode")).TryGetInteger(out var mode) && mode == 1);
    }

    [Fact]
    public async Task A_fenix_session_without_a_variable_reader_observes_nothing()
    {
        var provider = new FenixAircraftProvider(timeProvider: new FakeTimeProvider());

        await using var session = await provider.AttachAsync(Fenix);
        var snapshot = await session.CockpitObservations.GetSnapshotAsync();

        Assert.False(session.Capabilities.CockpitObservations.CanObserve);
        Assert.Empty(session.Capabilities.CockpitObservations.Keys);
        Assert.Empty(snapshot.Values);
    }

    private static CockpitObservationKey Key(string value) => CockpitObservationKey.Parse(value);

    private static void SetRaw(FakeVariableReader reader, string key, double value)
    {
        var control = FenixCockpitObservations.Controls.Single(c => c.Key.Value == key);
        reader.Values[control.Variable.Name] = value;
    }
}
