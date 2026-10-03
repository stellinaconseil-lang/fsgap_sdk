using FSGAP.Abstractions;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Fenix;
using FSGAP.Synaptic.Degradations;
using FSGAP.Synaptic.Failures;
using FSGAP.Synaptic.Systems;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic.Tests;

/// <summary>
/// BLOCK 11.2: the Synaptic failure provider. These tests prove the structure and the mechanics on a fake simulator,
/// never that a recipe produces the right effect in the aircraft (that is qualified later, from FSHANGAR).
/// </summary>
public class FailureProviderTests
{
    private static readonly SynapticControlBoard.Timing Fast = new(TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(2));

    private const double LowerBrightness = 0.73;

    private static FailureCommand Cmd(string key, FailureTarget? target = null) => new(FailureKey.Parse(key), target);

    private sealed class Rig : IAsyncDisposable
    {
        public Rig(IAircraftDetector? detector = null, Action<FakeVariableReader>? configure = null)
        {
            // The normal configuration: every registry control at its normal value (lower display at some brightness).
            foreach (var control in SynapticControls.All)
            {
                Reader.Values[control.Variable.Name] = control.Normal ?? LowerBrightness;
            }

            configure?.Invoke(Reader);
            Writer = new FakeVariableWriter(Reader);
            Board = new SynapticControlBoard(Reader, Writer, detector, Descriptors.AirFrance, NullLogger.Instance, Fast);
            Failures = new SynapticFailureProvider(Board, ownsBoard: false);
            Degradations = new SynapticDegradationProvider(Board, ownsBoard: false);
        }

        public FakeVariableReader Reader { get; } = new();

        public FakeVariableWriter Writer { get; }

        public SynapticControlBoard Board { get; }

        public SynapticFailureProvider Failures { get; }

        public SynapticDegradationProvider Degradations { get; }

        public (string, double)[] Writes => Writer.Writes.Select(w => (w.Variable.Name, w.Value)).ToArray();

        public async ValueTask DisposeAsync()
        {
            await Degradations.DisposeAsync();
            await Failures.DisposeAsync();
            await Board.DisposeAsync();
        }
    }

    public static TheoryData<string> ExecutableKeys()
    {
        var data = new TheoryData<string>();
        foreach (var recipe in SynapticFailureCatalog.Recipes.Where(r => r.Executable))
        {
            data.Add(recipe.Key.Value);
        }

        return data;
    }

    public static TheoryData<string> UnmappedKeys()
    {
        var data = new TheoryData<string>();
        foreach (var recipe in SynapticFailureCatalog.Recipes.Where(r => !r.Executable))
        {
            data.Add(recipe.Key.Value);
        }

        return data;
    }

