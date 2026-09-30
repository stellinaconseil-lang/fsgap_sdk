using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Core.Sessions;

/// <summary>
/// Whether a newly detected <see cref="AircraftDescriptor"/> still describes the aircraft a session was attached to,
/// as opposed to a different aircraft that needs a new resolution and a new session.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AircraftDescriptor"/> mixes what is loaded (title, ATC model and type, livery) with metadata the
/// simulator may change while the same aircraft stays loaded. Full descriptor equality is therefore not continuity:
/// the simulator's <c>ATC ID</c> (<see cref="AircraftDescriptor.Registration"/>) was seen changing several times within
/// seconds of a load with nothing else changing (BLOCK 10B.3: 8 of 15 emissions), which made hosts and sessions tear
/// down and restart for no reason.
/// </para>
/// <para>
/// Rule: two descriptors are the same loaded aircraft when every field is equal except
/// <see cref="AircraftDescriptor.Registration"/>. That is the one field proven volatile; every other field (and any
/// field added later) stays structural until proven otherwise. A livery change (name or folder) is a new aircraft: in
/// the simulator it is a reload, and providers look up their installed-livery data by it.
/// </para>
/// <para>
/// A session that survives a registration change keeps its provider and resources; its
/// <c>IAircraftSession.Identity</c> may reflect the new registration, as each provider's own resolution rules decide.
/// Providers must not base <c>IAircraftProvider.Match</c> support on the registration, so that a continuity-preserving
/// host never needs to resolve again.
/// </para>
/// </remarks>
public static class AircraftContinuity
{
    /// <summary>Equality comparer implementing <see cref="IsSameLoadedAircraft"/> (for example to deduplicate a detector stream).</summary>
    public static IEqualityComparer<AircraftDescriptor?> Comparer { get; } = new LoadedAircraftComparer();

    /// <summary>
    /// Whether <paramref name="current"/> is the same loaded aircraft as <paramref name="previous"/>: all fields equal
    /// except the registration (<c>ATC ID</c>). Two <see langword="null"/> descriptors (no aircraft) are the same;
    /// <see langword="null"/> and an aircraft are not.
    /// </summary>
    public static bool IsSameLoadedAircraft(AircraftDescriptor? previous, AircraftDescriptor? current) =>
        Comparer.Equals(previous, current);

    private sealed class LoadedAircraftComparer : IEqualityComparer<AircraftDescriptor?>
    {
        public bool Equals(AircraftDescriptor? x, AircraftDescriptor? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && Structural(x) == Structural(y));

        public int GetHashCode(AircraftDescriptor? obj) => obj is null ? 0 : Structural(obj).GetHashCode();

        // Record equality minus the volatile field: new descriptor fields are structural by default.
        private static AircraftDescriptor Structural(AircraftDescriptor descriptor) => descriptor with { Registration = null };
    }
}
