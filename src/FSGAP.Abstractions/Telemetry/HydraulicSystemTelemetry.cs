namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one hydraulic system. Deliberately minimal; extended as consumers need more.</summary>
public sealed record HydraulicSystemTelemetry
{
    /// <summary>Stable, provider-assigned normalized key of the system (e.g. <c>green</c>, <c>a</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the system is pressurized.</summary>
    public TelemetryValue<bool> Pressurized { get; init; }

    /// <summary>System pressure, in psi.</summary>
    public TelemetryValue<double> PressurePsi { get; init; }
}
