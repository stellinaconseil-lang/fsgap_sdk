namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// Fire detection state of a zone other than an engine or the APU, which carry their own <c>FireDetected</c> reading.
/// </summary>
public sealed record FireZoneTelemetry
{
    /// <summary>Stable, provider-assigned normalized key of the zone (e.g. <c>cargo-fwd</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether a fire or smoke condition is detected in the zone.</summary>
    public TelemetryValue<bool> FireDetected { get; init; }
}
