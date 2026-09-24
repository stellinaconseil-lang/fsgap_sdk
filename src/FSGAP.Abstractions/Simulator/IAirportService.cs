using FSGAP.Abstractions.Geography;

namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// Finds airports known to the simulator around a coordinate. Read-only and purely geographic: "which airports are
/// near this point", never "which airport is the departure" — that meaning belongs to the application.
/// </summary>
/// <remarks>
/// <para>
/// The caller supplies the position (the aircraft's, from telemetry, or any other point). Results are the airports the
/// simulator reports, sorted by great-circle distance, filtered by <see cref="AirportSearchOptions"/>.
/// </para>
/// <para>
/// Outcomes are distinct: an empty list (or <see langword="null"/>) means the simulator answered and no airport is
/// within range; <see cref="SimulatorServiceException"/> means the simulator could not be asked or did not answer
/// usably. Thread-safe.
/// </para>
/// </remarks>
public interface IAirportService
{
    /// <summary>Airports within range of <paramref name="position"/>, nearest first.</summary>
    /// <param name="position">Where to search from.</param>
    /// <param name="options">Radius and result limit; <see cref="AirportSearchOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <exception cref="SimulatorServiceException">The simulator is not connected, or the query failed.</exception>
    Task<IReadOnlyList<AirportInfo>> FindNearbyAirportsAsync(
        GeoPosition position,
        AirportSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>The nearest airport within range, or <see langword="null"/> when there is none.</summary>
    /// <param name="position">Where to search from.</param>
    /// <param name="options">Radius; <see cref="AirportSearchOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <exception cref="SimulatorServiceException">The simulator is not connected, or the query failed.</exception>
    Task<AirportInfo?> FindNearestAirportAsync(
        GeoPosition position,
        AirportSearchOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>An airport as reported by the simulator, with its distance from the searched position.</summary>
public sealed record AirportInfo
{
    /// <summary>Identifier the simulator reports, trimmed and upper-case (normally the ICAO code, e.g. <c>LFMN</c>).</summary>
    public required string Icao { get; init; }

    /// <summary>Two-letter ICAO region the simulator reports (e.g. <c>LF</c>), or <see langword="null"/> when empty.</summary>
    public string? Region { get; init; }

    /// <summary>Airport reference position.</summary>
    public required GeoPosition Position { get; init; }

    /// <summary>Elevation in feet, or <see langword="null"/> when the simulator gave none.</summary>
    public double? ElevationFeet { get; init; }

    /// <summary>Great-circle distance from the searched position, in nautical miles.</summary>
    public required double DistanceNauticalMiles { get; init; }
}

/// <summary>Limits of an airport search. The defaults are defined here and nowhere else.</summary>
public sealed record AirportSearchOptions
{
    /// <summary>Default radius: 50 NM.</summary>
    public const double DefaultMaxDistanceNauticalMiles = 50;

    /// <summary>Default maximum number of results: 10.</summary>
    public const int DefaultMaxResults = 10;

    /// <summary>The default options.</summary>
    public static AirportSearchOptions Default { get; } = new();

    /// <summary>Only airports at most this far are returned, in nautical miles (default 50, at most 1,000).</summary>
    public double MaxDistanceNauticalMiles { get; init; } = DefaultMaxDistanceNauticalMiles;

    /// <summary>At most this many airports are returned (default 10, at most 1,000).</summary>
    public int MaxResults { get; init; } = DefaultMaxResults;

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        if (!double.IsFinite(MaxDistanceNauticalMiles) || MaxDistanceNauticalMiles <= 0 || MaxDistanceNauticalMiles > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDistanceNauticalMiles), MaxDistanceNauticalMiles, "The radius must be above 0 and at most 1,000 NM.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxResults, 1, nameof(MaxResults));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxResults, 1000, nameof(MaxResults));
    }
}

/// <summary>Why a simulator service could not answer.</summary>
public enum SimulatorServiceError
{
    /// <summary>The simulator is not connected (not running, connection lost, transport stopped or disposed).</summary>
    SimulatorUnavailable = 0,

    /// <summary>The simulator is connected but the query failed: rejected, no answer in time, or an unreadable answer.</summary>
    QueryFailed,
}

/// <summary>A simulator service could not answer. Never used for "nothing found", which is an empty result.</summary>
public sealed class SimulatorServiceException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">Why the service could not answer.</param>
    /// <param name="message">Detail.</param>
    /// <param name="innerException">Underlying error, if any.</param>
    public SimulatorServiceException(SimulatorServiceError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>Why the service could not answer.</summary>
    public SimulatorServiceError Error { get; }
}
