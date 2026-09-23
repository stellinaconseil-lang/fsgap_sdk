using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Observation;

namespace FSGAP.SimConnect;

/// <summary>
/// Loaded aircraft published by the transport. A new value is published only when the descriptor's values change,
/// so repeated identical polls notify nobody.
/// </summary>
internal sealed class AircraftDetectorSource : IAircraftDetector
{
    private readonly ObservableState<AircraftDescriptor?> _aircraft = new(null);

    public AircraftDescriptor? Current => _aircraft.Current;

    public IAsyncEnumerable<AircraftDescriptor?> WatchAsync(CancellationToken cancellationToken = default) =>
        _aircraft.WatchAsync(cancellationToken);

    /// <returns><see langword="true"/> when the loaded aircraft changed.</returns>
    public bool Publish(AircraftDescriptor? aircraft) => _aircraft.Set(aircraft);

    public void Complete() => _aircraft.Complete();
}
