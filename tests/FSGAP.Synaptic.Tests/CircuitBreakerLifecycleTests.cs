using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Synaptic.CircuitBreakers;
using FSGAP.Synaptic.Degradations;
using FSGAP.Synaptic.Failures;
using FSGAP.Synaptic.Systems;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic.Tests;

/// <summary>
/// The lifecycle of a tripped circuit breaker across sessions: a breaker trip is a persistent aircraft condition (it stays
/// out when the session ends, the connection drops or the application quits) and a trip is idempotent on a breaker already
/// out, whoever pulled it. The historical recipes and the degradations keep their restore-on-dispose behaviour. Each test
/// uses one simulated aircraft (<see cref="Aircraft"/>) shared by successive sessions, as the real cockpit is.
/// </summary>
public class CircuitBreakerLifecycleTests
{
    private const string Dome = "electrical.circuit-breaker.lights-dome";
    private const string DomeCell = "L:A22X Circuit Breaker R B4";
    private const string Iru = "electrical.circuit-breaker.nav-iru";
    private const string Generator = "electrical.generator.2";
    private const string GeneratorCell = "L:A22X R Gen Off";

    private static readonly SynapticControlBoard.Timing Fast = new(TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(2));

    private static readonly SynapticCircuitBreakerManifest Manifest = SynapticCircuitBreakerManifest.Embedded;

    private static SynapticCircuitBreakerOptions AllEnabled() => new() { QualifiedTrips = Manifest.Breakers.Select(b => b.Id).ToArray() };

    private static FailureCommand Cmd(string key) => new(FailureKey.Parse(key));

    private static string CellOf(string key) => SynapticFailureCatalog.Find(FailureKey.Parse(key))!.CircuitBreaker!.Control.Variable.Name;

    /// <summary>One loaded aircraft: its variables and input events outlive the sessions attached to it.</summary>
    private sealed class Aircraft
    {
        public Aircraft()
        {
            foreach (var breaker in Manifest.Breakers)
            {
                Inventory.Events[breaker.InputEvent] = breaker.InputEventHash;
            }

            foreach (var control in SynapticControls.All)
            {
                Reader.Values[control.Variable.Name] = control.Normal ?? 0.5;
            }

            foreach (var control in SynapticControls.CircuitBreakers)
            {
                Reader.Values[control.Variable.Name] = 0;
            }

            Writer = new FakeVariableWriter(Reader);
        }

        public FakeVariableReader Reader { get; } = new();

        public FakeVariableWriter Writer { get; }

        public FakeInputEventInventory Inventory { get; } = new();

        public (string, double)[] Writes => Writer.Writes.Select(w => (w.Variable.Name, w.Value)).ToArray();

        public int CellReads(string key) => Reader.ReadsWhere(v => v.Name == CellOf(key));

        /// <summary>A session attached to this aircraft, as <see cref="SynapticAircraftProvider"/> builds it (the failure provider owns the board).</summary>
        public Session Attach(IAircraftDetector? detector = null, SynapticCircuitBreakerOptions? options = null)
        {
            var board = new SynapticControlBoard(Reader, Writer, detector, Descriptors.AirFrance, NullLogger.Instance, Fast);
            return new Session(
                board,
                new SynapticDegradationProvider(board, ownsBoard: false),
                new SynapticFailureProvider(board, ownsBoard: true, SynapticCircuitBreakerActivation.From(options ?? AllEnabled(), Manifest), Inventory));
        }
    }

    /// <summary>A session's providers, disposed in the order <c>AircraftSession</c> uses (degradations, then failures).</summary>
    private sealed class Session(SynapticControlBoard board, SynapticDegradationProvider degradations, SynapticFailureProvider failures) : IAsyncDisposable
    {
        public SynapticControlBoard Board { get; } = board;

        public SynapticDegradationProvider Degradations { get; } = degradations;

        public SynapticFailureProvider Failures { get; } = failures;

        public async ValueTask DisposeAsync()
        {
            await Degradations.DisposeAsync();
            await Failures.DisposeAsync();
        }
    }

    // -- B1: a tripped breaker stays out when its session ends ------------------------------------------------------------

