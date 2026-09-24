using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Abstractions.Tests;

/// <summary>BLOCK 8 contracts: coordinates, great-circle distance, airport search options and errors.</summary>
public class GeoPositionAndAirportContractTests
{
    private static readonly GeoPosition Lfmn = new(43.665278, 7.215000);
    private static readonly GeoPosition Lfmd = new(43.546389, 6.954167);

    [Fact]
    public void The_same_position_is_zero_nautical_miles_away()
    {
        Assert.Equal(0.0, Lfmn.DistanceNauticalMilesTo(Lfmn));
    }

    [Fact]
    public void Nice_to_cannes_is_the_known_great_circle_distance()
    {
        // Independent check: Δlat 0.118889° = 7.13 NM; Δlon 0.260833° × cos(43.61°) = 11.33 NM; √(7.13² + 11.33²) = 13.39 NM.
        Assert.Equal(13.40, Lfmn.DistanceNauticalMilesTo(Lfmd), 1);
        Assert.Equal(Lfmn.DistanceNauticalMilesTo(Lfmd), Lfmd.DistanceNauticalMilesTo(Lfmn), 12);
    }

    [Fact]
    public void One_minute_of_latitude_is_about_one_nautical_mile()
    {
        var a = new GeoPosition(45.0, 5.0);
        var b = new GeoPosition(45.0 + (1.0 / 60), 5.0);

        Assert.InRange(a.DistanceNauticalMilesTo(b), 0.999, 1.001);
    }

    [Fact]
    public void Crossing_the_antimeridian_takes_the_short_way()
    {
        var west = new GeoPosition(0.0, 179.9);
        var east = new GeoPosition(0.0, -179.9);

        Assert.InRange(west.DistanceNauticalMilesTo(east), 11.9, 12.1); // 0.2° of equator ≈ 12 NM, not 21,588
    }

    [Fact]
    public void High_latitudes_and_poles_are_handled()
    {
        var a = new GeoPosition(89.9, 0.0);
        var b = new GeoPosition(89.9, 180.0);

        Assert.InRange(a.DistanceNauticalMilesTo(b), 11.9, 12.1); // across the pole
        Assert.InRange(new GeoPosition(90, 0).DistanceNauticalMilesTo(new GeoPosition(-90, 0)), 10_800, 10_810);
    }

    [Theory]
    [InlineData(90.0001, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 180.0001)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Invalid_coordinates_are_rejected(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(latitude, longitude));
        Assert.Null(GeoPosition.TryFrom(latitude, longitude));
    }

    [Fact]
    public void Try_from_accepts_valid_readings()
    {
        Assert.Equal(Lfmn, GeoPosition.TryFrom(43.665278, 7.215));
    }

    [Fact]
    public void Search_defaults_are_defined_once_and_validated()
    {
        Assert.Equal(50, AirportSearchOptions.Default.MaxDistanceNauticalMiles);
        Assert.Equal(10, AirportSearchOptions.Default.MaxResults);
        AirportSearchOptions.Default.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirportSearchOptions { MaxDistanceNauticalMiles = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirportSearchOptions { MaxDistanceNauticalMiles = 5000 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirportSearchOptions { MaxResults = 0 }.Validate());
    }

    [Fact]
    public void A_service_error_carries_its_reason()
    {
        var error = new SimulatorServiceException(SimulatorServiceError.QueryFailed, "rejected");

        Assert.Equal(SimulatorServiceError.QueryFailed, error.Error);
        Assert.Equal("rejected", error.Message);
    }
}
