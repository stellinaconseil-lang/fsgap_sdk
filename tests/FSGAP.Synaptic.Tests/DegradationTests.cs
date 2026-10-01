using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Degradations;
using FSGAP.Core.Failures;
using FSGAP.Synaptic.Degradations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic.Tests;

/// <summary>BLOCK 11.0: the four controlled degradations qualified live on the Synaptic A220 (BLOCK 10C.3).</summary>
public class DegradationTests
{
    private static readonly FSGAP.Synaptic.Systems.SynapticControlBoard.Timing Fast = new(TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(2));

    private static readonly DegradationKey Generator = DegradationKey.Parse("electrical.generator.1.forced-off");
    private static readonly DegradationKey Pump = DegradationKey.Parse("hydraulic.system-3.electric-pump-a.forced-off");
    private static readonly DegradationKey Pack = DegradationKey.Parse("air-conditioning.pack.1.forced-off");
    private static readonly DegradationKey Pfcc = DegradationKey.Parse("flight-controls.pfcc.1.forced-off");

    /// <summary>Key, internal variable, qualified normal value, degraded value (the live-qualified mapping).</summary>
    public static TheoryData<string, string, double, double> Qualified => new()
    {
        { "electrical.generator.1.forced-off", "L:A22X L Gen Off", 0, 1 },
        { "hydraulic.system-3.electric-pump-a.forced-off", "L:A22X ACMP 3A", 1, 0 },
        { "air-conditioning.pack.1.forced-off", "L:A22X L Pack Off", 0, 1 },
        { "flight-controls.pfcc.1.forced-off", "L:A22X PFCC 1 Off", 0, 1 },
    };

    private sealed class Rig : IAsyncDisposable
    {
        public Rig(IAircraftDetector? detector = null, Action<FakeVariableReader>? normal = null)
        {
            // The live normal configuration: every switch at its qualified normal value.
            Reader.Values["L:A22X L Gen Off"] = 0;
            Reader.Values["L:A22X ACMP 3A"] = 1;
            Reader.Values["L:A22X L Pack Off"] = 0;
            Reader.Values["L:A22X PFCC 1 Off"] = 0;
            normal?.Invoke(Reader);
            Writer = new FakeVariableWriter(Reader);
            Provider = new SynapticDegradationProvider(Reader, Writer, detector, Descriptors.AirFrance, NullLogger.Instance, Fast);
        }

        public FakeVariableReader Reader { get; } = new();

        public FakeVariableWriter Writer { get; }