    [Fact]
    public async Task B1_a_tripped_breaker_stays_out_when_its_session_is_disposed()
    {
        var aircraft = new Aircraft();
        var session = aircraft.Attach();
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        await session.DisposeAsync(); // the aircraft is still loaded: this is what closing the application does

        Assert.Equal(1, aircraft.Reader.Values[DomeCell]);
        Assert.Equal([(DomeCell, 1.0)], aircraft.Writes); // never written back to 0
    }

    [Fact]
    public async Task B1_several_tripped_breakers_all_stay_out_and_the_session_releases_its_resources()
    {
        var aircraft = new Aircraft();
        var detector = new StreamingDetector(Descriptors.AirFrance);
        var session = aircraft.Attach(detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the board watch");
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        Assert.True((await session.Failures.TriggerAsync(Cmd(Iru))).IsSuccess);

        await session.DisposeAsync();

        Assert.Equal(1, aircraft.Reader.Values[DomeCell]);
        Assert.Equal(1, aircraft.Reader.Values[CellOf(Iru)]);
        Assert.DoesNotContain(aircraft.Writes, w => w.Item2 == 0.0);
        Assert.True(session.Board.Disposed);
        await Wait.UntilAsync(() => detector.Watchers == 0, "the board watch to stop"); // .NET/connection resources still released
        Assert.Empty(session.Failures.OwnedKeys);
    }

    [Fact]
    public async Task End_of_flight_reads_the_active_breakers_and_never_resets_them()
    {
        var aircraft = new Aircraft();
        var session = aircraft.Attach();
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        // What a consumer does around the end of a flight: read the active failures (debrief), then close the session.
        Assert.Equal([Dome], (await session.Failures.GetActiveFailuresAsync()).Select(a => a.Key!.Value));
        await session.DisposeAsync();

        Assert.Equal(1, aircraft.Reader.Values[DomeCell]);
        Assert.Equal([(DomeCell, 1.0)], aircraft.Writes);
    }

    [Fact]
    public async Task A_lost_connection_never_resets_a_tripped_breaker_neither_then_nor_at_dispose()
    {
        var aircraft = new Aircraft();
        var detector = new StreamingDetector(Descriptors.AirFrance);
        var session = aircraft.Attach(detector);
        await Wait.UntilAsync(() => detector.Watchers == 1, "the board watch");
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        detector.Publish(null); // simulator disconnected
        await Wait.UntilAsync(() => session.Failures.OwnedKeys.Count == 0, "the ownership drop");
        await session.DisposeAsync();

        Assert.Equal(1, aircraft.Reader.Values[DomeCell]);
        Assert.Equal([(DomeCell, 1.0)], aircraft.Writes);
    }

    // -- B2: a trip is idempotent on a breaker already out, across sessions -----------------------------------------------

    [Fact]
    public async Task B2_a_new_session_triggering_a_breaker_an_earlier_session_tripped_succeeds_without_writing()
    {
        var aircraft = new Aircraft();
        var first = aircraft.Attach();
        Assert.True((await first.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        await first.DisposeAsync();

        await using var second = aircraft.Attach();
        var again = await second.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Succeeded, again.Status);
        Assert.Contains("POSITION_CONFIRMED", again.Message, StringComparison.Ordinal);
        Assert.Contains("nothing written", again.Message, StringComparison.Ordinal);
        Assert.Equal([(DomeCell, 1.0)], aircraft.Writes); // one physical action in total
        Assert.Equal([Dome], (await second.Failures.GetActiveFailuresAsync()).Select(a => a.Key!.Value));
    }

    [Fact]
    public async Task B2_a_breaker_pulled_by_the_pilot_before_the_trigger_is_an_idempotent_success()
    {
        var aircraft = new Aircraft();
        aircraft.Reader.Values[DomeCell] = 1; // pulled in the cockpit
        await using var session = aircraft.Attach();

        var trip = await session.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Succeeded, trip.Status);
        Assert.Contains("AIRCRAFT_EFFECT_UNVERIFIED", trip.Message, StringComparison.Ordinal);
        Assert.Empty(aircraft.Writes);
        Assert.Equal(1, aircraft.Inventory.Listings); // the compatibility gate ran before the cell was read
        Assert.True(aircraft.CellReads(Dome) >= 1); // position confirmed by reading the cell
        var status = session.Failures.CircuitBreakerStatuses().Single(b => b.Id == "CB_LIGHTS_DOME");
        Assert.True(status.Executable);
        Assert.Equal(SynapticBreakerExecution.PositionConfirmed, status.LastExecution);
    }

    [Fact]
    public async Task A_reconnection_to_the_same_aircraft_never_duplicates_the_physical_action()
    {
        var aircraft = new Aircraft();
        var first = aircraft.Attach();
        Assert.True((await first.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        Assert.True((await first.Failures.TriggerAsync(Cmd(Dome))).IsSuccess); // redelivered in the same session
        await first.DisposeAsync();

        for (var reconnect = 0; reconnect < 3; reconnect++)
        {
            await using var next = aircraft.Attach();
            Assert.True((await next.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
            Assert.True((await next.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        }

        Assert.Equal([(DomeCell, 1.0)], aircraft.Writes);
        Assert.Equal(1, aircraft.Reader.Values[DomeCell]);
    }

    [Fact]
    public async Task A_breaker_found_in_after_a_reload_is_tripped_again_by_the_next_trigger_fsgap_does_not_remember_it()
    {
        var aircraft = new Aircraft();
        var first = aircraft.Attach();
        Assert.True((await first.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        await first.DisposeAsync();

        aircraft.Reader.Values[DomeCell] = 0; // the aircraft was reloaded: the physical state no longer matches the business state
        await using var second = aircraft.Attach();

        Assert.Empty(await second.Failures.GetActiveFailuresAsync()); // FSGAP holds no memory across sessions: nothing claimed, nothing re-applied on its own
        Assert.Equal([(DomeCell, 1.0)], aircraft.Writes);

        Assert.True((await second.Failures.TriggerAsync(Cmd(Dome))).IsSuccess); // the consumer re-injects its known condition
        Assert.Equal([(DomeCell, 1.0), (DomeCell, 1.0)], aircraft.Writes);
        Assert.Equal(1, aircraft.Reader.Values[DomeCell]);
    }

    // -- Explicit reset is kept -------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_explicit_clear_resets_a_breaker_the_session_tripped()
    {
        var aircraft = new Aircraft();
        await using var session = aircraft.Attach();
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        var reset = await session.Failures.ClearAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Succeeded, reset.Status);
        Assert.Equal([(DomeCell, 1.0), (DomeCell, 0.0)], aircraft.Writes);
        Assert.Equal(0, aircraft.Reader.Values[DomeCell]);
        Assert.Empty(await session.Failures.GetActiveFailuresAsync());
    }

    [Fact]
    public async Task An_explicit_clear_in_a_later_session_resets_a_breaker_left_out_by_an_earlier_one()
    {
        var aircraft = new Aircraft();
        var first = aircraft.Attach();
        Assert.True((await first.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        await first.DisposeAsync();

        await using var maintenance = aircraft.Attach();
        var reset = await maintenance.Failures.ClearAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Succeeded, reset.Status);
        Assert.Contains("POSITION_CONFIRMED", reset.Message, StringComparison.Ordinal);
        Assert.Equal([(DomeCell, 1.0), (DomeCell, 0.0)], aircraft.Writes);
        Assert.Equal(0, aircraft.Reader.Values[DomeCell]);
    }

    [Fact]
    public async Task An_explicit_clear_of_a_breaker_already_in_writes_nothing()
    {
        var aircraft = new Aircraft();
        await using var session = aircraft.Attach();

        Assert.Equal(FailureCommandStatus.Succeeded, (await session.Failures.ClearAsync(Cmd(Dome))).Status);
        Assert.Empty(aircraft.Writes);
    }

    // -- Compatibility and coherence still gate everything ----------------------------------------------------------------

    [Fact]
    public async Task An_incompatible_breaker_already_out_is_never_read_nor_written_nor_reported_as_success()
    {
        var aircraft = new Aircraft();
        aircraft.Reader.Values[DomeCell] = 1;
        aircraft.Inventory.Events.TryRemove("AIRLINER_CB_LIGHTS_DOME", out _);
        await using var session = aircraft.Attach();

        var trip = await session.Failures.TriggerAsync(Cmd(Dome));
        var clear = await session.Failures.ClearAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.NotSupported, trip.Status);
        Assert.Contains("COMPATIBILITY_UNVERIFIED", trip.Message, StringComparison.Ordinal);
        Assert.False(clear.IsSuccess);
        Assert.Equal(0, aircraft.CellReads(Dome));
        Assert.Empty(aircraft.Writes);
    }

    [Fact]
    public async Task An_unavailable_input_event_list_never_confirms_a_breaker_already_out()
    {
        var aircraft = new Aircraft();
        aircraft.Reader.Values[DomeCell] = 1;
        aircraft.Inventory.Failure = new InvalidOperationException("The simulator is not connected.");
        await using var session = aircraft.Attach();

        var trip = await session.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Unavailable, trip.Status);
        Assert.Equal(0, aircraft.CellReads(Dome));
        Assert.Empty(aircraft.Writes);
    }

    [Fact]
    public async Task An_incoherent_or_unreadable_cell_is_never_adopted_as_a_success()
    {
        var aircraft = new Aircraft();
        aircraft.Reader.Values[DomeCell] = 2;
        aircraft.Reader.Values[CellOf(Iru)] = double.NaN;
        await using var session = aircraft.Attach();

        Assert.Equal(FailureCommandStatus.Rejected, (await session.Failures.TriggerAsync(Cmd(Dome))).Status);
        Assert.Equal(FailureCommandStatus.Rejected, (await session.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Empty(aircraft.Writes);
        Assert.Empty(session.Failures.OwnedKeys);
    }

    [Fact]
    public async Task A_breaker_not_enabled_for_the_session_stays_not_supported_even_when_already_out()
    {
        var aircraft = new Aircraft();
        aircraft.Reader.Values[CellOf(Iru)] = 1;
        await using var session = aircraft.Attach(options: new SynapticCircuitBreakerOptions());

        Assert.Equal(FailureCommandStatus.NotSupported, (await session.Failures.TriggerAsync(Cmd(Iru))).Status);
        Assert.Equal(0, aircraft.CellReads(Iru));
    }

    // -- The historical recipes and the degradations keep restoring on dispose --------------------------------------------

    [Fact]
    public async Task Disposing_still_restores_the_historical_recipes_and_degradations_but_keeps_the_breakers_out()
    {
        var aircraft = new Aircraft();
        var session = aircraft.Attach();
        Assert.True((await session.Failures.TriggerAsync(Cmd(Generator))).IsSuccess);
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        Assert.True((await session.Degradations.ApplyAsync(FSGAP.Abstractions.Degradations.DegradationKey.Parse("flight-controls.pfcc.1.forced-off"))).IsSuccess);

        await session.DisposeAsync();

        Assert.Equal(0, aircraft.Reader.Values[GeneratorCell]); // historical recipe: restored, as before
        Assert.Equal(1, aircraft.Reader.Values[DomeCell]); // breaker: kept out
        Assert.Contains(aircraft.Writes, w => w.Item1 == GeneratorCell && w.Item2 == 0.0);
        Assert.DoesNotContain(aircraft.Writes, w => w.Item1 == DomeCell && w.Item2 == 0.0);
    }

    [Fact]
    public async Task A_historical_recipe_set_outside_fsgap_is_still_never_claimed_by_a_new_session()
    {
        var aircraft = new Aircraft();
        aircraft.Reader.Values[GeneratorCell] = 1; // generator switched off in the cockpit
        await using var session = aircraft.Attach();

        Assert.Equal(FailureCommandStatus.Rejected, (await session.Failures.TriggerAsync(Cmd(Generator))).Status);
        Assert.Equal(FailureCommandStatus.Rejected, (await session.Failures.ClearAsync(Cmd(Generator))).Status);
        Assert.Empty(aircraft.Writes);
    }

    [Fact]
    public void Only_the_55_circuit_breaker_recipes_persist_across_sessions()
    {
        Assert.All(SynapticFailureCatalog.HistoricalRecipes, r => Assert.Equal(SynapticFailureLifecycle.RestoredOnSessionEnd, r.Lifecycle));
        Assert.Equal(384, SynapticFailureCatalog.HistoricalRecipes.Count);
        Assert.Equal(55, SynapticFailureCatalog.CircuitBreakerRecipes.Count(r => r.Lifecycle == SynapticFailureLifecycle.Persistent));
        Assert.All(SynapticFailureCatalog.CircuitBreakerRecipes, r => Assert.Equal(SynapticFailureLifecycle.Persistent, r.Lifecycle));
    }
}
