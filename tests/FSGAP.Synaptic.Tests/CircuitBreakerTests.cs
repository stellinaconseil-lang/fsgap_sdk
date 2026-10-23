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

    private static SynapticCircuitBreakerOptions Supported(params string[] qualifiedTrips) =>
        new() { AircraftVersion = "1.0.11", QualifiedTrips = qualifiedTrips };

    /// <summary>Every breaker qualified: the mechanics are proven on all 55, independently of the live qualification.</summary>
    private static SynapticCircuitBreakerOptions AllEnabled() => Supported(Manifest.Breakers.Select(b => b.Id).ToArray());

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
        Assert.Equal(Manifest.SynapticVersion, SynapticCircuitBreakerOptions.SupportedAircraftVersion);
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

    // -- Catalog and activation -------------------------------------------------------------------------------------------

    [Fact]
    public void The_55_breakers_are_listed_but_none_is_executable_by_default()
    {
        var catalog = SynapticFailureProvider.DefaultCapabilities;

        Assert.Equal(55, SynapticFailureCatalog.CircuitBreakerRecipes.Count);
        Assert.All(SynapticFailureCatalog.CircuitBreakerRecipes, r =>
        {
            Assert.True(catalog.Catalog.TryGet(r.Key, out var definition));
            Assert.Equal(FailureOperations.None, definition.Operations);
            Assert.Equal(FailureCategory.Electrical, definition.Category);
            Assert.Equal([FailureTarget.Aircraft], definition.SupportedTargets);
            Assert.False(catalog.CanTrigger(r.Key));
            Assert.False(r.Executable);
            Assert.True(r.RequiresActivation);
            Assert.Equal(SynapticFailureMechanism.CircuitBreakerTrip, r.Mechanism);
        });
    }

    [Fact]
    public async Task Without_options_a_breaker_answers_not_supported_without_contacting_the_aircraft()
    {
        await using var rig = new Rig(options: null);

        var result = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.NotSupported, result.Status);
        Assert.Contains("disabled", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task With_the_supported_version_only_the_live_qualified_breaker_is_executable()
    {
        await using var rig = new Rig(Supported());
        var capabilities = rig.Failures.Capabilities;

        var executable = SynapticFailureCatalog.CircuitBreakerRecipes.Where(r => capabilities.CanTrigger(r.Key)).Select(r => r.Key.Value).ToArray();

        Assert.Equal([Dome], executable);
        Assert.True(capabilities.CanClear(FailureKey.Parse(Dome)));
        // The historical recipes are unchanged by breaker activation.
        Assert.Equal(17, SynapticFailureCatalog.HistoricalRecipes.Count(r => capabilities.CanTrigger(r.Key)));
    }

    [Theory]
    [MemberData(nameof(UnqualifiedBreakerKeys))]
    public async Task An_unqualified_breaker_is_never_executed_even_with_the_supported_version(string key)
    {
        await using var rig = new Rig(Supported());

        var trigger = await rig.Failures.TriggerAsync(Cmd(key));
        var clear = await rig.Failures.ClearAsync(Cmd(key));

        Assert.Equal(FailureCommandStatus.NotSupported, trigger.Status);
        Assert.Equal(FailureCommandStatus.NotSupported, clear.Status);
        Assert.Contains("not qualified live", trigger.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task A_breaker_qualified_by_the_application_becomes_executable_without_a_new_sdk()
    {
        await using var rig = new Rig(Supported("CB_NAV_IRU"));

        Assert.True(rig.Failures.Capabilities.CanTrigger(FailureKey.Parse(Iru)));
        Assert.Equal(FailureCommandStatus.Succeeded, (await rig.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Equal([(CellOf(Iru), 1.0)], rig.Writes);
    }

    [Theory]
    [InlineData("1.0.12")]
    [InlineData("1.0.10")]
    [InlineData("2.0.0")]
    public async Task Another_synaptic_version_disables_every_breaker(string version)
    {
        await using var rig = new Rig(new SynapticCircuitBreakerOptions { AircraftVersion = version, QualifiedTrips = ["CB_NAV_IRU"] });

        Assert.DoesNotContain(SynapticFailureCatalog.CircuitBreakerRecipes, r => rig.Failures.Capabilities.CanTrigger(r.Key));
        var result = await rig.Failures.TriggerAsync(Cmd(Dome));
        Assert.Equal(FailureCommandStatus.NotSupported, result.Status);
        Assert.Contains("qualified on Synaptic A220 1.0.11", result.Message, StringComparison.Ordinal);
        Assert.Contains(version, result.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Reader.ReadCount);
        Assert.Empty(rig.Writes);
    }

    [Theory]
    [InlineData("v1.0.11")]
    [InlineData(" 1.0.11 ")]
    public void The_reported_version_is_normalized(string version)
    {
        var activation = SynapticCircuitBreakerActivation.From(new SynapticCircuitBreakerOptions { AircraftVersion = version }, Manifest);

        Assert.Equal(["CB_LIGHTS_DOME"], activation.Enabled);
    }

    [Fact]
    public void An_unknown_version_disables_every_breaker()
    {
        Assert.Empty(SynapticCircuitBreakerActivation.From(new SynapticCircuitBreakerOptions { QualifiedTrips = ["CB_NAV_IRU"] }, Manifest).Enabled);
        Assert.Empty(SynapticCircuitBreakerActivation.From(null, Manifest).Enabled);
    }

    [Fact]
    public void Invalid_options_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new SynapticCircuitBreakerOptions { AircraftVersion = "1.0.11", QualifiedTrips = ["CB_DOES_NOT_EXIST"] }.Validate());
        Assert.Throws<ArgumentException>(() => new SynapticCircuitBreakerOptions { AircraftVersion = " " }.Validate());
        Assert.Throws<ArgumentException>(() => new SynapticAircraftProvider(circuitBreakers: Supported("CB_DOES_NOT_EXIST")));
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
        await using var rig = new Rig(Supported());

        await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.True(rig.Reader.ReadsWhere(v => v.Name == DomeCell) >= 2); // read before the write, then read back
        Assert.All(rig.Reader.Reads, read => Assert.All(read, v => Assert.Equal(DomeCell, v.Name)));
    }

    [Fact]
    public async Task Tripping_twice_or_resetting_twice_writes_once()
    {
        await using var rig = new Rig(Supported());

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
        await using var rig = new Rig(Supported());
        rig.Writer.Ignore = true;

        var trip = await rig.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Unconfirmed, trip.Status);
        Assert.Equal([FailureKey.Parse(Dome)], rig.Failures.OwnedKeys); // kept, so it can still be reset
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync()); // the cell never left 0
    }

    [Fact]
    public async Task An_ambiguous_reset_is_unconfirmed_keeps_the_breaker_owned_and_can_be_retried()
    {
        await using var rig = new Rig(Supported());
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
        await using var rig = new Rig(Supported());
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
        await using var rig = new Rig(Supported(), detector);
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
        await using var rig = new Rig(Supported(), configure: r => r.Values[DomeCell] = 1);

        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Rejected, (await rig.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Empty(await rig.Failures.GetActiveFailuresAsync());
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public async Task A_pilot_pushing_the_breaker_back_in_makes_it_inactive_and_nothing_is_rewritten()
    {
        await using var rig = new Rig(Supported());
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
        await using var rig = new Rig(Supported(), configure: r => r.Values[DomeCell] = 2);

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
        var provider = new SynapticAircraftProvider(simulatorVariables: reader, simulatorVariableWriter: writer, circuitBreakers: Supported());
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
