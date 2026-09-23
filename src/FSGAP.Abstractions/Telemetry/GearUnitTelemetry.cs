namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one landing gear unit (leg).</summary>
public sealed record GearUnitTelemetry
{
    /// <summary>
    /// Stable, provider-assigned normalized key of the unit (e.g. <c>nose</c>, <c>left-main</c>,
    /// <c>right-main</c>, <c>center-main</c>, <c>tail</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Extension, in percent: 0 fully retracted, 100 fully extended.</summary>
    public TelemetryValue<double> ExtensionPercent { get; init; }
}
