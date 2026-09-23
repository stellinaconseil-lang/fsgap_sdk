namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one electrical bus. Deliberately minimal; extended as consumers need more.</summary>
public sealed record ElectricalBusTelemetry
{
    /// <summary>Stable, provider-assigned normalized key of the bus (e.g. <c>ac-1</c>, <c>dc-battery</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the bus is powered.</summary>
    public TelemetryValue<bool> Powered { get; init; }
}
