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
        Assert.Equal(ValueState.Unavailable, telemetry.Flight.AltitudeFeet.State);
        Assert.Equal(ValueState.Unavailable, telemetry.Apu.FireDetected.State);
        Assert.Equal(ValueState.Unavailable, telemetry.FlightControls.FlapsExtensionPercent.State);
        Assert.Empty(telemetry.Engines);
        Assert.Empty(telemetry.InertialReferences);
        Assert.Empty(telemetry.FuelPumps);
        Assert.Empty(telemetry.ElectricalBuses);
        Assert.Empty(telemetry.HydraulicSystems);
        Assert.Empty(telemetry.FireZones);
    }

    [Fact]
    public void Systems_are_collections_of_any_size()
    {
        var telemetry = AircraftTelemetry.Unavailable(Timestamp) with
        {
            Engines = [new EngineTelemetry { Index = 1, Running = true, N1Percent = 21.5 }],
            InertialReferences =
            [
                new InertialReferenceTelemetry { Index = 1, Mode = InertialReferenceMode.Navigation, Aligned = true },
                new InertialReferenceTelemetry { Index = 2, Mode = InertialReferenceMode.Align, Aligned = false },
            ],
        };

        var engine = Assert.Single(telemetry.Engines);
        Assert.True(engine.Running.Value);
        Assert.Equal(21.5, engine.N1Percent.Value);
        Assert.Equal(ValueState.Unavailable, engine.N2Percent.State);
        Assert.Equal(2, telemetry.InertialReferences.Count);
        Assert.False(telemetry.InertialReferences[1].Aligned.Value);
    }
}
