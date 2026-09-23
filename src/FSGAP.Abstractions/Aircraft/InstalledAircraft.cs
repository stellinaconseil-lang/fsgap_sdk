namespace FSGAP.Abstractions.Aircraft;

/// <summary>
/// An aircraft variant installed locally (typically one livery of one airframe), as found by an
/// <see cref="IInstalledAircraftCatalog"/>.
/// </summary>
public sealed record InstalledAircraft
{
    /// <summary>
    /// Stable, opaque identifier of this entry within its catalog. It stays the same across scans as long as the
    /// installed files do not move. Do not parse it.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Normalized identity (developer, family, model, engine, registration, livery...).</summary>
    public required AircraftIdentity Identity { get; init; }

    /// <summary>
    /// Livery folder name, matching <see cref="AircraftDescriptor.LiveryFolder"/> when this aircraft is loaded.
    /// </summary>
    public string? LiveryFolder { get; init; }

    /// <summary>Name of the simulator package that contains this aircraft.</summary>
    public string? PackageName { get; init; }

    /// <summary>Most recent modification time of the files this entry was read from.</summary>
    public DateTimeOffset? ModifiedAt { get; init; }
}