    // -- Catalog parity --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_synaptic_catalog_has_exactly_the_fenix_keys_display_names_categories_and_targets()
    {
        var fenix = new FenixAircraftProvider(fenixOptions: new FenixOptions(), efbHttpClient: new HttpClient(new NoEfb()));
        await using var fenixSession = await fenix.AttachAsync(Descriptors.FenixA320);
        var reference = fenixSession.Capabilities.Failures.Catalog;
        var synaptic = SynapticFailureProvider.Capabilities.Catalog;

        Assert.Equal(384, reference.Count);
        Assert.Equal(reference.Count, synaptic.Count);
        Assert.Equal(reference.Select(d => d.Key.Value).Order(), synaptic.Select(d => d.Key.Value).Order());
        foreach (var expected in reference)
        {
            Assert.True(synaptic.TryGet(expected.Key, out var actual), expected.Key.Value);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Category, actual.Category);
            Assert.Equal(expected.SupportedTargets, actual.SupportedTargets);
        }
    }

    /// <summary>The 40 keys normalized before 0.12.0-preview.4 (frozen in tests/FSGAP.Fenix.Tests/Contract).</summary>
    private static readonly HashSet<string> OriginalKeys =
    [
        "air-conditioning.cpc.1", "air-conditioning.pack.1.overheat", "air-conditioning.pack.1.regulator-fault", "electrical.static-inverter",
        "electrical.generator.1", "electrical.generator.2", "electrical.bus.ac-1", "electrical.bus.ac-ess", "electrical.bus.dc-1",
        "electrical.bus.dc-2", "electrical.bus.dc-bat", "fire.lavatory.smoke", "fire.engine.1.loop-a", "fire.fdu.1", "fuel.fqi.channel-2",
        "fuel.pump.left-1", "fuel.pump.right-1", "hydraulic.blue.electric-pump", "hydraulic.yellow.electric-pump", "hydraulic.blue.low-level",
        "hydraulic.green.low-level", "hydraulic.blue.leak", "hydraulic.green.leak", "ice-rain.aoa-heat.standby", "ice-rain.pitot-heat.fo",
        "indicating.display.ecam-lower", "landing-gear.brake.wheel-1", "landing-gear.tyre-pressure.main-1", "landing-gear.tyre-pressure.right-1",
        "navigation.fmgc.1", "navigation.mcdu.1.recoverable-fault", "navigation.adf.1", "navigation.gps.1", "navigation.ils.1.localizer",
        "pneumatic.bleed-valve.1", "doors.entry.forward-left", "doors.entry.aft-left", "engine.1.surge", "engine.1.vibration.n1", "engine.1.eiu",
    ];

    [Fact]
    public void The_executable_recipes_are_unchanged_and_all_on_original_keys()
    {
        var recipes = SynapticFailureCatalog.Recipes;
        var executable = recipes.Where(r => r.Executable).ToArray();

        Assert.Equal(40, OriginalKeys.Count);
        Assert.Equal(17, executable.Length);
        Assert.All(executable, r => Assert.Contains(r.Key.Value, OriginalKeys));
        Assert.Equal(2, executable.Count(r => r.Qualification == SynapticFailureQualification.Validated));
        Assert.Equal(5, executable.Count(r => r.Qualification == SynapticFailureQualification.Assumed));
        Assert.Equal(10, executable.Count(r => r.Qualification == SynapticFailureQualification.Approximation));
    }

    [Fact]
    public void Every_key_added_in_preview_4_is_listed_but_not_executable()
    {
        var added = SynapticFailureCatalog.Recipes.Where(r => !OriginalKeys.Contains(r.Key.Value)).ToArray();

        Assert.Equal(344, added.Length);
        Assert.All(added, r =>
        {
            Assert.False(r.Executable);
            Assert.Empty(r.Controls);
            Assert.Equal(FailureOperations.None, r.Definition.Operations);
            Assert.Equal(SynapticFailureQualification.Unmapped, r.Qualification);
            Assert.False(SynapticFailureProvider.Capabilities.CanTrigger(r.Key));
        });
    }

    [Fact]
    public void Every_key_has_exactly_one_recipe_and_the_executable_ones_support_trigger_and_clear()
    {
        var recipes = SynapticFailureCatalog.Recipes;

        Assert.Equal(384, recipes.Count);
        Assert.Equal(recipes.Count, recipes.Select(r => r.Key).Distinct().Count());
        Assert.Equal(17, recipes.Count(r => r.Executable));
        Assert.Equal(367, recipes.Count(r => !r.Executable));
        Assert.All(recipes.Where(r => r.Executable), r => Assert.Equal(FailureOperations.Trigger | FailureOperations.Clear, r.Definition.Operations));
        Assert.All(recipes.Where(r => !r.Executable), r =>
        {
            Assert.Equal(FailureOperations.None, r.Definition.Operations);
            Assert.Equal(SynapticFailureQualification.Unmapped, r.Qualification);
            Assert.Equal(SynapticFailureMechanism.None, r.Mechanism);
        });
        Assert.All(recipes.Where(r => r.Executable), r => Assert.NotEqual(SynapticFailureQualification.Unmapped, r.Qualification));
        Assert.True(SynapticFailureProvider.Capabilities.CanReadActiveFailures);
        Assert.True(SynapticFailureProvider.Capabilities.CanTriggerAny);
        Assert.True(SynapticFailureProvider.Capabilities.CanClearAny);
    }

    [Fact]
    public void The_live_validated_recipes_reuse_the_degradation_controls()
    {
        var validated = SynapticFailureCatalog.Recipes.Where(r => r.Qualification == SynapticFailureQualification.Validated).ToArray();

        Assert.Equal(["electrical.generator.1", "hydraulic.blue.electric-pump"], validated.Select(r => r.Key.Value));
        Assert.All(validated, r =>
        {
            Assert.Equal(SynapticFailureMechanism.ControlledDegradation, r.Mechanism);
            Assert.Contains(SynapticDegradationControls.Controls, d => ReferenceEquals(d.Control, r.Controls.Single()));
            Assert.Equal(SynapticControlEvidence.LiveValidated, r.Controls.Single().Evidence);
        });
        Assert.Same(SynapticControls.LeftPackOff, SynapticFailureCatalog.Find(FailureKey.Parse("air-conditioning.pack.1.overheat"))!.Controls.Single());
    }

    [Fact]
    public void Public_definitions_carry_no_raw_variable_name()
    {
        foreach (var d in SynapticFailureProvider.Capabilities.Catalog)
        {
            var text = $"{d.Key} {d.DisplayName}";
            Assert.DoesNotContain("A22X", text, StringComparison.Ordinal);
            Assert.DoesNotContain("L:", text, StringComparison.Ordinal);
        }
    }

    // -- Trigger / clear / read active -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ExecutableKeys))]
    public async Task Every_executable_recipe_writes_its_degraded_values_reads_active_and_clears_back(string key)
    {
        await using var rig = new Rig();
        var recipe = SynapticFailureCatalog.Find(FailureKey.Parse(key))!;
        var target = recipe.Definition.SupportedTargets[0];

        var trigger = await rig.Failures.TriggerAsync(Cmd(key, target));

        Assert.Equal(FailureCommandStatus.Succeeded, trigger.Status);
        Assert.Equal(recipe.Controls.Select(c => (c.Variable.Name, c.Degraded)), rig.Writes);
        var active = Assert.Single(await rig.Failures.GetActiveFailuresAsync());
        Assert.Equal(recipe.Key, active.Key);
        Assert.Equal(target, active.Target);
        Assert.Equal(recipe.Definition.Category, active.Category);

        var clear = await rig.Failures.ClearAsync(Cmd(key, target));

        Assert.Equal(FailureCommandStatus.Succeeded, clear.Status);
        Assert.Equal(
            recipe.Controls.Select(c => (c.Variable.Name, c.Degraded)).Concat(recipe.Controls.Select(c => (c.Variable.Name, c.Normal ?? LowerBrightness))),
            rig.Writes);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Failures.OwnedKeys);
    }

    [Theory]
    [MemberData(nameof(UnmappedKeys))]
    public async Task Unmapped_keys_are_listed_but_answer_not_supported_without_contacting_the_aircraft(string key)
    {
        await using var rig = new Rig();
        var target = SynapticFailureCatalog.Find(FailureKey.Parse(key))!.Definition.SupportedTargets[0];

        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.TriggerAsync(Cmd(key, target))).Status);
        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.ClearAsync(Cmd(key, target))).Status);
        Assert.False(SynapticFailureProvider.Capabilities.CanTrigger(FailureKey.Parse(key)));
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task An_unsupported_target_or_unknown_key_is_not_supported()
    {
        await using var rig = new Rig();

        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.TriggerAsync(Cmd("fuel.pump.left-1", FailureTarget.FuelPump("right-1")))).Status);
        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.TriggerAsync(Cmd("engine.2.surge"))).Status);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task The_lower_display_recipe_restores_the_brightness_it_found()
    {
        await using var rig = new Rig(configure: r => r.Values["L:A22X Lower Brightness"] = 0.41);

        await rig.Failures.TriggerAsync(Cmd("indicating.display.ecam-lower"));
        await rig.Failures.ClearAsync(Cmd("indicating.display.ecam-lower"));

        Assert.Equal([("L:A22X Lower Brightness", 0.0), ("L:A22X Lower Brightness", 0.41)], rig.Writes);
    }

    // -- Several failures, conflicts -------------------------------------------------------------------------------------

    [Fact]
    public async Task Independent_failures_can_be_active_together()
    {
        await using var rig = new Rig();

        Assert.True((await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd("fuel.pump.left-1", FailureTarget.FuelPump("left-1")))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd("pneumatic.bleed-valve.1"))).IsSuccess);

        var active = await rig.Failures.GetActiveFailuresAsync();
        Assert.Equal(["electrical.generator.2", "fuel.pump.left-1", "pneumatic.bleed-valve.1"], active.Select(a => a.Key!.Value).Order());
    }

    [Theory]
    [InlineData("electrical.generator.1", "engine.1.eiu")]
    [InlineData("pneumatic.bleed-valve.1", "engine.1.eiu")]
    [InlineData("hydraulic.blue.electric-pump", "hydraulic.blue.leak")]
    [InlineData("hydraulic.blue.leak", "hydraulic.blue.low-level")]
    [InlineData("hydraulic.green.low-level", "hydraulic.green.leak")]
    public async Task A_failure_needing_a_control_held_by_another_is_rejected_without_writing(string first, string second)
    {
        await using var rig = new Rig();
        var firstTarget = SynapticFailureCatalog.Find(FailureKey.Parse(first))!.Definition.SupportedTargets[0];
        var secondTarget = SynapticFailureCatalog.Find(FailureKey.Parse(second))!.Definition.SupportedTargets[0];
        Assert.True((await rig.Failures.TriggerAsync(Cmd(first, firstTarget))).IsSuccess);
        var writes = rig.Writes.Length;

        var result = await rig.Failures.TriggerAsync(Cmd(second, secondTarget));

        Assert.Equal(FailureCommandStatus.Rejected, result.Status);
        Assert.Contains($"failure:{first}", result.Message);
        Assert.Equal(writes, rig.Writes.Length);
    }

    [Fact]
    public async Task Failures_and_degradations_never_share_a_control()
    {
        await using var rig = new Rig();
        Assert.True((await rig.Degradations.ApplyAsync(DegradationKey.Parse("electrical.generator.1.forced-off"))).IsSuccess);

        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.TriggerAsync(Cmd("electrical.generator.1"))).Status);
        Assert.True((await rig.Degradations.RestoreAsync(DegradationKey.Parse("electrical.generator.1.forced-off"))).IsSuccess);

        Assert.True((await rig.Failures.TriggerAsync(Cmd("electrical.generator.1"))).IsSuccess);
        var degradation = await rig.Degradations.ApplyAsync(DegradationKey.Parse("electrical.generator.1.forced-off"));
        Assert.Equal(DegradationCommandStatus.Rejected, degradation.Status);
        Assert.Equal(DegradationState.PreExisting, degradation.State);
        Assert.Equal(DegradationState.PreExisting, await rig.Degradations.GetStateAsync(DegradationKey.Parse("electrical.generator.1.forced-off")));
        Assert.Equal(DegradationCommandStatus.Rejected, (await rig.Degradations.RestoreAsync(DegradationKey.Parse("electrical.generator.1.forced-off"))).Status);
    }

    // -- Ownership --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_composite_recipe_leaves_a_pre_existing_control_alone_and_restores_only_what_it_changed()
    {
        await using var rig = new Rig(configure: r => r.Values["L:A22X ACMP 3B"] = 0); // pump 3B already off (pilot)

        Assert.True((await rig.Failures.TriggerAsync(Cmd("hydraulic.blue.leak", FailureTarget.HydraulicSystem("blue")))).IsSuccess);
        Assert.Equal([("L:A22X ACMP 3A", 0.0)], rig.Writes);
        Assert.Single(await rig.Failures.GetActiveFailuresAsync());

        Assert.True((await rig.Failures.ClearAsync(Cmd("hydraulic.blue.leak", FailureTarget.HydraulicSystem("blue")))).IsSuccess);
        Assert.Equal([("L:A22X ACMP 3A", 0.0), ("L:A22X ACMP 3A", 1.0)], rig.Writes);
        Assert.Equal(0, rig.Reader.Values["L:A22X ACMP 3B"]);
    }

    [Fact]
    public async Task A_configuration_entirely_set_outside_fsgap_is_never_claimed_nor_cleared()
    {
        await using var rig = new Rig(configure: r => r.Values["L:A22X R Gen Off"] = 1);

        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).Status);
        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.ClearAsync(Cmd("electrical.generator.2"))).Status);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task An_unexpected_control_value_is_never_written()
    {
        await using var rig = new Rig(configure: r => r.Values["L:A22X ACMP 2B"] = 2);

        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.TriggerAsync(Cmd("hydraulic.yellow.electric-pump", FailureTarget.HydraulicSystem("yellow")))).Status);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task A_pilot_restore_makes_the_failure_inactive_and_nothing_is_rewritten()
    {
        await using var rig = new Rig();
        await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"));
        rig.Reader.Values["L:A22X R Gen Off"] = 0; // the pilot switched it back on

        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Failures.OwnedKeys);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd("electrical.generator.2"))).Status);
        Assert.Single(rig.Writes);
    }

    [Fact]
    public async Task Clearing_an_inactive_failure_succeeds_without_writing()
    {
        await using var rig = new Rig();

        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd("electrical.generator.2"))).Status);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task Triggering_twice_writes_once()
    {
        await using var rig = new Rig();

        await rig.Failures.TriggerAsync(Cmd("pneumatic.bleed-valve.1"));
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd("pneumatic.bleed-valve.1"))).Status);

        Assert.Single(rig.Writes);
    }

    // -- Failure modes ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_composite_write_cut_halfway_is_unconfirmed_and_what_was_written_can_be_cleared()
    {
        await using var rig = new Rig();
        rig.Writer.FailAfter = 1;

        var trigger = await rig.Failures.TriggerAsync(Cmd("engine.1.eiu", FailureTarget.Engine(1)));

        Assert.Equal(FailureCommandStatus.Unconfirmed, trigger.Status);
        Assert.Equal([("L:A22X L Gen Off", 1.0)], rig.Writes);
        Assert.Equal([FailureKey.Parse("engine.1.eiu")], rig.Failures.OwnedKeys);

        rig.Writer.FailAfter = null;
        Assert.True((await rig.Failures.ClearAsync(Cmd("engine.1.eiu", FailureTarget.Engine(1)))).IsSuccess);
        Assert.Equal([("L:A22X L Gen Off", 1.0), ("L:A22X L Gen Off", 0.0)], rig.Writes);
        Assert.Equal(0, rig.Reader.Values["L:A22X L Bleed Off"]);
    }

    [Fact]
    public async Task A_write_the_aircraft_does_not_take_is_never_a_success()
    {
        await using var rig = new Rig();
        rig.Writer.Ignore = true;

        var trigger = await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"));

        Assert.Equal(FailureCommandStatus.Unconfirmed, trigger.Status);
        Assert.Equal([FailureKey.Parse("electrical.generator.2")], rig.Failures.OwnedKeys);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync()); // the control never left normal
    }

    [Fact]
    public async Task Without_a_readable_aircraft_nothing_is_written_and_active_failures_are_unavailable()
    {
        await using var rig = new Rig();
        await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"));
        rig.Reader.Failure = new InvalidOperationException("The simulator is not connected.");

        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.TriggerAsync(Cmd("pneumatic.bleed-valve.1"))).Status);
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Failures.GetActiveFailuresAsync());
        Assert.Single(rig.Writes);
    }

    [Fact]
    public async Task A_disconnection_drops_ownership_and_a_replaced_aircraft_is_never_written()
    {
        var detector = new StreamingDetector(Descriptors.AirFrance);
        await using var rig = new Rig(detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the board watch");
        await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"));

        detector.Publish(null);
        await Wait.UntilAsync(() => rig.Failures.OwnedKeys.Count == 0, "the ownership drop");
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Failures.GetActiveFailuresAsync());
        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.ClearAsync(Cmd("electrical.generator.2"))).Status);

        detector.Publish(Descriptors.AirFrance);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.ClearAsync(Cmd("electrical.generator.2"))).Status);
        Assert.Single(rig.Writes);
    }

    // -- Disposal ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Disposing_clears_what_the_session_triggered()
    {
        var rig = new Rig();
        await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"));
        await rig.Failures.TriggerAsync(Cmd("hydraulic.blue.leak", FailureTarget.HydraulicSystem("blue")));

        await rig.DisposeAsync();

        Assert.Equal(0, rig.Reader.Values["L:A22X R Gen Off"]);
        Assert.Equal(1, rig.Reader.Values["L:A22X ACMP 3A"]);
        Assert.Equal(1, rig.Reader.Values["L:A22X ACMP 3B"]);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Failures.GetActiveFailuresAsync());
        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).Status);
    }

    [Fact]
    public async Task Disposing_after_an_aircraft_change_writes_nothing()
    {
        var detector = new StreamingDetector(Descriptors.AirFrance);
        var rig = new Rig(detector);
        await rig.Failures.TriggerAsync(Cmd("pneumatic.bleed-valve.1"));
        detector.Publish(Descriptors.Delta);

        await rig.DisposeAsync();

        Assert.Single(rig.Writes);
    }

    // -- Session ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_session_shares_one_board_between_failures_and_degradations_and_clears_both_on_dispose()
    {
        var reader = new FakeVariableReader();
        foreach (var control in SynapticControls.All)
        {
            reader.Values[control.Variable.Name] = control.Normal ?? LowerBrightness;
        }

        var writer = new FakeVariableWriter(reader);
        var provider = new SynapticAircraftProvider(simulatorVariables: reader, simulatorVariableWriter: writer);
        IAircraftSession session = await provider.AttachAsync(Descriptors.AirFrance);

        Assert.IsType<SynapticFailureProvider>(session.Failures);
        Assert.True((await session.Degradations.ApplyAsync(DegradationKey.Parse("flight-controls.pfcc.1.forced-off"))).IsSuccess);
        Assert.True((await session.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
        Assert.True((await session.Failures.TriggerAsync(Cmd("electrical.generator.1"))).IsSuccess); // independent of the PFCC degradation

        await session.DisposeAsync();

        Assert.Equal(0, reader.Values["L:A22X PFCC 1 Off"]);
        Assert.Equal(0, reader.Values["L:A22X R Gen Off"]);
        Assert.Equal(0, reader.Values["L:A22X L Gen Off"]);
    }

    /// <summary>The Fenix provider only builds its catalog here; no EFB call is expected.</summary>
    private sealed class NoEfb : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
