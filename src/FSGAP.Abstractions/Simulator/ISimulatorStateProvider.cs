namespace FSGAP.Abstractions.Simulator;

/// <summary>Exposes the general simulation state (pause, crashes).</summary>
/// <remarks>Same observation pattern as <see cref="ISimulatorConnection.WatchStatusAsync"/>. Thread-safe.</remarks>
public interface ISimulatorStateProvider
{
    /// <summary>Current simulation state.</summary>
    SimulatorState Current { get; }

    /// <summary>
    /// Yields the current state, then each change, until cancelled (throws <see cref="OperationCanceledException"/>)
    /// or disposed (completes).
    /// </summary>
    /// <param name="cancellationToken">Ends the observation.</param>
    IAsyncEnumerable<SimulatorState> WatchAsync(CancellationToken cancellationToken = default);
}
