namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one fuel pump.</summary>
public sealed record FuelPumpTelemetry
{
    /// <summary>
    /// Stable, provider-assigned normalized key of the pump (e.g. <c>left-1</c>, <c>center-left</c>).
    /// Never a vendor variable name.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name (e.g. "Left tank pump 1").</summary>
    public required string Name { get; init; }

    /// <summary>Whether the pump is switched on.</summary>
    public TelemetryValue<bool> IsOn { get; init; }

    /// <summary>Whether the pump reports a fault (e.g. low pressure).</summary>
    public TelemetryValue<bool> Fault { get; init; }
}
