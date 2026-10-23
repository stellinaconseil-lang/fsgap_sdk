using FSGAP.Synaptic.CircuitBreakers;

namespace FSGAP.Synaptic;

/// <summary>
/// Which Synaptic A220 circuit-breaker failures a session may execute. The 55 breakers are always listed in the failure
/// catalog; this decides which of them have operations (trip by writing their cell = 1, clear by writing it = 0).
/// </summary>
/// <remarks>
/// <para>
/// Closed by default: without options, or when <see cref="AircraftVersion"/> is unknown or is not
/// <see cref="SupportedAircraftVersion"/>, no breaker is executable (the breaker cells were mapped on that version only and
/// are never assumed to survive an update).
/// </para>
/// <para>
/// With the supported version, a breaker is executable when its direct trip is qualified: in the embedded manifest
/// (proven live in the lab) or in <see cref="QualifiedTrips"/>, the breakers the application has qualified since. A
/// breaker qualified later is therefore enabled by configuration, without rebuilding the SDK.
/// </para>
/// </remarks>
public sealed record SynapticCircuitBreakerOptions
{
    /// <summary>The Synaptic A220 version the breaker manifest was qualified on.</summary>
    public static string SupportedAircraftVersion => SynapticCircuitBreakerManifest.Embedded.SynapticVersion;

    /// <summary>
    /// The Synaptic A220 version installed, as observed by the application (for example <c>1.0.11</c>; a leading
    /// <c>v</c> is accepted). <see langword="null"/> when unknown: no breaker is executable.
    /// </summary>
    public string? AircraftVersion { get; init; }

    /// <summary>
    /// Breaker ids (for example <c>CB_NAV_IRU</c>) whose direct trip the application has qualified live, in addition to
    /// those qualified in the embedded manifest. Every id must be one of the 55 manifest breakers.
    /// </summary>
    public IReadOnlyCollection<string> QualifiedTrips { get; init; } = [];

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentException">A qualified trip is not a known breaker id, or the version is blank.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(QualifiedTrips, nameof(QualifiedTrips));
        if (AircraftVersion is not null && string.IsNullOrWhiteSpace(AircraftVersion))
        {
            throw new ArgumentException("The aircraft version is blank; leave it null when it is unknown.", nameof(AircraftVersion));
        }

        var unknown = QualifiedTrips.Where(id => SynapticCircuitBreakerManifest.Embedded.FindById(id) is null).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Unknown circuit-breaker id(s): {string.Join(", ", unknown)}.", nameof(QualifiedTrips));
        }
    }
}
