using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Observation;
using FSGAP.Core.Telemetry;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Telemetry;

/// <summary>
/// Merges the SimVar groups into one normalized snapshot and publishes it. Implements
/// <see cref="ITelemetryProvider"/> for the transport.
/// </summary>
/// <remarks>
/// <para>
/// <b>Groups in, one snapshot out.</b> Groups arrive at different rates (position every second, engines every
/// five). Each arriving group replaces its own sections and leaves the others as they were, so what is published is
/// always a complete aircraft state, never a fragment. Publication is per group read, not per variable.
/// </para>
/// <para>
/// <b>Nothing is mutated after publication.</b> Each merge builds a new record with <c>with</c> and swaps it in
/// under a lock; a consumer holding an earlier snapshot keeps exactly what it was given.
/// </para>
/// <para>
/// <b>Freshness</b> uses <see cref="Core.Telemetry.TelemetryFreshness"/>. Every publication ages the whole
/// snapshot, so a stalled group's values turn Unknown on the back of the groups still being read.
/// <see cref="GetSnapshotAsync"/> ages at read time. When nothing is being read at all (connection lost), the
/// transport calls <see cref="ExpireNow"/>.
/// </para>
/// <para>
/// <b>Generations.</b> <see cref="Reset"/> starts a new generation (aircraft change, stop). A group read issued under
/// an older generation is dropped when it completes, so a read begun on the previous aircraft can never repopulate
/// the snapshot of the new one.
/// </para>
/// </remarks>
internal sealed class TelemetrySource : ITelemetryProvider
{
    private readonly ObservableState<AircraftTelemetry> _snapshot;
    private readonly TimeProvider _time;
    private readonly TimeSpan _staleAfter;
    private readonly object _gate = new();

    private AircraftTelemetry _current;
    private int _generation;

    internal TelemetrySource(TimeProvider time, TimeSpan staleAfter)
    {
        _time = time;
        _staleAfter = staleAfter;
        _current = AircraftTelemetry.Unavailable(time.GetUtcNow());
        _snapshot = new ObservableState<AircraftTelemetry>(_current);
    }

    /// <summary>Current generation; capture it before a read and pass it to the matching Apply method.</summary>
    internal int Generation => Volatile.Read(ref _generation);

    /// <inheritdoc />
    /// <remarks>Aged at the moment of the read, so a value that stopped arriving is never returned as Known.</remarks>
    public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Aged(_current));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The current snapshot first, then the latest snapshot at most once per
    /// <see cref="TelemetryStreamOptions.Interval"/>. Each enumeration is its own latest-value subscription: a slow
    /// consumer skips intermediate snapshots without holding up the native reads or other consumers, and a consumer
    /// that throws ends only its own enumeration.
    /// </remarks>
    public IAsyncEnumerable<AircraftTelemetry> StreamAsync(TelemetryStreamOptions? options = null, CancellationToken cancellationToken = default)
    {
        var interval = (options ?? TelemetryStreamOptions.Default).Interval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(options));
        return ThrottledAsync(interval, cancellationToken);
    }

    /// <summary>Merges the 1 Hz group.</summary>
    internal void ApplyFast(in FastGroupVars vars, DateTimeOffset observedAt, int generation)
    {
        var flight = GenericTelemetryMapper.ToFlightState(vars, observedAt);
        var warnings = GenericTelemetryMapper.ToWarnings(vars, observedAt);
        var deflections = GenericTelemetryMapper.ToControlDeflections(vars, observedAt);
        Publish(
            current => current with
            {
                Flight = flight,
                Warnings = warnings,
                FlightControls = GenericTelemetryMapper.WithDeflections(current.FlightControls, deflections),
            },
            observedAt,
            generation);
    }

    /// <summary>Merges the gear, brakes, flaps and speed-brake group.</summary>
    internal void ApplyNormal(in NormalGroupVars vars, DateTimeOffset observedAt, int generation)
    {
        var gear = GenericTelemetryMapper.ToLandingGear(vars, observedAt);
        var controls = GenericTelemetryMapper.ToFlightControls(vars, observedAt);
        Publish(
            current => current with
            {
                LandingGear = gear,
                FlightControls = GenericTelemetryMapper.WithConfiguration(current.FlightControls, controls),
            },
            observedAt,
            generation);
    }

    /// <summary>Merges the engine, APU bleed and cabin pressurization group.</summary>
    internal void ApplySlow(in SlowGroupVars vars, DateTimeOffset observedAt, int generation)
    {
        var engines = GenericTelemetryMapper.ToEngines(vars, observedAt);
        var apuBleed = GenericTelemetryMapper.ToApuBleed(vars, observedAt);
        var pressurization = GenericTelemetryMapper.ToPressurization(vars, observedAt);
        Publish(
            current => current with
            {
                Engines = engines,
                Apu = current.Apu with { BleedOn = apuBleed },
                Pressurization = pressurization,
            },
            observedAt,
            generation);
    }

    /// <summary>Merges the weather group.</summary>
    internal void ApplyEnvironment(in EnvironmentGroupVars vars, DateTimeOffset observedAt, int generation)
    {
        var environment = GenericTelemetryMapper.ToEnvironment(vars, observedAt);
        Publish(current => current with { Environment = environment }, observedAt, generation);
    }

    /// <summary>
    /// Publishes the current snapshot aged to now, so subscribers see staleness although no group arrived
    /// (connection lost, every group failing).
    /// </summary>
    internal void ExpireNow()
    {
        lock (_gate)
        {
            _current = Aged(_current);
            _snapshot.Set(_current);
        }
    }

    /// <summary>
    /// Drops everything, starts a new generation and publishes an empty snapshot. Used on an aircraft change and on
    /// stop: the previous aircraft's readings are not stale data about the new aircraft, they are accurate data about
    /// a different one, and no timestamp would reveal that.
    /// </summary>
    internal void Reset()
    {
        lock (_gate)
        {
            _generation++;
            _current = AircraftTelemetry.Unavailable(_time.GetUtcNow());
            _snapshot.Set(_current);
        }
    }

    internal void Complete() => _snapshot.Complete();

    private AircraftTelemetry Aged(AircraftTelemetry snapshot) =>
        TelemetryFreshness.ExpireStaleValues(snapshot with { Timestamp = _time.GetUtcNow() }, _staleAfter);

    private void Publish(Func<AircraftTelemetry, AircraftTelemetry> merge, DateTimeOffset observedAt, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return; // read issued for a previous aircraft or session
            }

            _current = TelemetryFreshness.ExpireStaleValues(merge(_current) with { Timestamp = observedAt }, _staleAfter);
            _snapshot.Set(_current);
        }
    }

    private async IAsyncEnumerable<AircraftTelemetry> ThrottledAsync(TimeSpan interval, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AircraftTelemetry? last = null;
        long? lastYield = null;
        await foreach (var _ in _snapshot.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            if (lastYield is { } yieldedAt && interval - _time.GetElapsedTime(yieldedAt) is var wait && wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
            }

            var current = _snapshot.Current;
            if (ReferenceEquals(current, last))
            {
                continue; // already delivered while waiting out the interval
            }

            last = current;
            lastYield = _time.GetTimestamp();
            yield return current;
        }
    }
}
