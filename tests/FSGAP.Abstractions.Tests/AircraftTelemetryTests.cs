using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

public class AircraftTelemetryTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unavailable_snapshot_reports_nothing_as_known()
    {
        var telemetry = AircraftTelemetry.Unavailable(Timestamp);

        Assert.Equal(Timestamp, telemetry.Timestamp);
        Assert.Equal(ValueState.Unavailable, telemetry.Flight.OnGround.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Flight.HeightAboveGroundFeet.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Flight.TouchdownVerticalSpeedFeetPerMinute.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Warnings.Overspeed.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Apu.FireDetected.State);
        Assert.Equal(ValueState.Unavailable, telemetry.LandingGear.HandleDown.State);
        Assert.Equal(ValueState.Unavailable, telemetry.FlightControls.FlapsHandlePercent.State);
        Assert.Empty(telemetry.Engines);
        Assert.Empty(telemetry.InertialReferences);
        Assert.Empty(telemetry.FuelPumps);
        Assert.Empty(telemetry.ElectricalBuses);
        Assert.Empty(telemetry.HydraulicSystems);
        Assert.Empty(telemetry.FireZones);
        Assert.Empty(telemetry.Batteries);
        Assert.Empty(telemetry.LandingGear.Units);
        Assert.Empty(telemetry.FlightControls.FlapSurfaces);
    }

    [Fact]
    public void The_0_9_fields_and_sections_default_to_unavailable()
    {
        var telemetry = AircraftTelemetry.Unavailable(Timestamp);

        Assert.Equal(ValueState.Unavailable, telemetry.Flight.AngleOfAttackDegrees.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Flight.GrossWeightKilograms.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Flight.BodyAccelerationYG.State);
        Assert.Equal(ValueState.Unavailable, telemetry.LandingGear.BrakeLeftPercent.State);
        Assert.Equal(ValueState.Unavailable, telemetry.LandingGear.AntiskidActive.State);
        Assert.Equal(ValueState.Unavailable, telemetry.FlightControls.RudderDeflectionPercent.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Pressurization.CabinAltitudeFeet.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Pressurization.CabinAltitudeRateFeetPerMinute.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Environment.OutsideAirTemperatureCelsius.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Environment.Precipitation.State);
        Assert.Equal(ValueState.Unavailable, new EngineTelemetry { Index = 1 }.OilPressurePsi.State);
        Assert.Equal(ValueState.Unavailable, new EngineTelemetry { Index = 1 }.ReverserEngaged.State);
        Assert.Equal(ValueState.Unavailable, new HydraulicSystemTelemetry { Id = "green", Name = "Green" }.ReservoirPercent.State);
        Assert.Equal(ValueState.Unavailable, new BatteryTelemetry { Id = "bat-1", Name = "Battery 1" }.VoltageVolts.State);
    }

    [Fact]
    public void Batteries_are_copied_and_read_only_like_every_other_collection()
    {
        var batteries = new List<BatteryTelemetry> { new() { Id = "bat-1", Name = "Battery 1", VoltageVolts = TelemetryValue<double>.Known(28.0, Timestamp) } };
        var telemetry = AircraftTelemetry.Unavailable(Timestamp) with { Batteries = batteries };

        batteries.Add(new BatteryTelemetry { Id = "bat-2", Name = "Battery 2" });

        Assert.Equal("bat-1", Assert.Single(telemetry.Batteries).Id);
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<BatteryTelemetry>>(telemetry.Batteries).Clear());
        Assert.Throws<ArgumentNullException>(() => AircraftTelemetry.Unavailable(Timestamp) with { Batteries = null! });
    }

    [Fact]
    public void Systems_are_collections_of_any_size()
    {
        var telemetry = AircraftTelemetry.Unavailable(Timestamp) with
        {
            Engines = [new EngineTelemetry { Index = 1, Running = Known(true), N1Percent = Known(21.5) }],
            InertialReferences =
            [
                new InertialReferenceTelemetry { Index = 1, Mode = Known(InertialReferenceMode.Navigation), Aligned = Known(true) },
                new InertialReferenceTelemetry { Index = 2, Mode = Known(InertialReferenceMode.Align), Aligned = Known(false) },
            ],
        };

        var engine = Assert.Single(telemetry.Engines);
        Assert.True(engine.Running.Value);
        Assert.Equal(21.5, engine.N1Percent.Value);
        Assert.Equal(ValueState.Unavailable, engine.N2Percent.State);
        Assert.Equal(2, telemetry.InertialReferences.Count);
        Assert.False(telemetry.InertialReferences[1].Aligned.Value);
    }

    [Fact]
    public void Mutating_the_source_collection_does_not_change_the_snapshot()
    {
        var engines = new List<EngineTelemetry> { new() { Index = 1 } };
        var telemetry = AircraftTelemetry.Unavailable(Timestamp) with { Engines = engines };

        engines.Add(new EngineTelemetry { Index = 2 });
        engines[0] = new EngineTelemetry { Index = 99 };

        var engine = Assert.Single(telemetry.Engines);
        Assert.Equal(1, engine.Index);
    }

    [Fact]
    public void Consumers_cannot_mutate_snapshot_collections_by_casting()
    {
        var telemetry = AircraftTelemetry.Unavailable(Timestamp) with
        {
            Engines = [new EngineTelemetry { Index = 1 }],
            LandingGear = new LandingGearTelemetry { Units = [new GearUnitTelemetry { Id = "nose", Name = "Nose" }] },
        };

        var engines = Assert.IsAssignableFrom<IList<EngineTelemetry>>(telemetry.Engines);
        var units = Assert.IsAssignableFrom<IList<GearUnitTelemetry>>(telemetry.LandingGear.Units);

        Assert.Throws<NotSupportedException>(() => engines.Add(new EngineTelemetry { Index = 2 }));
        Assert.Throws<NotSupportedException>(() => engines[0] = new EngineTelemetry { Index = 2 });
        Assert.Throws<NotSupportedException>(() => units.Clear());
    }

    [Fact]
    public void Deriving_a_snapshot_leaves_the_original_untouched()
    {
        var original = AircraftTelemetry.Unavailable(Timestamp);

        var derived = original with { Flight = original.Flight with { OnGround = Known(true) } };

        Assert.Equal(ValueState.Unavailable, original.Flight.OnGround.State);
        Assert.True(derived.Flight.OnGround.Value);
        Assert.NotSame(original.Flight, derived.Flight);
    }

    [Fact]
    public void Null_collections_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => AircraftTelemetry.Unavailable(Timestamp) with { FuelPumps = null! });
    }

    private static TelemetryValue<T> Known<T>(T value)
        where T : struct => TelemetryValue<T>.Known(value, Timestamp);
}
