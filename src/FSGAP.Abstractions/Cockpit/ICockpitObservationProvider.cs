namespace FSGAP.Abstractions.Cockpit;

/// <summary>
/// A snapshot of the cockpit observations read in one batch, with the instant it was taken. A key the provider
/// supports but could not read is present with an <see cref="CockpitObservationState.Unknown"/> value.
/// </summary>
public sealed record CockpitObservationSnapshot
{
    /// <summary>When the snapshot was taken.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Observed value per key.</summary>
    public IReadOnlyDictionary<CockpitObservationKey, CockpitObservationValue> Values { get; init; } =
        new Dictionary<CockpitObservationKey, CockpitObservationValue>();

    /// <summary>The value for a key, or an unavailable value when the key is not in this snapshot.</summary>
    public CockpitObservationValue Get(CockpitObservationKey key) =>
        Values.TryGetValue(key, out var value) ? value : CockpitObservationValue.Unavailable(CockpitObservationValueKind.Boolean);

    /// <summary>An empty snapshot (no observations) at <paramref name="timestamp"/>.</summary>
    public static CockpitObservationSnapshot Empty(DateTimeOffset timestamp) => new() { Timestamp = timestamp };
}

/// <summary>
/// Reads normalized cockpit observations for an attached aircraft, on the simulator connection the runtime already
/// owns. Read-only: there is no way to set a cockpit control. The set of keys a provider offers is published through
/// its <c>AircraftCapabilities.CockpitObservations</c>; check that before relying on a snapshot.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot reads every supported key in <b>one batched request</b> on the shared connection — never one native
/// request per key, and never a connection of its own. The consumer controls the cadence by calling
/// <see cref="GetSnapshotAsync"/>; the provider runs no polling loop of its own.
/// </para>
/// <para>
/// A snapshot never throws for the ordinary "simulator away" or "another aircraft loaded" cases: those keys come back
/// <see cref="CockpitObservationState.Unknown"/> so no stale value survives an aircraft change.
/// </para>
/// </remarks>
public interface ICockpitObservationProvider
{
    /// <summary>Reads the current observations in one batch.</summary>
    Task<CockpitObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
