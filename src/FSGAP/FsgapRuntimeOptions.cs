using FSGAP.Abstractions.Configuration;

namespace FSGAP;

/// <summary>Configuration of an <see cref="FsgapRuntime"/>.</summary>
public sealed record FsgapRuntimeOptions
{
    /// <summary>SDK settings: application name, data directory, connection and telemetry options.</summary>
    public required FsgapOptions Sdk { get; init; }

    /// <summary>
    /// Whether sessions may act on the aircraft: trigger and clear failures, apply and restore controlled degradations.
    /// <see langword="true"/> by default. When <see langword="false"/>, sessions are read-only (identity, telemetry) and
    /// their failure and degradation capabilities are empty.
    /// </summary>
    public bool EnableAircraftCommands { get; init; } = true;

    /// <summary>
    /// The Synaptic A220 version installed, as observed by the application (for example <c>1.0.11</c>).
    /// <see langword="null"/> by default (unknown): the 55 Synaptic circuit-breaker failures are listed with no operation.
    /// With the version the breaker manifest was qualified on, the breakers whose direct trip is qualified become
    /// executable (with <see cref="EnableAircraftCommands"/>); with any other version, none.
    /// </summary>
    public string? SynapticAircraftVersion { get; init; }

    /// <summary>
    /// Synaptic circuit-breaker ids (for example <c>CB_NAV_IRU</c>) whose direct trip the application has qualified live,
    /// in addition to those qualified in the embedded manifest; enables a newly qualified breaker without a new SDK.
    /// </summary>
    public IReadOnlyCollection<string> SynapticQualifiedCircuitBreakers { get; init; } = [];

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sdk, nameof(Sdk));
        Sdk.Validate();
        SynapticCircuitBreakers().Validate();
    }

    /// <summary>The Synaptic provider's breaker options built from <see cref="SynapticAircraftVersion"/> and <see cref="SynapticQualifiedCircuitBreakers"/>.</summary>
    internal FSGAP.Synaptic.SynapticCircuitBreakerOptions SynapticCircuitBreakers() =>
        new() { AircraftVersion = SynapticAircraftVersion, QualifiedTrips = SynapticQualifiedCircuitBreakers };
}
