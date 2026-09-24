namespace FSGAP.Abstractions.Telemetry;

/// <summary>Cabin pressurization.</summary>
public sealed record PressurizationTelemetry
{
    /// <summary>Cabin (pressure) altitude, in feet.</summary>
    public TelemetryValue<double> CabinAltitudeFeet { get; init; }

    /// <summary>Cabin altitude rate of change, in feet per minute, positive climbing.</summary>
    public TelemetryValue<double> CabinAltitudeRateFeetPerMinute { get; init; }
}
