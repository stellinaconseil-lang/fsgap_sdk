using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Telemetry;
using FSGAP.Fenix.Variables;

namespace FSGAP.Fenix.Tests;

/// <summary>Generic snapshot + Fenix policy + Fenix overlay.</summary>
public class FenixTelemetryCompositionTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    private static AircraftTelemetry Generic() => AircraftTelemetry.Unavailable(At) with
    {
        Flight = new FlightStateTelemetry { IndicatedAirspeedKnots = TelemetryValue<double>.Known(250.0, At) },
        Engines =
        [
            new EngineTelemetry { Index = 1, N1Percent = TelemetryValue<double>.Known(82.0, At) },
            new EngineTelemetry { Index = 2, N1Percent = TelemetryValue<double>.Known(81.5, At) },
        ],
        FlightControls = new FlightControlsTelemetry
        {
            FlapsHandlePercent = TelemetryValue<double>.Known(0.0, At),
            SpeedBrakeDeploymentPercent = TelemetryValue<double>.Known(37.0, At),
        },
    };

    private static FenixSystemState Fenix(DateTimeOffset at)
    {
        var raw = new double[FenixVariables.Cockpit.Count];
        raw[(int)FenixVariables.CockpitIndex.Ir1Mode] = 1;
        raw[(int)FenixVariables.CockpitIndex.FuelLeft1] = 1;
        raw[(int)FenixVariables.CockpitIndex.Eng1FireHandle] = 1;
        var state = FenixSystemMapper.ApplyCockpit(FenixSystemState.Empty, raw, at);
        return FenixSystemMapper.ApplyHydraulics(state, [3000, 2990], at);
    }

    [Fact]
    public void Generic_alone_gives_exactly_the_masked_generic_snapshot()
    {
        var generic = Generic();

        var composed = FenixTelemetryComposer.Compose(generic, FenixSystemState.Empty, aircraftReplaced: false, StaleAfter);

        Assert.Equal(ValueState.Unavailable, composed.FlightControls.SpeedBrakeDeploymentPercent.State);
        Assert.Equal(generic.Flight, composed.Flight);
        Assert.Equal(generic.Engines, composed.Engines);
        Assert.Empty(composed.InertialReferences);
        Assert.Empty(composed.FuelPumps);
        Assert.Empty(composed.HydraulicSystems);
    }

    [Fact]
    public void The_overlay_adds_the_fenix_sections_and_leaves_unrelated_generic_fields_unchanged()
    {
        var generic = Generic();

        var composed = FenixTelemetryComposer.Compose(generic, Fenix(At), aircraftReplaced: false, StaleAfter);

        Assert.Equal(InertialReferenceMode.Navigation, composed.InertialReferences[0].Mode.Value);
        Assert.True(composed.FuelPumps[0].IsOn.Value);
        Assert.Equal(3000.0, composed.HydraulicSystems[0].PressurePsi.Value);
        Assert.True(composed.Engines[0].FireHandlePulled.Value);
        Assert.Equal(82.0, composed.Engines[0].N1Percent.Value);
        Assert.Equal(81.5, composed.Engines[1].N1Percent.Value);
        Assert.Equal(generic.Flight, composed.Flight);
        Assert.Equal(generic.Warnings, composed.Warnings);
        Assert.Equal(generic.LandingGear.HandleDown, composed.LandingGear.HandleDown);
        Assert.Equal(generic.FlightControls.FlapsHandlePercent, composed.FlightControls.FlapsHandlePercent);
    }

    [Fact]
    public void A_masked_generic_value_stays_unavailable_when_fenix_has_no_replacement()
    {
        var composed = FenixTelemetryComposer.Compose(Generic(), Fenix(At), aircraftReplaced: false, StaleAfter);

        Assert.Equal(ValueState.Unavailable, composed.FlightControls.SpeedBrakeDeploymentPercent.State);
        Assert.Equal(ValueState.Unavailable, composed.Apu.Running.State);
    }

    [Fact]
    public void A_fenix_value_wins_over_a_generic_one_and_an_unavailable_fenix_value_leaves_the_generic_one()
    {
        var generic = TelemetryValue<bool>.Known(false, At);
        var fenix = TelemetryValue<bool>.Known(true, At.AddSeconds(1));

        Assert.Equal(fenix, FenixTelemetryComposer.Prefer(fenix, generic));
        Assert.Equal(generic, FenixTelemetryComposer.Prefer(TelemetryValue<bool>.Unavailable, generic));
        Assert.Equal(TelemetryValue<bool>.Unknown, FenixTelemetryComposer.Prefer(TelemetryValue<bool>.Unknown, generic));
    }

    [Fact]
    public void Engines_not_yet_reported_generically_still_carry_their_fire_panel()
    {
        var composed = FenixTelemetryComposer.Compose(AircraftTelemetry.Unavailable(At), Fenix(At), aircraftReplaced: false, StaleAfter);

        Assert.Equal([1, 2], composed.Engines.Select(e => e.Index));
        Assert.Equal(ValueState.Unavailable, composed.Engines[0].N1Percent.State);
        Assert.True(composed.Engines[0].FireHandlePulled.Value);
    }

    [Fact]
    public void Each_fenix_value_keeps_its_own_receive_time()
    {
        var composed = FenixTelemetryComposer.Compose(Generic() with { Timestamp = At.AddSeconds(3) }, Fenix(At.AddSeconds(2)), false, StaleAfter);

        Assert.Equal(At.AddSeconds(2), composed.InertialReferences[0].Mode.ObservedAt);
        Assert.Equal(At, composed.Flight.IndicatedAirspeedKnots.ObservedAt);
    }

    [Fact]
    public void Fenix_values_turn_unknown_after_stale_after_like_generic_ones()
    {
        var generic = Generic() with { Timestamp = At + StaleAfter + TimeSpan.FromSeconds(1) };

        var composed = FenixTelemetryComposer.Compose(generic, Fenix(At), false, StaleAfter);

        Assert.Equal(ValueState.Unknown, composed.InertialReferences[0].Mode.State);
        Assert.Equal(ValueState.Unknown, composed.FuelPumps[0].IsOn.State);
        Assert.Equal(ValueState.Unknown, composed.Engines[0].FireHandlePulled.State);
        Assert.Equal(ValueState.Unknown, composed.HydraulicSystems[0].PressurePsi.State);
        Assert.Equal(ValueState.Unavailable, composed.HydraulicSystems[2].PressurePsi.State);
    }

    [Fact]
    public void Nothing_is_published_once_another_aircraft_is_loaded()
    {
        var composed = FenixTelemetryComposer.Compose(Generic(), Fenix(At), aircraftReplaced: true, StaleAfter);

        Assert.Equal(AircraftTelemetry.Unavailable(At).Flight, composed.Flight);
        Assert.Empty(composed.InertialReferences);
        Assert.Empty(composed.Engines);
    }
}
