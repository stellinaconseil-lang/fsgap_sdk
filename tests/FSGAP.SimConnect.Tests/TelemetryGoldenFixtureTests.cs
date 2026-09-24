using System.Text.Json;
using System.Text.Json.Serialization;
using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Telemetry;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// Golden parity: for recorded flight situations, the normalized snapshot must carry what the audited applications
/// derived from the same raw SimVars (Fixtures/telemetry-golden.json).
/// </summary>
public class TelemetryGoldenFixtureTests
{
    private const int Precision = 6;

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static TheoryData<string> Scenarios()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in Load().Scenarios)
        {
            data.Add(scenario.Name);
        }

        return data;
    }

    [Fact]
    public void Every_required_situation_has_a_fixture()
    {
        var names = Load().Scenarios.Select(s => s.Name).ToArray();

        Assert.Equal(
            ["parked", "taxi", "takeoff", "climb", "cruise", "approach", "landing", "gear-in-transit", "flaps-handle-ahead-of-surfaces", "overspeed"],
            names);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Snapshot_matches_the_legacy_derivation(string name)
    {
        var s = Load().Scenarios.Single(x => x.Name == name);
        var clock = new TestClock();
        var source = new TelemetrySource(clock, TimeSpan.FromSeconds(15));
        source.ApplyFast(s.Fast, clock.GetUtcNow(), source.Generation);
        source.ApplyNormal(s.Normal, clock.GetUtcNow(), source.Generation);
        source.ApplySlow(s.Slow, clock.GetUtcNow(), source.Generation);
        source.ApplyEnvironment(s.Environment, clock.GetUtcNow(), source.Generation);
        var t = await source.GetSnapshotAsync();
        var e = s.Expected;

        // Derived values: the legacy formula applied to the raw reading.
        Assert.Equal(e.VerticalSpeedFpm, t.Flight.VerticalSpeedFeetPerMinute.Value, Precision);
        if (e.TouchdownFpm is { } touchdown)
        {
            Assert.Equal(touchdown, t.Flight.TouchdownVerticalSpeedFeetPerMinute.Value, Precision);
        }
        else
        {
            Assert.Equal(ValueState.Unknown, t.Flight.TouchdownVerticalSpeedFeetPerMinute.State);
        }

        Assert.Equal(e.Pitch, t.Flight.PitchDegrees.Value, Precision);
        Assert.Equal(e.Bank, t.Flight.BankDegrees.Value, Precision);
        Assert.Equal(e.OnGround, t.Flight.OnGround.Value);
        Assert.Equal(e.HandleDown, t.LandingGear.HandleDown.Value);
        Assert.Equal(e.SpeedBrake, t.FlightControls.SpeedBrakeDeploymentPercent.Value, Precision);
        Assert.Equal(e.Running1, t.Engines[0].Running.Value);
        Assert.Equal(e.Running2, t.Engines[1].Running.Value);
        Assert.Equal(e.FuelFlow1Kgh, t.Engines[0].FuelFlowKilogramsPerHour.Value, Precision);
        Assert.Equal(e.FuelFlow2Kgh, t.Engines[1].FuelFlowKilogramsPerHour.Value, Precision);
        Assert.Equal(e.Overspeed, t.Warnings.Overspeed.Value);
        Assert.Equal(e.FlapSpeedExceeded, t.Warnings.FlapSpeedExceeded.Value);
        Assert.Equal(e.GearSpeedExceeded, t.Warnings.GearSpeedExceeded.Value);
        Assert.Equal(e.Stall, t.Warnings.Stall.Value);
        Assert.Equal(e.CabinRateFpm, t.Pressurization.CabinAltitudeRateFeetPerMinute.Value, Precision);
        if (e.Precipitation is { } precipitation)
        {
            Assert.Equal(precipitation, t.Environment.Precipitation.Value);
        }
        else
        {
            Assert.Equal(ValueState.Unknown, t.Environment.Precipitation.State);
        }

        // Pass-through values: unchanged.
        Assert.Equal(s.Fast.LatitudeDegrees, t.Flight.LatitudeDegrees.Value);
        Assert.Equal(s.Fast.LongitudeDegrees, t.Flight.LongitudeDegrees.Value);
        Assert.Equal(s.Fast.AltitudeFeet, t.Flight.AltitudeFeet.Value);
        Assert.Equal(s.Fast.HeightAboveGroundFeet, t.Flight.HeightAboveGroundFeet.Value);
        Assert.Equal(s.Fast.IndicatedAirspeedKnots, t.Flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(s.Fast.GroundSpeedKnots, t.Flight.GroundSpeedKnots.Value);
        Assert.Equal(s.Fast.HeadingMagneticDegrees, t.Flight.HeadingMagneticDegrees.Value);
        Assert.Equal(s.Fast.GLoad, t.Flight.GLoad.Value);
        Assert.Equal([s.Normal.GearCenterPercent, s.Normal.GearLeftPercent, s.Normal.GearRightPercent], t.LandingGear.Units.Select(u => u.ExtensionPercent.Value));
        Assert.Equal(s.Normal.FlapsHandlePercent, t.FlightControls.FlapsHandlePercent.Value);
        Assert.Equal([s.Normal.FlapsLeftPercent, s.Normal.FlapsRightPercent], t.FlightControls.FlapSurfaces.Select(f => f.ExtensionPercent.Value));
        Assert.Equal(s.Slow.Engine1N1Percent, t.Engines[0].N1Percent.Value);
        Assert.Equal(s.Slow.Engine2N2Percent, t.Engines[1].N2Percent.Value);
        Assert.Equal(s.Slow.Engine1EgtCelsius, t.Engines[0].EgtCelsius.Value);
        Assert.Equal(s.Fast.AngleOfAttackDegrees, t.Flight.AngleOfAttackDegrees.Value);
        Assert.Equal(s.Fast.GrossWeightKilograms, t.Flight.GrossWeightKilograms.Value);
        Assert.Equal([s.Fast.BodyAccelerationXG, s.Fast.BodyAccelerationYG, s.Fast.BodyAccelerationZG], [t.Flight.BodyAccelerationXG.Value, t.Flight.BodyAccelerationYG.Value, t.Flight.BodyAccelerationZG.Value]);
        Assert.Equal(s.Fast.AileronLeftPercent, t.FlightControls.AileronLeftDeflectionPercent.Value);
        Assert.Equal(s.Fast.ElevatorPercent, t.FlightControls.ElevatorDeflectionPercent.Value);
        Assert.Equal(s.Fast.RudderPercent, t.FlightControls.RudderDeflectionPercent.Value);
        Assert.Equal(s.Normal.BrakeLeftPercent, t.LandingGear.BrakeLeftPercent.Value);
        Assert.Equal(s.Normal.BrakeRightPercent, t.LandingGear.BrakeRightPercent.Value);
        Assert.Equal(s.Normal.SteeringInputPercent, t.LandingGear.SteeringInputPercent.Value);
        Assert.Equal(s.Normal.AntiskidActive != 0.0, t.LandingGear.AntiskidActive.Value);
        Assert.Equal(s.Slow.Engine1OilTemperatureCelsius, t.Engines[0].OilTemperatureCelsius.Value);
        Assert.Equal(s.Slow.Engine2OilPressurePsi, t.Engines[1].OilPressurePsi.Value);
        Assert.Equal(s.Slow.Engine1ThrottleLeverPercent, t.Engines[0].ThrottleLeverPercent.Value);
        Assert.Equal(s.Slow.Engine2ReverserEngaged != 0.0, t.Engines[1].ReverserEngaged.Value);
        Assert.Equal(s.Slow.CabinAltitudeFeet, t.Pressurization.CabinAltitudeFeet.Value);
        Assert.Equal(s.Environment.OutsideAirTemperatureCelsius, t.Environment.OutsideAirTemperatureCelsius.Value);
        Assert.Equal(s.Environment.WindDirectionDegreesTrue, t.Environment.WindDirectionDegreesTrue.Value);
        Assert.Equal(s.Environment.WindSpeedKnots, t.Environment.WindSpeedKnots.Value);
        Assert.Equal(s.Environment.PrecipitationRateMillimeters, t.Environment.PrecipitationRateMillimeters.Value);

        // Never invented.
        Assert.Equal(ValueState.Unavailable, t.Flight.RadioAltitudeFeet.State);
    }

    private static GoldenFile Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "telemetry-golden.json");
        return JsonSerializer.Deserialize<GoldenFile>(File.ReadAllText(path), Json)!;
    }

    private sealed record GoldenFile(IReadOnlyList<Scenario> Scenarios);

    private sealed record Scenario(string Name, FastGroupVars Fast, NormalGroupVars Normal, SlowGroupVars Slow, EnvironmentGroupVars Environment, Expected Expected);

    private sealed record Expected(
        double VerticalSpeedFpm,
        double? TouchdownFpm,
        double Pitch,
        double Bank,
        bool OnGround,
        bool HandleDown,
        double SpeedBrake,
        bool Running1,
        bool Running2,
        double FuelFlow1Kgh,
        double FuelFlow2Kgh,
        bool Overspeed,
        bool FlapSpeedExceeded,
        bool GearSpeedExceeded,
        bool Stall,
        double CabinRateFpm,
        PrecipitationType? Precipitation);
}
