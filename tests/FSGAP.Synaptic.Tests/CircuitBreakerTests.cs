using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Synaptic.CircuitBreakers;
using FSGAP.Synaptic.Failures;
using FSGAP.Synaptic.Systems;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic.Tests;

/// <summary>
/// 0.13.0-preview.4: the 55 Synaptic A220 circuit breakers. These tests prove the manifest against the lab evidence and the
/// mechanics on a fake simulator (trip = cell 1, reset = cell 0, both read back); they never inject in MSFS.
/// </summary>
public class CircuitBreakerTests
{
    private const string Dome = "electrical.circuit-breaker.lights-dome";
    private const string DomeCell = "L:A22X Circuit Breaker R B4";
    private const string Iru = "electrical.circuit-breaker.nav-iru";

    private static readonly SynapticControlBoard.Timing Fast = new(TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(2));

    private static readonly SynapticCircuitBreakerManifest Manifest = SynapticCircuitBreakerManifest.Embedded;

    private static SynapticCircuitBreakerOptions Options(params string[] qualifiedTrips) => new() { QualifiedTrips = qualifiedTrips };

    /// <summary>Every breaker qualified: the mechanics are proven on all 55, independently of the live qualification.</summary>
    private static SynapticCircuitBreakerOptions AllEnabled() => Options(Manifest.Breakers.Select(b => b.Id).ToArray());

    private static SynapticCircuitBreakerStatus StatusOf(SynapticFailureProvider failures, string id) =>
        failures.CircuitBreakerStatuses().Single(b => b.Id == id);

    private static FailureCommand Cmd(string key) => new(FailureKey.Parse(key));

    private static string CellOf(string key) => SynapticFailureCatalog.Find(FailureKey.Parse(key))!.CircuitBreaker!.Control.Variable.Name;

    private sealed class Rig : IAsyncDisposable
    {
        public Rig(SynapticCircuitBreakerOptions? options, IAircraftDetector? detector = null, Action<FakeVariableReader>? configure = null)
        {
            // Normal configuration: every historical control at its normal value, every breaker in (cell 0).
            foreach (var control in SynapticControls.All)
            {
                Reader.Values[control.Variable.Name] = control.Normal ?? 0.5;
            }

            foreach (var control in SynapticControls.CircuitBreakers)
            {
                Reader.Values[control.Variable.Name] = 0;
            }

            configure?.Invoke(Reader);
            Writer = new FakeVariableWriter(Reader);
            Board = new SynapticControlBoard(Reader, Writer, detector, Descriptors.AirFrance, NullLogger.Instance, Fast);
            Failures = new SynapticFailureProvider(Board, ownsBoard: false, SynapticCircuitBreakerActivation.From(options, Manifest));
        }

        public FakeVariableReader Reader { get; } = new();

        public FakeVariableWriter Writer { get; }

        public SynapticControlBoard Board { get; }

        public SynapticFailureProvider Failures { get; }

        public (string, double)[] Writes => Writer.Writes.Select(w => (w.Variable.Name, w.Value)).ToArray();

        public async ValueTask DisposeAsync()
        {
            await Failures.DisposeAsync();
            await Board.DisposeAsync();
        }
    }

    public static TheoryData<string> AllBreakerKeys()
    {
        var data = new TheoryData<string>();
        foreach (var recipe in SynapticFailureCatalog.CircuitBreakerRecipes)
        {
            data.Add(recipe.Key.Value);
        }

        return data;
    }

    public static TheoryData<string> UnqualifiedBreakerKeys()
    {
        var data = new TheoryData<string>();
        foreach (var recipe in SynapticFailureCatalog.CircuitBreakerRecipes.Where(r => r.CircuitBreaker!.Trip == SynapticBreakerQualification.Unqualified))
        {
            data.Add(recipe.Key.Value);
        }

        return data;
    }