        public SynapticDegradationProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    // -- Catalog ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_catalog_is_exactly_the_four_qualified_degradations()
    {
        var capabilities = SynapticDegradationProvider.Capabilities;

        Assert.Equal([Generator, Pump, Pack, Pfcc], capabilities.Catalog.Select(d => d.Key));
        Assert.Equal(1, capabilities.MaxActive);
        Assert.Equal(
            [DegradationCategory.Electrical, DegradationCategory.Hydraulic, DegradationCategory.AirConditioning, DegradationCategory.FlightControls],
            capabilities.Catalog.Select(d => d.Category));
        Assert.All(capabilities.Catalog, d =>
        {
            Assert.True(capabilities.CanApply(d.Key));
            Assert.True(capabilities.CanRestore(d.Key));
            Assert.True(capabilities.CanReadState(d.Key));
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
            Assert.EndsWith(".forced-off", d.Key.Value);
            Assert.DoesNotContain("fail", d.Key.Value, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Public_descriptors_carry_no_raw_variable_name_or_value()
    {
        foreach (var d in SynapticDegradationProvider.Capabilities.Catalog)
        {
            var text = $"{d.Key} {d.DisplayName} {d.Description}";
            Assert.DoesNotContain("A22X", text, StringComparison.Ordinal);
            Assert.DoesNotContain("L:", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Synaptic", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Descriptions_state_that_a_degradation_is_not_a_failure()
    {
        Assert.All(SynapticDegradationProvider.Capabilities.Catalog, d => Assert.Contains("forced off", d.DisplayName, StringComparison.Ordinal));
        Assert.Contains("Not a generator failure", SynapticDegradationControls.Find(Generator)!.Descriptor.Description);
        Assert.Contains("not a failure of hydraulic system 3", SynapticDegradationControls.Find(Pump)!.Descriptor.Description);
        Assert.Contains("Not a pack failure", SynapticDegradationControls.Find(Pack)!.Descriptor.Description);
        Assert.Contains("Not a PFCC internal fault", SynapticDegradationControls.Find(Pfcc)!.Descriptor.Description);
    }

    // -- Apply / restore -------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Qualified))]
    public async Task Apply_writes_the_degraded_value_reads_it_back_and_restore_writes_the_normal_value(string keyText, string variable, double normal, double degraded)
    {
        await using var rig = new Rig();
        var key = DegradationKey.Parse(keyText);
        Assert.Equal(DegradationState.Normal, await rig.Provider.GetStateAsync(key));

        var applied = await rig.Provider.ApplyAsync(key);

        Assert.Equal(DegradationCommandStatus.Succeeded, applied.Status);
        Assert.Equal(DegradationState.Applied, applied.State);
        Assert.Equal([(variable, degraded)], rig.Writer.Writes.Select(w => (w.Variable.Name, w.Value)));
        Assert.Equal(DegradationState.Applied, await rig.Provider.GetStateAsync(key));
        Assert.Equal(key, rig.Provider.OwnedKey);

        var restored = await rig.Provider.RestoreAsync(key);

        Assert.Equal(DegradationCommandStatus.Succeeded, restored.Status);
        Assert.Equal(DegradationState.Normal, restored.State);
        Assert.Equal([(variable, degraded), (variable, normal)], rig.Writer.Writes.Select(w => (w.Variable.Name, w.Value)));
        Assert.Equal(DegradationState.Normal, await rig.Provider.GetStateAsync(key));
        Assert.Null(rig.Provider.OwnedKey);
    }

    [Fact]
    public async Task Applying_again_what_this_session_applied_writes_nothing()
    {
        await using var rig = new Rig();
        await rig.Provider.ApplyAsync(Generator);

        var again = await rig.Provider.ApplyAsync(Generator);

        Assert.Equal(DegradationCommandStatus.Succeeded, again.Status);
        Assert.Single(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(-1.0)]
    public async Task An_unexpected_control_value_is_reported_unknown_and_never_written(double raw)
    {
        await using var rig = new Rig(normal: r => r.Values["L:A22X ACMP 3A"] = raw);

        Assert.Equal(DegradationState.Unknown, await rig.Provider.GetStateAsync(Pump));
        var result = await rig.Provider.ApplyAsync(Pump);

        Assert.Equal(DegradationCommandStatus.Rejected, result.Status);
        Assert.Equal(DegradationState.Unknown, result.State);
        Assert.Empty(rig.Writer.Writes);
    }

    [Fact]
    public async Task A_read_failure_is_unavailable_and_nothing_is_written()
    {
        await using var rig = new Rig();
        rig.Reader.Failure = new InvalidOperationException("The simulator is not connected.");

        Assert.Equal(DegradationState.Unavailable, await rig.Provider.GetStateAsync(Pack));
        var result = await rig.Provider.ApplyAsync(Pack);

        Assert.Equal(DegradationCommandStatus.Unavailable, result.Status);
        Assert.Empty(rig.Writer.Writes);
        Assert.Null(rig.Provider.OwnedKey);
    }

    [Fact]
    public async Task A_write_refused_for_lack_of_connection_is_unavailable_and_not_owned()
    {
        await using var rig = new Rig();
        rig.Writer.Failure = new InvalidOperationException("The simulator is not connected.");

        var result = await rig.Provider.ApplyAsync(Pfcc);

        Assert.Equal(DegradationCommandStatus.Unavailable, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(rig.Provider.OwnedKey);
    }

    [Fact]
    public async Task A_write_that_did_not_complete_is_unconfirmed_and_owned_so_it_can_still_be_restored()
    {
        await using var rig = new Rig();
        rig.Writer.Failure = new TimeoutException("no answer");

        var result = await rig.Provider.ApplyAsync(Generator);

        Assert.Equal(DegradationCommandStatus.Unconfirmed, result.Status);
        Assert.Equal(Generator, rig.Provider.OwnedKey);
        rig.Writer.Failure = null;
        Assert.Equal(DegradationCommandStatus.Succeeded, (await rig.Provider.RestoreAsync(Generator)).Status);
        Assert.Equal(("L:A22X L Gen Off", 0.0), (rig.Writer.Writes.Single().Variable.Name, rig.Writer.Writes.Single().Value));
    }

    [Fact]
    public async Task A_readback_mismatch_is_never_reported_as_success()
    {
        await using var rig = new Rig();
        rig.Writer.Ignore = true;

        var apply = await rig.Provider.ApplyAsync(Pack);

        Assert.Equal(DegradationCommandStatus.Unconfirmed, apply.Status);
        Assert.Equal(DegradationState.Normal, apply.State);
        Assert.Equal(Pack, rig.Provider.OwnedKey);

        rig.Reader.Values["L:A22X L Pack Off"] = 1; // the aircraft took it late
        var restore = await rig.Provider.RestoreAsync(Pack);
        Assert.Equal(DegradationCommandStatus.Unconfirmed, restore.Status);
        Assert.Equal(DegradationState.Applied, restore.State);
        Assert.Equal(Pack, rig.Provider.OwnedKey);
    }

    [Fact]
    public async Task Unknown_keys_are_not_supported_without_any_read_or_write()
    {
        await using var rig = new Rig();
        var unknown = DegradationKey.Parse("electrical.generator.2.forced-off");

        Assert.Equal(DegradationCommandStatus.NotSupported, (await rig.Provider.ApplyAsync(unknown)).Status);
        Assert.Equal(DegradationCommandStatus.NotSupported, (await rig.Provider.RestoreAsync(unknown)).Status);
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Provider.GetStateAsync(unknown));
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Empty(rig.Writer.Writes);
    }

    // -- Ownership -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pre_existing_degradation_is_reported_never_written_never_restored()
    {
        await using var rig = new Rig(normal: r => r.Values["L:A22X L Gen Off"] = 1);

        Assert.Equal(DegradationState.PreExisting, await rig.Provider.GetStateAsync(Generator));
        var apply = await rig.Provider.ApplyAsync(Generator);
        var restore = await rig.Provider.RestoreAsync(Generator);

        Assert.Equal((DegradationCommandStatus.Rejected, DegradationState.PreExisting), (apply.Status, apply.State));
        Assert.Equal((DegradationCommandStatus.Rejected, DegradationState.PreExisting), (restore.Status, restore.State));
        Assert.Empty(rig.Writer.Writes);
        Assert.Null(rig.Provider.OwnedKey);
    }

    [Fact]
    public async Task Dispose_restores_what_this_session_applied()
    {
        var rig = new Rig();
        await rig.Provider.ApplyAsync(Pfcc);

        await rig.DisposeAsync();

        Assert.Equal([1.0, 0.0], rig.Writer.Writes.Select(w => w.Value));
        Assert.Equal(0, rig.Reader.Values["L:A22X PFCC 1 Off"]);
        Assert.Equal(DegradationState.Unavailable, await rig.Provider.GetStateAsync(Pfcc));
        Assert.Equal(DegradationCommandStatus.Unavailable, (await rig.Provider.ApplyAsync(Pfcc)).Status);
    }

    [Fact]
    public async Task Dispose_never_restores_a_pre_existing_degradation()
    {
        var rig = new Rig(normal: r => r.Values["L:A22X L Pack Off"] = 1);
        await rig.Provider.ApplyAsync(Pack);

        await rig.DisposeAsync();

        Assert.Empty(rig.Writer.Writes);
        Assert.Equal(1, rig.Reader.Values["L:A22X L Pack Off"]);
    }

    [Fact]
    public async Task Dispose_without_connection_does_not_throw_and_does_not_pretend()
    {
        var rig = new Rig();
        await rig.Provider.ApplyAsync(Generator);
        rig.Writer.Failure = new InvalidOperationException("The simulator is not connected.");

        await rig.DisposeAsync();

        Assert.Single(rig.Writer.Writes);
        Assert.Equal(1, rig.Reader.Values["L:A22X L Gen Off"]);
        await rig.DisposeAsync(); // idempotent
    }

    [Fact]
    public async Task Restoring_a_control_the_pilot_already_restored_releases_it_without_writing()
    {
        await using var rig = new Rig();
        await rig.Provider.ApplyAsync(Generator);
        rig.Reader.Values["L:A22X L Gen Off"] = 0; // the pilot switched it back

        Assert.Equal(DegradationState.Normal, await rig.Provider.GetStateAsync(Generator));
        Assert.Null(rig.Provider.OwnedKey);
        Assert.Equal(DegradationCommandStatus.Rejected, (await rig.Provider.RestoreAsync(Generator)).Status);
        Assert.Single(rig.Writer.Writes);
    }

    // -- One active degradation ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_one_degradation_may_be_active_at_a_time()
    {
        await using var rig = new Rig();
        Assert.True((await rig.Provider.ApplyAsync(Generator)).IsSuccess);

        var second = await rig.Provider.ApplyAsync(Pack);

        Assert.Equal(DegradationCommandStatus.Rejected, second.Status);
        Assert.Equal(DegradationState.Normal, second.State);
        Assert.Contains("only one controlled degradation", second.Message);
        Assert.Single(rig.Writer.Writes);

        Assert.True((await rig.Provider.RestoreAsync(Generator)).IsSuccess);
        Assert.True((await rig.Provider.ApplyAsync(Pack)).IsSuccess);
        Assert.Equal(Pack, rig.Provider.OwnedKey);
    }

    [Fact]
    public async Task Concurrent_applies_never_produce_two_active_degradations()
    {
        await using var rig = new Rig();

        var results = await Task.WhenAll(new[] { Generator, Pump, Pack, Pfcc }.Select(k => Task.Run(() => rig.Provider.ApplyAsync(k))));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(rig.Writer.Writes);
    }

    // -- Session changes -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_atc_id_change_keeps_the_ownership()
    {
        var detector = new StreamingDetector(Descriptors.AirFrance);
        await using var rig = new Rig(detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the degradation watch");
        await rig.Provider.ApplyAsync(Generator);

        detector.Publish(Descriptors.AirFrance with { Registration = "C-FFCO" });
        await Wait.SettleAsync();

        Assert.Equal(Generator, rig.Provider.OwnedKey);
        Assert.Equal(DegradationState.Applied, await rig.Provider.GetStateAsync(Generator));
    }

    [Fact]
    public async Task A_disconnection_drops_the_ownership_and_the_state_is_read_again_after_reconnection()
    {
        var detector = new StreamingDetector(Descriptors.AirFrance);
        await using var rig = new Rig(detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the degradation watch");
        await rig.Provider.ApplyAsync(Generator);

        detector.Publish(null);
        await Wait.UntilAsync(() => rig.Provider.OwnedKey is null, "the ownership drop");
        Assert.Equal(DegradationCommandStatus.Unavailable, (await rig.Provider.ApplyAsync(Pack)).Status);

        detector.Publish(Descriptors.AirFrance);
        Assert.Equal(DegradationState.PreExisting, await rig.Provider.GetStateAsync(Generator));
        Assert.Equal(DegradationCommandStatus.Rejected, (await rig.Provider.RestoreAsync(Generator)).Status);
        Assert.Single(rig.Writer.Writes);
    }

    [Fact]
    public async Task A_replaced_aircraft_drops_the_ownership_and_is_never_written()
    {
        var detector = new StreamingDetector(Descriptors.AirFrance);
        var rig = new Rig(detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the degradation watch");
        await rig.Provider.ApplyAsync(Pump);

        detector.Publish(Descriptors.Delta);
        await Wait.UntilAsync(() => rig.Provider.OwnedKey is null, "the ownership drop");
        Assert.Equal(DegradationCommandStatus.Unavailable, (await rig.Provider.RestoreAsync(Pump)).Status);
        await rig.DisposeAsync();

        Assert.Single(rig.Writer.Writes);
        Assert.Equal(0, detector.Watchers);
    }

    // -- Provider and session surface ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_synaptic_session_offers_degradations_and_the_normalized_failures()
    {
        var reader = new FakeVariableReader();
        reader.Values["L:A22X ACMP 3A"] = 1;
        var writer = new FakeVariableWriter(reader);
        var provider = new SynapticAircraftProvider(simulatorVariables: reader, simulatorVariableWriter: writer);

        await using var session = await provider.AttachAsync(Descriptors.AirFrance);

        // BLOCK 11.2: failures are no longer None on a Synaptic session with a writer.
        Assert.Equal(40, session.Capabilities.Failures.Catalog.Count);
        Assert.True(session.Capabilities.Failures.CanReadActiveFailures);
        Assert.IsNotType<UnsupportedFailureProvider>(session.Failures);
        Assert.Equal(4, session.Capabilities.Degradations.Catalog.Count);
        Assert.Equal(1, session.Capabilities.Degradations.MaxActive);
        Assert.IsNotType<UnsupportedDegradationProvider>(session.Degradations);
        Assert.True((await session.Degradations.ApplyAsync(Pump)).IsSuccess);

        await session.DisposeAsync();
        Assert.Equal([0.0, 1.0], writer.Writes.Select(w => w.Value)); // restored on clean dispose
    }

    [Fact]
    public async Task Without_a_writer_a_synaptic_session_has_no_degradation()
    {
        var provider = new SynapticAircraftProvider(simulatorVariables: new FakeVariableReader());

        await using var session = await provider.AttachAsync(Descriptors.AirFrance);

        Assert.Same(DegradationCapabilities.None, session.Capabilities.Degradations);
        Assert.Same(UnsupportedDegradationProvider.Instance, session.Degradations);
        Assert.Equal(DegradationCommandStatus.NotSupported, (await session.Degradations.ApplyAsync(Generator)).Status);
    }

    [Fact]
    public async Task A_writer_without_a_reader_never_enables_writes()
    {
        var writer = new FakeVariableWriter(new FakeVariableReader());
        var provider = new SynapticAircraftProvider(simulatorVariableWriter: writer);

        await using var session = await provider.AttachAsync(Descriptors.AirFrance);

        Assert.Same(DegradationCapabilities.None, session.Capabilities.Degradations);
        Assert.Empty(writer.Writes);
    }
}
