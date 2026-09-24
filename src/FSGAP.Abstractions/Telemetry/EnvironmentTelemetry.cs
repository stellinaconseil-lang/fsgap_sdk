namespace FSGAP.Abstractions.Telemetry;

/// <summary>Weather around the aircraft, as the simulator reports it.</summary>
public sealed record EnvironmentTelemetry
{
    /// <summary>Outside (ambient) air temperature, in degrees Celsius.</summary>
    public TelemetryValue<double> OutsideAirTemperatureCelsius { get; init; }

    /// <summary>Direction the wind blows from, in degrees true.</summary>
    public TelemetryValue<double> WindDirectionDegreesTrue { get; init; }

    /// <summary>Wind speed, in knots.</summary>
    public TelemetryValue<double> WindSpeedKnots { get; init; }

    /// <summary>Precipitation type at the aircraft.</summary>
    public TelemetryValue<PrecipitationType> Precipitation { get; init; }

    /// <summary>
    /// Precipitation rate, in millimeters of water, the unit the simulator documents (it does not document the time
    /// base). Compare values from the same source only.
    /// </summary>
    public TelemetryValue<double> PrecipitationRateMillimeters { get; init; }
}

/// <summary>Precipitation type.</summary>
public enum PrecipitationType
{
    /// <summary>No precipitation.</summary>
    None = 0,

    /// <summary>Rain.</summary>
    Rain,

    /// <summary>Snow.</summary>
    Snow,
}
