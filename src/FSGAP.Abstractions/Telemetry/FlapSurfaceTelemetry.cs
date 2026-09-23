namespace FSGAP.Abstractions.Telemetry;

/// <summary>Actual position of one flap surface (or group of surfaces reported together).</summary>
public sealed record FlapSurfaceTelemetry
{
    /// <summary>Stable, provider-assigned normalized key of the surface (e.g. <c>left</c>, <c>right</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Extension, in percent of full travel: 0 retracted, 100 fully extended.</summary>
    public TelemetryValue<double> ExtensionPercent { get; init; }
}
