using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

public class GearAndFlapsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gear_handle_and_actual_gear_position_are_independent()
    {
        // Gear handle selected DOWN, gear still in transit: the command and the state differ.
        var gear = new LandingGearTelemetry
        {
            HandleDown = Known(true),
            Units =
            [
                new GearUnitTelemetry { Id = "nose", Name = "Nose gear", ExtensionPercent = Known(40d) },
                new GearUnitTelemetry { Id = "left-main", Name = "Left main gear", ExtensionPercent = Known(35d) },
                new GearUnitTelemetry { Id = "right-main", Name = "Right main gear", ExtensionPercent = Known(35d) },
            ],
        };

        Assert.True(gear.HandleDown.Value);
        Assert.Equal(3, gear.Units.Count);
        Assert.All(gear.Units, unit => Assert.True(unit.ExtensionPercent.Value < 100));
    }

    [Fact]
    public void Gear_layout_is_not_limited_to_three_units()
    {
        var gear = new LandingGearTelemetry
        {
            Units = new[] { "nose", "left-main", "center-main", "right-main" }.Select(id => new GearUnitTelemetry { Id = id, Name = id }).ToArray(),
        };

        Assert.Equal(4, gear.Units.Count);
        Assert.Equal(ValueState.Unavailable, gear.HandleDown.State);
    }

    [Fact]
    public void Flap_handle_and_flap_surfaces_are_distinct_readings()
    {
        // Handle moved to the next detent, surfaces still travelling.
        var controls = new FlightControlsTelemetry
        {
            FlapsHandlePercent = Known(50d),
            FlapSurfaces =
            [
                new FlapSurfaceTelemetry { Id = "left", Name = "Left flaps", ExtensionPercent = Known(22.7) },
                new FlapSurfaceTelemetry { Id = "right", Name = "Right flaps", ExtensionPercent = Known(22.9) },
            ],
        };

        Assert.Equal(50d, controls.FlapsHandlePercent.Value);
        Assert.Equal([22.7, 22.9], controls.FlapSurfaces.Select(s => s.ExtensionPercent.Value));
        Assert.Equal(ValueState.Unavailable, controls.SpeedBrakeDeploymentPercent.State);
    }

    [Fact]
    public void A_provider_can_report_the_handle_without_surface_positions()
    {
        var controls = new FlightControlsTelemetry { FlapsHandlePercent = Known(0d) };

        Assert.Equal(0d, controls.FlapsHandlePercent.Value);
        Assert.Empty(controls.FlapSurfaces);
    }

    [Fact]
    public void Speed_warnings_default_to_unavailable_not_false()
    {
        var warnings = new WarningsTelemetry { Overspeed = Known(false) };

        Assert.False(warnings.Overspeed.Value);
        Assert.Equal(ValueState.Unavailable, warnings.FlapSpeedExceeded.State);
        Assert.Equal(ValueState.Unavailable, warnings.GearSpeedExceeded.State);
        Assert.Equal(ValueState.Unavailable, warnings.Stall.State);
    }

    private static TelemetryValue<T> Known<T>(T value)
        where T : struct => TelemetryValue<T>.Known(value, T0);
}
