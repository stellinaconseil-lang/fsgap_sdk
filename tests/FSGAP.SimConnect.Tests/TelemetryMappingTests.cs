using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Telemetry;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>Unit conversions and the raw-group to contract mapping, without a simulator.</summary>
public class TelemetryMappingTests
{
    private static readonly DateTimeOffset At = TestClock.Start;

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(10.0, 600.0)]
    [InlineData(-12.5, -750.0)]
    public void Vertical_speed_feet_per_second_becomes_feet_per_minute(double fps, double fpm)
    {
        Assert.Equal(fpm, TelemetryConversions.FeetPerSecondToFeetPerMinute(fps), 9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.45359237)]
    [InlineData(5000.0, 2267.96185)]
    public void Fuel_flow_pounds_per_hour_becomes_kilograms_per_hour(double pph, double kgh)
    {
        Assert.Equal(kgh, TelemetryConversions.PoundsPerHourToKilogramsPerHour(pph), 6);
    }

    [Theory]
    [InlineData(3.0, -180.0)]
    [InlineData(-3.0, -180.0)]
    [InlineData(0.5, -30.0)]
    public void Touchdown_rate_is_a_negative_magnitude_whatever_the_raw_sign(double fps, double fpm)
    {
        Assert.Equal(fpm, TelemetryConversions.TouchdownToFeetPerMinute(fps));
    }

    [Fact]
    public void Zero_touchdown_velocity_means_no_touchdown_recorded()
    {
        Assert.Null(TelemetryConversions.TouchdownToFeetPerMinute(0.0));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(49.9, false)]
    [InlineData(50.0, true)]
    [InlineData(100.0, true)]
    public void Gear_handle_is_down_from_the_midpoint(double percent, bool down)
    {
        Assert.Equal(down, TelemetryConversions.IsGearHandleDown(percent));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(-1.0, true)]
    public void Simconnect_booleans_are_non_zero(double raw, bool expected)
    {
        Assert.Equal(expected, TelemetryConversions.ToBoolean(raw));
    }

    [Fact]
    public void Pitch_and_bank_are_negated_into_the_contract_convention()
    {
        // MSFS: pitch positive nose down, bank positive left wing down. Contract: nose up, right wing down.
        var flight = GenericTelemetryMapper.ToFlightState(new FastGroupVars { PitchDegrees = -5.0, BankDegrees = -20.0 }, At);

        Assert.Equal(5.0, flight.PitchDegrees.Value);
        Assert.Equal(20.0, flight.BankDegrees.Value);
    }

    [Fact]
    public void A_level_attitude_is_positive_zero()
    {
        var flight = GenericTelemetryMapper.ToFlightState(default, At);

        Assert.False(double.IsNegative(flight.PitchDegrees.Value));
        Assert.False(double.IsNegative(flight.BankDegrees.Value));
    }

    [Fact]
    public void Flight_state_maps_every_read_value_as_known_at_the_group_time()
    {
        var vars = new FastGroupVars
        {
            LatitudeDegrees = 48.7233,
            LongitudeDegrees = 2.3794,
            AltitudeFeet = 291.0,
            HeightAboveGroundFeet = 4.2,
            IndicatedAirspeedKnots = 142.0,
            GroundSpeedKnots = 139.5,
            VerticalSpeedFeetPerSecond = -12.0,
            TouchdownNormalVelocityFeetPerSecond = 2.5,
            HeadingMagneticDegrees = 65.0,
            GLoad = 1.18,
            OnGround = 1.0,
        };

        var flight = GenericTelemetryMapper.ToFlightState(vars, At);

        Assert.Equal(48.7233, flight.LatitudeDegrees.Value);
        Assert.Equal(2.3794, flight.LongitudeDegrees.Value);
        Assert.Equal(291.0, flight.AltitudeFeet.Value);
        Assert.Equal(4.2, flight.HeightAboveGroundFeet.Value);
        Assert.Equal(142.0, flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(139.5, flight.GroundSpeedKnots.Value);
        Assert.Equal(-720.0, flight.VerticalSpeedFeetPerMinute.Value);
        Assert.Equal(-150.0, flight.TouchdownVerticalSpeedFeetPerMinute.Value);
        Assert.Equal(65.0, flight.HeadingMagneticDegrees.Value);
        Assert.Equal(1.18, flight.GLoad.Value);
        Assert.True(flight.OnGround.Value);
        Assert.Equal(At, flight.AltitudeFeet.ObservedAt);
        Assert.Equal(At, flight.OnGround.ObservedAt);
    }

    [Fact]
    public void Zero_readings_are_known_zeros_not_missing_values()
    {
        var flight = GenericTelemetryMapper.ToFlightState(default, At);
        var warnings = GenericTelemetryMapper.ToWarnings(default, At);

        Assert.Equal(0.0, flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(0.0, flight.VerticalSpeedFeetPerMinute.Value);
        Assert.False(flight.OnGround.Value);
        Assert.False(warnings.Overspeed.Value);
        Assert.True(warnings.Stall.IsKnown);
    }

    [Fact]
    public void Values_without_a_trusted_generic_source_stay_unavailable()
    {
        var flight = GenericTelemetryMapper.ToFlightState(new FastGroupVars { HeightAboveGroundFeet = 1000.0 }, At);
        var engines = GenericTelemetryMapper.ToEngines(default, At);

        Assert.Equal(ValueState.Unavailable, flight.RadioAltitudeFeet.State);
        Assert.Equal(ValueState.Unknown, flight.TouchdownVerticalSpeedFeetPerMinute.State);
        Assert.All(engines, e => Assert.Equal(ValueState.Unavailable, e.FireDetected.State));
    }

    [Fact]
    public void Warnings_map_each_flag()
    {
        var warnings = GenericTelemetryMapper.ToWarnings(
            new FastGroupVars { OverspeedWarning = 1.0, FlapSpeedExceeded = 0.0, GearSpeedExceeded = 1.0, StallWarning = 0.0 }, At);

        Assert.True(warnings.Overspeed.Value);
        Assert.False(warnings.FlapSpeedExceeded.Value);
        Assert.True(warnings.GearSpeedExceeded.Value);
        Assert.False(warnings.Stall.Value);
    }

    [Fact]
    public void Gear_keeps_the_handle_and_each_leg_separate()
    {
        var gear = GenericTelemetryMapper.ToLandingGear(
            new NormalGroupVars { GearHandlePercent = 100.0, GearCenterPercent = 40.0, GearLeftPercent = 35.0, GearRightPercent = 37.0 }, At);

        Assert.True(gear.HandleDown.Value);
        Assert.Equal(["nose", "left-main", "right-main"], gear.Units.Select(u => u.Id));
        Assert.Equal([40.0, 35.0, 37.0], gear.Units.Select(u => u.ExtensionPercent.Value));
    }

    [Fact]
    public void Flaps_keep_the_handle_and_the_surfaces_separate_and_speed_brake_is_the_larger_side()
    {
        var controls = GenericTelemetryMapper.ToFlightControls(
            new NormalGroupVars { FlapsHandlePercent = 50.0, FlapsLeftPercent = 42.0, FlapsRightPercent = 41.5, SpoilersLeftPercent = 10.0, SpoilersRightPercent = 60.0 }, At);

        Assert.Equal(50.0, controls.FlapsHandlePercent.Value);
        Assert.Equal(["trailing-left", "trailing-right"], controls.FlapSurfaces.Select(s => s.Id));
        Assert.Equal([42.0, 41.5], controls.FlapSurfaces.Select(s => s.ExtensionPercent.Value));
        Assert.Equal(60.0, controls.SpeedBrakeDeploymentPercent.Value);
    }

    [Fact]
    public void Engines_are_indexed_from_one_with_fuel_flow_in_kilograms()
    {
        var engines = GenericTelemetryMapper.ToEngines(
            new SlowGroupVars
            {
                Engine1Combustion = 1.0, Engine1N1Percent = 85.2, Engine1N2Percent = 92.1, Engine1EgtCelsius = 620.0, Engine1FuelFlowPoundsPerHour = 5000.0,
                Engine2Combustion = 0.0, Engine2N1Percent = 0.0, Engine2N2Percent = 0.0, Engine2EgtCelsius = 25.0, Engine2FuelFlowPoundsPerHour = 0.0,
            },
            At);

        Assert.Equal([1, 2], engines.Select(e => e.Index));
        Assert.True(engines[0].Running.Value);
        Assert.Equal(85.2, engines[0].N1Percent.Value);
        Assert.Equal(92.1, engines[0].N2Percent.Value);
        Assert.Equal(620.0, engines[0].EgtCelsius.Value);
        Assert.Equal(2267.96185, engines[0].FuelFlowKilogramsPerHour.Value, 6);
        Assert.False(engines[1].Running.Value);
        Assert.Equal(25.0, engines[1].EgtCelsius.Value);
    }
}
