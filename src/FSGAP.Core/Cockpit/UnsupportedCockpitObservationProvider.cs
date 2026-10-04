using FSGAP.Abstractions.Cockpit;

namespace FSGAP.Core.Cockpit;

/// <summary>
/// Cockpit observation provider for sessions that observe nothing. Pair it with
/// <see cref="Abstractions.Capabilities.CockpitObservationCapabilities.None"/>. It never throws: a snapshot is empty.
/// </summary>
public sealed class UnsupportedCockpitObservationProvider : ICockpitObservationProvider
{
    /// <summary>Shared instance; the provider is stateless.</summary>
    public static UnsupportedCockpitObservationProvider Instance { get; } = new();

    private UnsupportedCockpitObservationProvider()
    {
    }

    /// <inheritdoc />
    public Task<CockpitObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CockpitObservationSnapshot.Empty(DateTimeOffset.UtcNow));
}
