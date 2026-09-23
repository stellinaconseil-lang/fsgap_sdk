namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// State of one engine. Readings that do not apply to an engine type stay unavailable (e.g. N2 on a piston engine).
/// </summary>
public sealed record EngineTelemetry
{
    /// <summary>1-based engine position, numbered from left to right as seen from the cockpit.</summary>
    public required int Index { get; init; }

    /// <summary>Whether the engine is running.</summary>
    public TelemetryValue<bool> Running { get; init; }

    /// <summary>N1 (fan / low-pressure spool speed), in percent.</summary>
    public TelemetryValue<double> N1Percent { get; init; }

    /// <summary>N2 (high-pressure spool speed), in percent.</summary>
    public TelemetryValue<double> N2Percent { get; init; }

    /// <summary>Exhaust gas temperature, in degrees Celsius.</summary>
    public TelemetryValue<double> EgtCelsius { get; init; }

    /// <summary>Fuel flow, in kilograms per hour.</summary>
    public TelemetryValue<double> FuelFlowKilogramsPerHour { get; init; }

    /// <summary>Whether the engine fire detection reports a fire.</summary>
    public TelemetryValue<bool> FireDetected { get; init; }
}
