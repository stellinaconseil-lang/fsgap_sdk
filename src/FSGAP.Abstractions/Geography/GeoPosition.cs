namespace FSGAP.Abstractions.Geography;

/// <summary>A point on the Earth: latitude and longitude in decimal degrees (WGS 84, as the simulator reports them).</summary>
/// <remarks>
/// The one coordinate type of FSGAP services. Telemetry keeps its own per-value freshness
/// (<c>FlightStateTelemetry.LatitudeDegrees</c> / <c>LongitudeDegrees</c>); <see cref="TryFrom"/> turns a known pair of
/// readings into a position.
/// </remarks>
public readonly record struct GeoPosition
{
    /// <summary>Mean Earth radius in nautical miles (6371.0088 km / 1.852), the value used for great-circle distances.</summary>
    public const double EarthRadiusNauticalMiles = 3440.0695;

    /// <summary>Creates a position.</summary>
    /// <param name="latitudeDegrees">Latitude, -90 to 90.</param>
    /// <param name="longitudeDegrees">Longitude, -180 to 180.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range or not finite.</exception>
    public GeoPosition(double latitudeDegrees, double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || latitudeDegrees is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), latitudeDegrees, "Latitude must be between -90 and 90 degrees.");
        }

        if (!double.IsFinite(longitudeDegrees) || longitudeDegrees is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), longitudeDegrees, "Longitude must be between -180 and 180 degrees.");
        }

        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees;
    }

    /// <summary>Latitude in degrees, positive north.</summary>
    public double LatitudeDegrees { get; }

    /// <summary>Longitude in degrees, positive east.</summary>
    public double LongitudeDegrees { get; }

    /// <summary>A position from two readings, or <see langword="null"/> when a value is not a valid coordinate.</summary>
    /// <param name="latitudeDegrees">Latitude in degrees.</param>
    /// <param name="longitudeDegrees">Longitude in degrees.</param>
    public static GeoPosition? TryFrom(double latitudeDegrees, double longitudeDegrees) =>
        double.IsFinite(latitudeDegrees) && latitudeDegrees is >= -90 and <= 90
        && double.IsFinite(longitudeDegrees) && longitudeDegrees is >= -180 and <= 180
            ? new GeoPosition(latitudeDegrees, longitudeDegrees)
            : null;

    /// <summary>
    /// Great-circle distance to <paramref name="other"/>, in nautical miles (haversine on a spherical Earth: well
    /// within 0.5 % of the ellipsoidal distance, and exact for ordering nearby points).
    /// </summary>
    /// <remarks>Correct across the ±180° meridian and near the poles: only sines of half-differences are used.</remarks>
    /// <param name="other">The other position.</param>
    public double DistanceNauticalMilesTo(GeoPosition other)
    {
        var lat1 = ToRadians(LatitudeDegrees);
        var lat2 = ToRadians(other.LatitudeDegrees);
        var sinHalfDLat = Math.Sin((lat2 - lat1) / 2);
        var sinHalfDLon = Math.Sin(ToRadians(other.LongitudeDegrees - LongitudeDegrees) / 2);
        var a = (sinHalfDLat * sinHalfDLat) + (Math.Cos(lat1) * Math.Cos(lat2) * sinHalfDLon * sinHalfDLon);
        return 2 * EarthRadiusNauticalMiles * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{LatitudeDegrees:F6}, {LongitudeDegrees:F6}");

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
