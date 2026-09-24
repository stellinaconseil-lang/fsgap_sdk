using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Services;

/// <summary>
/// Turns the simulator's raw airport list into the answer for one position. Pure and deterministic.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// Normalize: identifier trimmed and upper-cased (never invented: an empty one is dropped); region the same, empty
/// becomes <see langword="null"/>; an entry whose coordinates are not a valid position is dropped.
/// </description></item>
/// <item><description>Distance: great-circle, nautical miles (<see cref="GeoPosition.DistanceNauticalMilesTo"/>).</description></item>
/// <item><description>
/// Duplicates (same identifier listed twice): the entry nearest to the searched position is kept; on an exact tie,
/// the one with the lower latitude, then longitude. The same list always gives the same answer.
/// </description></item>
/// <item><description>Filter by <see cref="AirportSearchOptions.MaxDistanceNauticalMiles"/> (inclusive).</description></item>
/// <item><description>Sort by distance, then identifier (ordinal), and keep <see cref="AirportSearchOptions.MaxResults"/>.</description></item>
/// </list>
/// </remarks>
internal static class AirportSelection
{
    /// <summary>Feet in one meter: the simulator gives airport altitude in meters.</summary>
    internal const double FeetPerMeter = 1 / 0.3048;

    internal static IReadOnlyList<AirportInfo> Select(IEnumerable<RawAirport> raw, GeoPosition from, AirportSearchOptions options)
    {
        var byIcao = new Dictionary<string, AirportInfo>(StringComparer.Ordinal);
        foreach (var entry in raw)
        {
            if (Normalize(entry, from) is not { } airport)
            {
                continue;
            }

            if (!byIcao.TryGetValue(airport.Icao, out var existing) || Precedes(airport, existing))
            {
                byIcao[airport.Icao] = airport;
            }
        }

        return byIcao.Values
            .Where(a => a.DistanceNauticalMiles <= options.MaxDistanceNauticalMiles)
            .OrderBy(a => a.DistanceNauticalMiles)
            .ThenBy(a => a.Icao, StringComparer.Ordinal)
            .Take(options.MaxResults)
            .ToArray();
    }

    internal static AirportInfo? Normalize(RawAirport entry, GeoPosition from)
    {
        var icao = entry.Ident.Trim().ToUpperInvariant();
        if (icao.Length == 0 || GeoPosition.TryFrom(entry.Latitude, entry.Longitude) is not { } position)
        {
            return null;
        }

        var region = entry.Region.Trim().ToUpperInvariant();
        return new AirportInfo
        {
            Icao = icao,
            Region = region.Length == 0 ? null : region,
            Position = position,
            ElevationFeet = double.IsFinite(entry.AltitudeMeters) ? entry.AltitudeMeters * FeetPerMeter : null,
            DistanceNauticalMiles = from.DistanceNauticalMilesTo(position),
        };
    }

    private static bool Precedes(AirportInfo candidate, AirportInfo existing) =>
        candidate.DistanceNauticalMiles != existing.DistanceNauticalMiles
            ? candidate.DistanceNauticalMiles < existing.DistanceNauticalMiles
            : (candidate.Position.LatitudeDegrees, candidate.Position.LongitudeDegrees).CompareTo(
                (existing.Position.LatitudeDegrees, existing.Position.LongitudeDegrees)) < 0;
}