    // -- Manifest ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_manifest_lists_55_breakers_with_unique_ids_keys_cells_and_names()
    {
        var breakers = Manifest.Breakers;

        Assert.Equal(55, breakers.Count);
        Assert.Equal(55, breakers.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(55, breakers.Select(b => b.Key).Distinct().Count());
        Assert.Equal(55, breakers.Select(b => b.Control.Variable.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(55, breakers.Select(b => b.DisplayName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(breakers, b =>
        {
            Assert.StartsWith("electrical.circuit-breaker.", b.Key.Value, StringComparison.Ordinal);
            Assert.Matches(@"^L:A22X Circuit Breaker [LR] [A-I][1-5]$", b.Control.Variable.Name);
            Assert.Equal("number", b.Control.Variable.Unit);
            Assert.Equal(0, b.Control.Normal);
            Assert.Equal(1, b.Control.Degraded);
            Assert.Equal(SynapticBreakerQualification.LabValidated, b.Mapping);
            Assert.Equal(SynapticBreakerQualification.LabValidated, b.Reset);
        });
        Assert.Equal("1.0.11", Manifest.SynapticVersion);
        Assert.Equal(Manifest.SynapticVersion, SynapticCircuitBreakerOptions.QualifiedOnAircraftVersion);
    }

    [Fact]
    public void Only_the_dome_breaker_has_a_live_qualified_direct_trip()
    {
        var qualified = Assert.Single(Manifest.Breakers, b => b.Trip == SynapticBreakerQualification.LabValidated);

        Assert.Equal("CB_LIGHTS_DOME", qualified.Id);
        Assert.Equal(DomeCell, qualified.Control.Variable.Name);
        Assert.Equal(54, Manifest.Breakers.Count(b => b.Trip == SynapticBreakerQualification.Unqualified));
    }

    [Fact]
    public void The_manifest_is_exactly_the_lab_mapping_and_qualification()
    {
        var mapping = Evidence("A220_CB_MAPPING_55.json")["mappings"]!.AsArray();
        var automap = Evidence("A220_CB_AUTO_QUALIFICATION.json")["breakers"]!.AsArray();
        var direct = Evidence("A220_CB_DIRECT_L_TRIP_RESULTS.json");
        var capabilities = Evidence("FSGAP_CB_TRIP_CAPABILITIES.json")["breakers"]!.AsArray();

        // 55 exact correspondences: id <- input event name, cell <- the single cell the event flipped.
        var expected = mapping.Select(m => ("CB_" + m!["b"]!.GetValue<string>()["AIRLINER_CB_".Length..], m["l"]!.GetValue<string>())).Order().ToArray();
        var actual = Manifest.Breakers.Select(b => (b.Id, b.Control.Variable.Name)).Order().ToArray();
        Assert.Equal(55, expected.Length);
        Assert.Equal(expected, actual);

        // Mapping and reset: every breaker passed the automatic qualification, re-armed to 0, nothing else changed.
        Assert.Equal(55, automap.Count);
        Assert.All(automap, run =>
        {
            Assert.True(run!["mapping_pass"]!.GetValue<bool>());
            Assert.True(run["rearm_pass"]!.GetValue<bool>());
            Assert.True(run["others_unchanged"]!.GetValue<bool>());
            Assert.Equal(0, run["cell_after_rearm"]!.GetValue<int>());
            var id = "CB_" + run["name"]!.GetValue<string>()["AIRLINER_CB_".Length..];
            Assert.Equal(run["l_cell"]!.GetValue<string>(), Manifest.FindById(id)!.Control.Variable.Name);
        });

        // Direct trip: qualified exactly where the direct run confirmed a clean, restored open with no input event.
        Assert.Equal(0, direct["b_writes"]!.GetValue<int>());
        Assert.True(direct["physical_open_confirmed"]!.GetValue<bool>());
        Assert.True(direct["restored"]!.GetValue<bool>());
        Assert.Equal(direct["target_cell"]!.GetValue<string>(), Assert.Single(Manifest.Breakers, b => b.Trip == SynapticBreakerQualification.LabValidated).Control.Variable.Name);

        // Master cautions as recorded.
        Assert.Equal(
            capabilities.Where(c => c!["master_caution"]!.GetValue<bool>()).Select(c => c!["id"]!.GetValue<string>()).Order(),
            Manifest.Breakers.Where(b => b.MasterCaution).Select(b => b.Id).Order());
    }

    [Fact]
    public void The_cells_are_exactly_the_55_documented_circuit_breaker_variables()
    {
        var documented = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Evidence", "synaptic-a220-variable-mapping.csv"))
            .Select(line => line.Split(',')[1].Trim('"'))
            .Where(variable => variable.StartsWith("L:A22X Circuit Breaker ", StringComparison.Ordinal))
            .Order()
            .ToArray();

        Assert.Equal(55, documented.Length);
        Assert.Equal(documented, Manifest.Breakers.Select(b => b.Control.Variable.Name).Order());
    }

    [Fact]
    public void The_manifest_records_the_hash_of_every_evidence_file()
    {
        var evidence = EmbeddedManifestJson()["evidence"]!.AsObject();

        Assert.Equal(5, evidence.Count);
        foreach (var (file, hash) in evidence)
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Evidence", file));
            Assert.Equal(hash!.GetValue<string>(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    [Theory]
    [InlineData("duplicate cell")]
    [InlineData("missing breaker")]
    [InlineData("foreign key")]
    [InlineData("foreign cell")]
    [InlineData("unknown qualification")]
    [InlineData("raw name in display name")]
    public void An_inconsistent_manifest_is_refused(string defect)
    {
        var json = EmbeddedManifestJson();
        var breakers = json["breakers"]!.AsArray();
        switch (defect)
        {
            case "duplicate cell":
                breakers[1]!["cell"] = breakers[0]!["cell"]!.GetValue<string>();
                break;
            case "missing breaker":
                breakers.RemoveAt(0);
                break;
            case "foreign key":
                breakers[0]!["failureKey"] = "electrical.generator.1";
                break;
            case "foreign cell":
                breakers[0]!["cell"] = "L:A22X L Gen Off";
                break;
            case "unknown qualification":
                breakers[0]!["trip"] = "Assumed";
                break;
            case "raw name in display name":
                breakers[0]!["displayName"] = "L:A22X breaker";
                break;
        }

        Assert.Throws<InvalidOperationException>(() => SynapticCircuitBreakerManifest.Parse(json.ToJsonString()));
    }

    // -- Catalog and activation: qualification and application choice, never the Synaptic version ------------------------

    [Fact]
    public void The_55_breakers_are_listed_and_by_default_only_the_trip_qualified_one_is_executable()
    {
        var capabilities = SynapticFailureProvider.DefaultCapabilities;

        Assert.Equal(55, SynapticFailureCatalog.CircuitBreakerRecipes.Count);
        Assert.All(SynapticFailureCatalog.CircuitBreakerRecipes, r =>
        {
            Assert.True(capabilities.Catalog.TryGet(r.Key, out var definition));
            Assert.Equal(r.Key.Value == Dome ? FailureOperations.Trigger | FailureOperations.Clear : FailureOperations.None, definition.Operations);
            Assert.Equal(r.Key.Value == Dome, capabilities.CanTrigger(r.Key));
            Assert.Equal(FailureCategory.Electrical, definition.Category);
            Assert.Equal([FailureTarget.Aircraft], definition.SupportedTargets);
            Assert.False(r.Executable);
            Assert.True(r.RequiresActivation);
            Assert.Equal(SynapticFailureMechanism.CircuitBreakerTrip, r.Mechanism);
        });
    }

    [Theory]
    [MemberData(nameof(UnqualifiedBreakerKeys))]
    public async Task An_unqualified_breaker_is_listed_but_never_executed_even_with_a_readable_cell(string key)
    {
        await using var rig = new Rig(options: null);

        var trigger = await rig.Failures.TriggerAsync(Cmd(key));
        var clear = await rig.Failures.ClearAsync(Cmd(key));

        Assert.Equal(FailureCommandStatus.NotSupported, trigger.Status);
        Assert.Equal(FailureCommandStatus.NotSupported, clear.Status);
        Assert.Contains("not qualified live", trigger.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Empty(rig.Writes);
        var status = rig.Failures.CircuitBreakerStatuses().Single(b => b.Key == FailureKey.Parse(key));
        Assert.True(status.Listed);
        Assert.False(status.Executable);
        Assert.Equal(SynapticBreakerQualification.Unqualified, status.TripQualification);
    }

    [Fact]
    public async Task A_breaker_qualified_by_the_application_becomes_executable_without_a_new_sdk()
    {
        await using var rig = new Rig(Options("CB_NAV_IRU"));

        Assert.True(rig.Failures.Capabilities.CanTrigger(FailureKey.Parse(Iru)));
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Equal([(CellOf(Iru), 1.0)], rig.Writes);
    }

    [Theory]
    [InlineData("1.0.11")]
    [InlineData("1.0.12")]
    [InlineData("1.1.0")]
    [InlineData("2.0.0")]
    public async Task A_synaptic_update_with_unchanged_interfaces_keeps_the_breakers_working(string version)
    {
        await using var rig = new Rig(new SynapticCircuitBreakerOptions { AircraftVersion = version, QualifiedTrips = ["CB_NAV_IRU"] });

        Assert.True(rig.Failures.Capabilities.CanTrigger(FailureKey.Parse(Dome)));
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Equal([(DomeCell, 1.0), (CellOf(Iru), 1.0), (DomeCell, 0.0)], rig.Writes);
    }

    [Fact]
    public async Task An_unknown_version_with_accessible_cells_keeps_the_breakers_working()
    {
        await using var rig = new Rig(new SynapticCircuitBreakerOptions { AircraftVersion = null });

        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        var status = StatusOf(rig.Failures, "CB_LIGHTS_DOME");
        Assert.Equal(SynapticBreakerInterface.Accessible, status.Interface);
        Assert.Equal(SynapticBreakerExecution.Succeeded, status.LastExecution);
    }

    [Fact]
    public void The_reported_version_is_diagnostic_only()
    {
        var reference = SynapticCircuitBreakerActivation.From(Options("CB_NAV_IRU"), Manifest);
        foreach (var version in new[] { "1.0.11", "v1.0.12", "9.9.9" })
        {
            var activation = SynapticCircuitBreakerActivation.From(new SynapticCircuitBreakerOptions { AircraftVersion = version, QualifiedTrips = ["CB_NAV_IRU"] }, Manifest);
            Assert.Equal(reference.Enabled.Order(), activation.Enabled.Order());
        }

        Assert.Equal("1.0.12", SynapticCircuitBreakerActivation.From(new SynapticCircuitBreakerOptions { AircraftVersion = " v1.0.12 " }, Manifest).ReportedVersion);
        Assert.Null(SynapticCircuitBreakerActivation.From(null, Manifest).ReportedVersion);
        Assert.Equal("1.0.11", SynapticCircuitBreakerActivation.Default.QualifiedOnVersion);
        Assert.Equal(["CB_LIGHTS_DOME"], SynapticCircuitBreakerActivation.Default.Enabled);
    }

    [Fact]
    public async Task A_breaker_disabled_by_the_application_is_listed_but_never_executed_and_the_others_are_not_affected()
    {
        await using var rig = new Rig(new SynapticCircuitBreakerOptions { QualifiedTrips = ["CB_NAV_IRU"], DisabledCircuitBreakers = ["CB_LIGHTS_DOME"] });

        var dome = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.NotSupported, dome.Status);
        Assert.Contains("the application disabled it", dome.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.True(StatusOf(rig.Failures, "CB_LIGHTS_DOME").Listed);
    }

    [Fact]
    public async Task Opening_a_session_reads_no_breaker_cell_and_every_interface_starts_unverified()
    {
        var reader = new FakeVariableReader();
        var provider = new SynapticAircraftProvider(simulatorVariables: reader, simulatorVariableWriter: new FakeVariableWriter(reader), circuitBreakers: AllEnabled());

        await using var session = await provider.AttachAsync(Descriptors.AirFrance);
        _ = session.Capabilities.Failures.Catalog.Count(d => session.Capabilities.Failures.CanTrigger(d.Key));

        Assert.Equal(0, reader.ReadsWhere(v => v.Name.StartsWith("L:A22X Circuit Breaker ", StringComparison.Ordinal)));
        Assert.All(((SynapticFailureProvider)session.Failures).CircuitBreakerStatuses(), b => Assert.Equal(SynapticBreakerInterface.Unverified, b.Interface));
    }

    [Fact]
    public void Invalid_options_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new SynapticCircuitBreakerOptions { QualifiedTrips = ["CB_DOES_NOT_EXIST"] }.Validate());
        Assert.Throws<ArgumentException>(() => new SynapticCircuitBreakerOptions { DisabledCircuitBreakers = ["CB_DOES_NOT_EXIST"] }.Validate());
        Assert.Throws<ArgumentException>(() => new SynapticCircuitBreakerOptions { AircraftVersion = " " }.Validate());
        Assert.Throws<ArgumentException>(() => new SynapticAircraftProvider(circuitBreakers: Options("CB_DOES_NOT_EXIST")));
    }

    // -- Trip / read / reset on all 55 ------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllBreakerKeys))]
    public async Task Every_breaker_trips_with_its_cell_reads_active_and_resets_with_its_cell(string key)
    {
        await using var rig = new Rig(AllEnabled());
        var cell = CellOf(key);

        var trip = await rig.Failures.TriggerAsync(Cmd(key));

        Assert.Equal(FailureCommandStatus.Succeeded, trip.Status);
        Assert.Equal([(cell, 1.0)], rig.Writes);
        Assert.Equal(1, rig.Reader.Values[cell]);
        var active = Assert.Single(await rig.Failures.GetActiveFailuresAsync());
        Assert.Equal(FailureKey.Parse(key), active.Key);
        Assert.Equal(FailureTarget.Aircraft, active.Target);
        Assert.Equal(FailureCategory.Electrical, active.Category);

        var reset = await rig.Failures.ClearAsync(Cmd(key));

        Assert.Equal(FailureCommandStatus.Succeeded, reset.Status);
        Assert.Equal([(cell, 1.0), (cell, 0.0)], rig.Writes);
        Assert.Equal(0, rig.Reader.Values[cell]);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Failures.OwnedKeys);
    }

    [Fact]
    public async Task Trip_and_reset_read_the_cell_before_writing_and_read_it_back()
    {
        await using var rig = new Rig(Options());

        await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.True(rig.Reader.ReadsWhere(v => v.Name == DomeCell) >= 2); // read before the write, then read back
        Assert.All(rig.Reader.Reads, read => Assert.All(read, v => Assert.Equal(DomeCell, v.Name)));
    }

    [Fact]
    public async Task Tripping_twice_or_resetting_twice_writes_once()
    {
        await using var rig = new Rig(Options());

        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);

        Assert.Equal([(DomeCell, 1.0), (DomeCell, 0.0)], rig.Writes);
    }

    [Fact]
    public async Task Several_breakers_can_be_tripped_together_and_each_resets_only_its_own_cell()
    {
        await using var rig = new Rig(AllEnabled());

        Assert.True((await rig.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd(Iru))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
        Assert.Equal([Dome, Iru, "electrical.generator.2"], (await rig.Failures.GetActiveFailuresAsync()).Select(a => a.Key!.Value).Order());

        Assert.True((await rig.Failures.ClearAsync(Cmd(Dome))).IsSuccess);

        Assert.Equal(0, rig.Reader.Values[DomeCell]);
        Assert.Equal(1, rig.Reader.Values[CellOf(Iru)]);
        Assert.Equal(1, rig.Reader.Values["L:A22X R Gen Off"]);
    }

    // -- Confirmation, connection, ambiguous restore ----------------------------------------------------------------------

    [Fact]
    public async Task A_trip_the_aircraft_does_not_take_is_unconfirmed_never_a_success()
    {
        await using var rig = new Rig(Options());
        rig.Writer.Ignore = true;

        var trip = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Unconfirmed, trip.Status);
        Assert.Equal([FailureKey.Parse(Dome)], rig.Failures.OwnedKeys); // kept, so it can still be reset
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync()); // the cell never left 0
    }

    [Fact]
    public async Task An_ambiguous_reset_is_unconfirmed_keeps_the_breaker_owned_and_can_be_retried()
    {
        await using var rig = new Rig(Options());
        Assert.True((await rig.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        rig.Writer.Ignore = true;

        var reset = await rig.Failures.ClearAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Unconfirmed, reset.Status);
        Assert.Equal([FailureKey.Parse(Dome)], rig.Failures.OwnedKeys);
        Assert.Single(await rig.Failures.GetActiveFailuresAsync()); // still tripped: never reported as reset

        rig.Writer.Ignore = false;
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Equal(0, rig.Reader.Values[DomeCell]);
        Assert.Empty(rig.Failures.OwnedKeys);
    }

    [Fact]
    public async Task A_lost_connection_writes_nothing_and_reports_unavailable()
    {
        await using var rig = new Rig(Options());
        rig.Reader.Failure = new InvalidOperationException("The simulator is not connected.");

        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Empty(rig.Writes);

        rig.Reader.Failure = null;
        rig.Writer.Failure = new InvalidOperationException("The simulator is not connected.");
        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Empty(rig.Failures.OwnedKeys);
    }

    [Fact]
    public async Task A_disconnection_drops_ownership_and_a_replaced_aircraft_is_never_written()
    {
        var detector = new StreamingDetector(Descriptors.AirFrance);
        await using var rig = new Rig(Options(), detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the board watch");
        Assert.True((await rig.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        detector.Publish(null);
        await Wait.UntilAsync(() => rig.Failures.OwnedKeys.Count == 0, "the ownership drop");
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Failures.GetActiveFailuresAsync());
        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);

        detector.Publish(Descriptors.Delta);
        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Single(rig.Writes);
    }

    // -- Ownership: what FSGAP did not trip it never resets ---------------------------------------------------------------

    [Fact]
    public async Task A_breaker_already_pulled_outside_fsgap_is_never_claimed_nor_reset()
    {
        await using var rig = new Rig(Options(), configure: r => r.Values[DomeCell] = 1);

        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task A_pilot_pushing_the_breaker_back_in_makes_it_inactive_and_nothing_is_rewritten()
    {
        await using var rig = new Rig(Options());
        await rig.Failures.TriggerAsync(Cmd(Dome));
        rig.Reader.Values[DomeCell] = 0; // the pilot re-armed it in the cockpit

        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Failures.OwnedKeys);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Single(rig.Writes);
    }

    [Fact]
    public async Task An_unexpected_cell_value_is_never_written()
    {
        await using var rig = new Rig(Options(), configure: r => r.Values[DomeCell] = 2);

        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task Disposing_resets_the_breakers_the_session_tripped()
    {
        var rig = new Rig(AllEnabled());
        await rig.Failures.TriggerAsync(Cmd(Dome));
        await rig.Failures.TriggerAsync(Cmd(Iru));

        await rig.DisposeAsync();

        Assert.Equal(0, rig.Reader.Values[DomeCell]);
        Assert.Equal(0, rig.Reader.Values[CellOf(Iru)]);
    }

    // -- Compatibility: a failing breaker is disabled alone ---------------------------------------------------------------

    [Fact]
    public async Task A_cell_that_became_unavailable_disables_only_its_breaker()
    {
        await using var rig = new Rig(AllEnabled(), configure: r => r.Values[DomeCell] = double.NaN);

        var first = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Rejected, first.Status);
        Assert.Contains("could not be read", first.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Writes);
        var status = StatusOf(rig.Failures, "CB_LIGHTS_DOME");
        Assert.Equal(SynapticBreakerInterface.Unavailable, status.Interface);
        Assert.False(status.Executable);
        Assert.Equal(SynapticBreakerExecution.Failed, status.LastExecution);

        var reads = rig.Reader.ReadCount;
        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(reads, rig.Reader.ReadCount);

        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).Status);
        Assert.Equal(54, rig.Failures.CircuitBreakerStatuses().Count(b => b.Executable));
    }

    [Fact]
    public async Task A_readable_cell_whose_write_is_ineffective_fails_explicitly_and_is_never_written_again_automatically()
    {
        await using var rig = new Rig(AllEnabled());
        rig.Writer.IgnoredVariables.Add(DomeCell);

        var trip = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Unconfirmed, trip.Status);
        Assert.Contains("disabled for this session", trip.Message, StringComparison.Ordinal);
        Assert.Equal([(DomeCell, 1.0)], rig.Writes);
        Assert.Equal(SynapticBreakerInterface.WriteIneffective, StatusOf(rig.Failures, "CB_LIGHTS_DOME").Interface);

        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal([(DomeCell, 1.0)], rig.Writes);

        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Equal([Iru], (await rig.Failures.GetActiveFailuresAsync()).Select(a => a.Key!.Value));
    }

    [Fact]
    public async Task A_cell_with_an_incoherent_value_disables_only_its_breaker()
    {
        await using var rig = new Rig(AllEnabled(), configure: r => r.Values[DomeCell] = 2);

        var trip = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Rejected, trip.Status);
        Assert.Empty(rig.Writes);
        Assert.Equal(SynapticBreakerInterface.IncoherentValue, StatusOf(rig.Failures, "CB_LIGHTS_DOME").Interface);
        Assert.Equal(FailureCommandStatus.NotSupported, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
    }

    [Fact]
    public async Task An_open_breaker_is_not_written_and_stays_usable()
    {
        await using var rig = new Rig(Options(), configure: r => r.Values[DomeCell] = 1);

        var trip = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Rejected, trip.Status);
        Assert.Contains("already out", trip.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Writes);
        var status = StatusOf(rig.Failures, "CB_LIGHTS_DOME");
        Assert.True(status.Executable);
        Assert.Equal(SynapticBreakerInterface.Accessible, status.Interface);

        rig.Reader.Values[DomeCell] = 0; // the pilot pushed it back in
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
    }

    [Fact]
    public async Task A_tripped_breaker_whose_cell_becomes_unreadable_stays_reported_and_does_not_hide_the_others()
    {
        await using var rig = new Rig(AllEnabled());
        Assert.True((await rig.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd(Iru))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
        rig.Reader.Values[DomeCell] = double.NaN;

        var active = await rig.Failures.GetActiveFailuresAsync();

        Assert.Equal([Dome, Iru, "electrical.generator.2"], active.Select(a => a.Key!.Value).Order());
        Assert.Equal(SynapticBreakerInterface.Unavailable, StatusOf(rig.Failures, "CB_LIGHTS_DOME").Interface);
    }

    [Fact]
    public async Task A_lost_connection_never_disables_a_breaker()
    {
        await using var rig = new Rig(Options());
        rig.Reader.Failure = new InvalidOperationException("The simulator is not connected.");

        Assert.Equal(FailureCommandStatus.Unavailable, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);

        rig.Reader.Failure = null;
        Assert.True(StatusOf(rig.Failures, "CB_LIGHTS_DOME").Executable);
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
    }

    [Fact]
    public async Task Breaker_failures_never_affect_the_historical_recipes_nor_the_degradations()
    {
        await using var rig = new Rig(AllEnabled(), configure: r => r.Values[CellOf(Iru)] = double.NaN);
        await using var degradations = new FSGAP.Synaptic.Degradations.SynapticDegradationProvider(rig.Board, ownsBoard: false);
        rig.Writer.IgnoredVariables.Add(DomeCell);
        await rig.Failures.TriggerAsync(Cmd(Dome));
        await rig.Failures.TriggerAsync(Cmd(Iru));

        Assert.True((await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
        Assert.True((await rig.Failures.TriggerAsync(Cmd("pneumatic.bleed-valve.1"))).IsSuccess);
        Assert.True((await degradations.ApplyAsync(FSGAP.Abstractions.Degradations.DegradationKey.Parse("flight-controls.pfcc.1.forced-off"))).IsSuccess);
        Assert.Equal(["electrical.generator.2", "pneumatic.bleed-valve.1"], (await rig.Failures.GetActiveFailuresAsync()).Select(a => a.Key!.Value).Order());
        Assert.True((await rig.Failures.ClearAsync(Cmd("electrical.generator.2"))).IsSuccess);
        Assert.Equal(53, rig.Failures.CircuitBreakerStatuses().Count(b => b.Executable));
    }

    [Fact]
    public async Task Breaker_failures_are_returned_as_results_never_thrown()
    {
        await using var rig = new Rig(AllEnabled(), configure: r =>
        {
            r.Values[DomeCell] = double.NaN;
            r.Values[CellOf(Iru)] = 2;
        });
        rig.Writer.IgnoredVariables.Add(CellOf("electrical.circuit-breaker.nav-ads"));

        foreach (var key in new[] { Dome, Iru, "electrical.circuit-breaker.nav-ads", Dome, Iru })
        {
            Assert.Null(await Record.ExceptionAsync(() => rig.Failures.TriggerAsync(Cmd(key))));
            Assert.Null(await Record.ExceptionAsync(() => rig.Failures.ClearAsync(Cmd(key))));
        }

        Assert.Null(await Record.ExceptionAsync(() => rig.Failures.GetActiveFailuresAsync()));
        Assert.True((await rig.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
    }

    [Fact]
    public async Task Each_status_dimension_is_separate_and_a_write_never_implies_a_functional_effect()
    {
        await using var rig = new Rig(Options());

        var dome = StatusOf(rig.Failures, "CB_LIGHTS_DOME");
        Assert.True(dome.Listed);
        Assert.True(dome.Executable);
        Assert.Equal(SynapticBreakerInterface.Unverified, dome.Interface);
        Assert.Equal(SynapticBreakerQualification.LabValidated, dome.TripQualification);
        Assert.Equal(SynapticBreakerExecution.NotAttempted, dome.LastExecution);
        Assert.Equal(SynapticBreakerFunctionalEffect.NotObserved, dome.FunctionalEffect);

        var ads = StatusOf(rig.Failures, "CB_NAV_ADS");
        Assert.True(ads.Listed);
        Assert.False(ads.Executable);
        Assert.Equal(SynapticBreakerQualification.Unqualified, ads.TripQualification);
        Assert.Equal(SynapticBreakerFunctionalEffect.MasterCautionInLab, ads.FunctionalEffect);

        Assert.True((await rig.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        dome = StatusOf(rig.Failures, "CB_LIGHTS_DOME");
        Assert.Equal(SynapticBreakerInterface.Accessible, dome.Interface);
        Assert.Equal(SynapticBreakerExecution.Succeeded, dome.LastExecution);
        Assert.Equal(SynapticBreakerFunctionalEffect.NotObserved, dome.FunctionalEffect);
        Assert.Equal(SynapticBreakerQualification.LabValidated, dome.TripQualification);
    }

    // -- Session ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_session_declares_and_executes_the_breakers_its_options_enable()
    {
        var reader = new FakeVariableReader();
        foreach (var control in SynapticControls.Writable)
        {
            reader.Values[control.Variable.Name] = control.Normal ?? 0.5;
        }

        var writer = new FakeVariableWriter(reader);
        var provider = new SynapticAircraftProvider(simulatorVariables: reader, simulatorVariableWriter: writer, circuitBreakers: Options());
        await using var session = await provider.AttachAsync(Descriptors.AirFrance);

        Assert.Equal(384 + 55, session.Capabilities.Failures.Catalog.Count);
        Assert.True(session.Capabilities.Failures.CanTrigger(FailureKey.Parse(Dome)));
        Assert.False(session.Capabilities.Failures.CanTrigger(FailureKey.Parse(Iru)));
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        Assert.Equal(1, reader.Values[DomeCell]);
    }

    private static JsonNode Evidence(string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Evidence", file)))!;

    private static JsonObject EmbeddedManifestJson()
    {
        using var stream = typeof(SynapticAircraftProvider).Assembly.GetManifestResourceStream("FSGAP.Synaptic.CircuitBreakers.synaptic-a220-circuit-breakers.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
}
