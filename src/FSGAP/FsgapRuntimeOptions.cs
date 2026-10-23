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
    /// The Synaptic A220 version installed, as observed by the application (for example <c>1.0.11</c>), if known.
    /// Diagnostic only: logged next to the version the breaker manifest was qualified on, never a condition. A Synaptic
    /// update keeps every circuit breaker whose interface still works; a breaker whose cell fails is disabled alone.
    /// </summary>
    public string? SynapticAircraftVersion { get; init; }

    /// <summary>
    /// Synaptic circuit-breaker ids (for example <c>CB_NAV_IRU</c>) whose direct trip the application has qualified live,
    /// in addition to those qualified in the embedded manifest; enables a newly qualified breaker without a new SDK.
    /// </summary>
    public IReadOnlyCollection<string> SynapticQualifiedCircuitBreakers { get; init; } = [];

    /// <summary>
    /// Synaptic circuit-breaker ids the application disables (for example one found faulty after an aircraft update):
    /// listed, never executed. The other breakers are not affected.
    /// </summary>
    public IReadOnlyCollection<string> SynapticDisabledCircuitBreakers { get; init; } = [];

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sdk, nameof(Sdk));
        Sdk.Validate();
        SynapticCircuitBreakers().Validate();
    }

    /// <summary>The Synaptic provider's breaker options built from the three <c>Synaptic*</c> properties.</summary>
    internal FSGAP.Synaptic.SynapticCircuitBreakerOptions SynapticCircuitBreakers() =>
        new()
        {
            AircraftVersion = SynapticAircraftVersion,
            QualifiedTrips = SynapticQualifiedCircuitBreakers,
            DisabledCircuitBreakers = SynapticDisabledCircuitBreakers,
        };
}
