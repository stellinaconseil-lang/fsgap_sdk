namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// The connection to the simulator: starting and stopping it, its state, and the session clock. Retrying while the
/// simulator is absent or after a loss is the connection's job, not the host's.
/// </summary>
/// <remarks>
/// Observation follows the same pattern as telemetry streaming: <see cref="WatchStatusAsync"/> returns an
/// <see cref="IAsyncEnumerable{T}"/> that yields the current status immediately, then every change. A slow
/// consumer may skip intermediate statuses but always receives the latest one. Implementations must be thread-safe.
/// </remarks>
public interface ISimulatorConnection : IAsyncDisposable
{
    /// <summary>Current status.</summary>
    SimulatorConnectionStatus Status { get; }

    /// <summary>
    /// Monotonic time elapsed since <see cref="StartAsync"/>. It keeps running across reconnections and is reset by
    /// <see cref="StopAsync"/>. Use it to timestamp a flight independently of simulator pauses or wall-clock changes.
    /// </summary>
    TimeSpan SessionElapsed { get; }

    /// <summary>
    /// Starts connecting in the background and returns without waiting for the simulator. Calling it while
    /// already started has no effect.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start request itself, not the connection.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects, stops retrying and moves to <see cref="SimulatorConnectionState.Disconnected"/>.</summary>
    /// <param name="cancellationToken">Cancels waiting for the shutdown to complete.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Yields the current status, then each change, until <paramref name="cancellationToken"/> is cancelled
    /// (the enumeration then throws <see cref="OperationCanceledException"/>) or the connection is disposed
    /// (the enumeration completes).
    /// </summary>
    /// <param name="cancellationToken">Ends the observation.</param>
    IAsyncEnumerable<SimulatorConnectionStatus> WatchStatusAsync(CancellationToken cancellationToken = default);
}
