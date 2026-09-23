namespace FSGAP.Abstractions.Capabilities;

/// <summary>
/// Everything a provider can do for a given aircraft, discoverable at runtime.
/// </summary>
/// <remarks>
/// Every capability defaults to "not supported": a provider declares only what it actually implements, and a
/// consumer checks a capability before relying on the corresponding data or command.
/// </remarks>
public sealed record AircraftCapabilities
{
    /// <summary>No capability at all.</summary>
    public static AircraftCapabilities None { get; } = new();

    /// <summary>Telemetry sections the provider can read.</summary>
    public TelemetryCapabilities Telemetry { get; init; } = TelemetryCapabilities.None;

    /// <summary>Failure operations and types the provider supports.</summary>
    public FailureCapabilities Failures { get; init; } = FailureCapabilities.None;
}
