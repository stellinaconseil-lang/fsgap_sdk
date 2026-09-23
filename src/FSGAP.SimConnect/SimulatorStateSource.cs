using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Observation;

namespace FSGAP.SimConnect;

/// <summary>
/// Simulation state published by the transport. Updates can come from the native event thread (pause, crash) and
/// from the transport loop (connect, loss, stop), so each read-modify-write happens under a lock. Publication goes
/// through <see cref="ObservableState{T}"/>, which never blocks the caller.
/// </summary>
internal sealed class SimulatorStateSource : ISimulatorStateProvider
{
    private readonly object _gate = new();
    private readonly ObservableState<SimulatorState> _state = new(SimulatorState.Initial);

    public SimulatorState Current => _state.Current;

    public IAsyncEnumerable<SimulatorState> WatchAsync(CancellationToken cancellationToken = default) =>
        _state.WatchAsync(cancellationToken);

    /// <summary>
    /// A connection was established. Pause becomes Unknown while waiting for the first <c>Pause_EX1</c>
    /// notification, or stays Unavailable if the subscription failed.
    /// </summary>
    public void MarkConnected(bool pauseSubscribed) => Update(state => state with
    {
        Paused = pauseSubscribed ? TelemetryValue<bool>.Unknown : TelemetryValue<bool>.Unavailable,
    });

    /// <summary>The connection was lost: pause can no longer be known. Crashes of this session are kept.</summary>
    public void MarkDisconnected() => Update(state => state with { Paused = TelemetryValue<bool>.Unavailable });

    public void RecordPause(bool paused, DateTimeOffset observedAt) => Update(state =>
        state.Paused.TryGetValue(out var current) && current == paused
            ? state
            : state with { Paused = TelemetryValue<bool>.Known(paused, observedAt) });

    /// <returns>The new crash count.</returns>
    public int RecordCrash(DateTimeOffset at) =>
        Update(state => state with { CrashCount = state.CrashCount + 1, LastCrashAt = at }).CrashCount;

    /// <summary>Back to <see cref="SimulatorState.Initial"/> (new or stopped session).</summary>
    public void Reset() => Update(_ => SimulatorState.Initial);

    public void Complete() => _state.Complete();

    private SimulatorState Update(Func<SimulatorState, SimulatorState> change)
    {
        lock (_gate)
        {
            var next = change(_state.Current);
            _state.Set(next);
            return next;
        }
    }
}
