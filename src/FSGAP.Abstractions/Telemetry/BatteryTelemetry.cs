namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one battery.</summary>
public sealed record BatteryTelemetry
{
    /// <summary>Stable, provider-assigned normalized key of the battery (e.g. <c>bat-1</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Battery voltage, in volts.</summary>
    public TelemetryValue<double> VoltageVolts { get; init; }
}
