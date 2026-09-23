using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Core.Simulator;

/// <summary>Waiting helpers over the simulator observation contracts.</summary>
public static class SimulatorObservationExtensions
{
    /// <summary>Waits until the connection reaches <paramref name="state"/> (returns immediately if it already has).</summary>
    /// <returns>The status that matched.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException">The connection was disposed before reaching the state.</exception>
    public static async Task<SimulatorConnectionStatus> WaitForStateAsync(
        this ISimulatorConnection connection,
        SimulatorConnectionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await foreach (var status in connection.WatchStatusAsync(cancellationToken).ConfigureAwait(false))
        {
            if (status.State == state)
            {
                return status;
            }
        }

        throw new InvalidOperationException($"The connection ended before reaching {state}.");
    }

    /// <summary>Waits until an aircraft is loaded (returns immediately if one already is).</summary>
    /// <returns>The loaded aircraft.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException">The detector was disposed before an aircraft was loaded.</exception>
    public static async Task<AircraftDescriptor> WaitForAircraftAsync(
        this IAircraftDetector detector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detector);
        await foreach (var aircraft in detector.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            if (aircraft is not null)
            {
                return aircraft;
            }
        }

        throw new InvalidOperationException("The detector ended before an aircraft was loaded.");
    }
}
