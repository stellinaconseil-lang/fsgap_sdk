using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// Reports which aircraft is loaded in the simulator, as a raw <see cref="AircraftDescriptor"/>. The detector knows
/// no vendor: recognizing a Fenix, a PMDG or any other aircraft is the job of the
/// <see cref="IAircraftProvider"/> implementations.
/// </summary>
/// <remarks>
/// A change is any difference in the descriptor's values (title, livery folder, ATC id...). Loading another livery
/// of the same aircraft is therefore a change. Same observation pattern as
/// <see cref="ISimulatorConnection.WatchStatusAsync"/>. Thread-safe.
/// </remarks>
public interface IAircraftDetector
{
    /// <summary>The loaded aircraft, or <see langword="null"/> when none is loaded or the simulator is not connected.</summary>
    AircraftDescriptor? Current { get; }

    /// <summary>
    /// Yields the current descriptor (possibly <see langword="null"/>), then each change, until cancelled (throws
    /// <see cref="OperationCanceledException"/>) or disposed (completes).
    /// </summary>
    /// <param name="cancellationToken">Ends the observation.</param>
    IAsyncEnumerable<AircraftDescriptor?> WatchAsync(CancellationToken cancellationToken = default);
}
