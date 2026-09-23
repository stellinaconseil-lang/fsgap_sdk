namespace FSGAP.Abstractions.Telemetry;

/// <summary>Position, attitude and speeds of the aircraft. Units are part of each property name.</summary>
public sealed record FlightStateTelemetry
{
    /// <summary>Whether the aircraft is on the ground.</summary>
    public TelemetryValue<bool> OnGround { get; init; }

    /// <summary>Latitude in decimal degrees, positive north.</summary>
    public TelemetryValue<double> LatitudeDegrees { get; init; }

    /// <summary>Longitude in decimal degrees, positive east.</summary>
    public TelemetryValue<double> LongitudeDegrees { get; init; }

    /// <summary>Altitude above mean sea level, in feet.</summary>
    public TelemetryValue<double> AltitudeFeet { get; init; }

    /// <summary>Height above ground from the radio altimeter, in feet.</summary>
    public TelemetryValue<double> RadioAltitudeFeet { get; init; }

    /// <summary>Indicated airspeed, in knots.</summary>
    public TelemetryValue<double> IndicatedAirspeedKnots { get; init; }

    /// <summary>Ground speed, in knots.</summary>
    public TelemetryValue<double> GroundSpeedKnots { get; init; }

    /// <summary>Vertical speed, in feet per minute, positive climbing.</summary>
    public TelemetryValue<double> VerticalSpeedFeetPerMinute { get; init; }

    /// <summary>Magnetic heading, in degrees [0, 360).</summary>
    public TelemetryValue<double> HeadingMagneticDegrees { get; init; }

    /// <summary>Pitch attitude, in degrees, positive nose up.</summary>
    public TelemetryValue<double> PitchDegrees { get; init; }

    /// <summary>Bank angle, in degrees, positive right wing down.</summary>
    public TelemetryValue<double> BankDegrees { get; init; }

    /// <summary>Vertical load factor, in g (1.0 in level flight).</summary>
    public TelemetryValue<double> GLoad { get; init; }
}
