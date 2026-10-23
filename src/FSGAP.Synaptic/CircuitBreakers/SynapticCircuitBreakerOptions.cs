using FSGAP.Synaptic.CircuitBreakers;

namespace FSGAP.Synaptic;

/// <summary>
/// Which Synaptic A220 circuit-breaker failures a session may execute. The 55 breakers are always listed in the failure
/// catalog; this decides which of them have operations (trip by writing their cell = 1, reset by writing it = 0).
/// </summary>
/// <remarks>
/// <para>
/// A breaker is executable when its direct trip is qualified, in the embedded manifest (proven live in the lab) or in
/// <see cref="QualifiedTrips"/> (qualified by the application since, enabled without rebuilding the SDK), and it is not in
/// <see cref="DisabledCircuitBreakers"/>. Without options, the manifest-qualified breakers are executable.
/// </para>
/// <para>
/// The Synaptic version is <b>not</b> a condition: a Synaptic update keeps every breaker whose interface still works. Each
/// breaker's cell is verified when the breaker is used (read before any write, read back after it); a breaker whose cell
/// cannot be read, holds an unexpected value or does not take a write is disabled for the session, alone.
/// </para>
/// </remarks>
public sealed record SynapticCircuitBreakerOptions
{
    /// <summary>The Synaptic A220 version the breaker manifest was qualified on (diagnostic reference).</summary>
    public static string QualifiedOnAircraftVersion => SynapticCircuitBreakerManifest.Embedded.SynapticVersion;

    /// <summary>
    /// The Synaptic A220 version installed, as observed by the application, if known. Diagnostic only: it is logged next
    /// to <see cref="QualifiedOnAircraftVersion"/> and never enables or disables a breaker.
    /// </summary>
    public string? AircraftVersion { get; init; }

    /// <summary>
    /// Breaker ids (for example <c>CB_NAV_IRU</c>) whose direct trip the application has qualified live, in addition to
    /// those qualified in the embedded manifest. Every id must be one of the 55 manifest breakers.
    /// </summary>
    public IReadOnlyCollection<string> QualifiedTrips { get; init; } = [];

    /// <summary>
    /// Breaker ids the application disables (for example a breaker found faulty after an aircraft update): listed, never
    /// executed, whatever their qualification. Every id must be one of the 55 manifest breakers.
    /// </summary>
    public IReadOnlyCollection<string> DisabledCircuitBreakers { get; init; } = [];

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentException">An id is not a known breaker id, or the version is blank.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(QualifiedTrips, nameof(QualifiedTrips));
        ArgumentNullException.ThrowIfNull(DisabledCircuitBreakers, nameof(DisabledCircuitBreakers));
        if (AircraftVersion is not null && string.IsNullOrWhiteSpace(AircraftVersion))
        {
            throw new ArgumentException("The aircraft version is blank; leave it null when it is unknown.", nameof(AircraftVersion));
        }

        RequireKnown(QualifiedTrips, nameof(QualifiedTrips));
        RequireKnown(DisabledCircuitBreakers, nameof(DisabledCircuitBreakers));
    }

    private static void RequireKnown(IEnumerable<string> ids, string parameter)
    {
        var unknown = ids.Where(id => SynapticCircuitBreakerManifest.Embedded.FindById(id) is null).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Unknown circuit-breaker id(s): {string.Join(", ", unknown)}.", parameter);
        }
    }
}
