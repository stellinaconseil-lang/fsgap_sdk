using FSGAP.Abstractions.Telemetry;
using FSGAP.Synaptic.Telemetry;

namespace FSGAP.Synaptic.Tests;

/// <summary>Generic policy, overlay mapping and composition of a Synaptic A220 session.</summary>
public class TelemetryTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    private static TelemetryValue<double> K(double v) => TelemetryValue<double>.Known(v, At);

    private static TelemetryValue<bool> K(bool v) => TelemetryValue<bool>.Known(v, At);

    /// <summary>A generic snapshot shaped like the live 44 % N1 capture (BLOCK 10A-LIVE).</summary>
    private static AircraftTelemetry Generic() => AircraftTelemetry.Unavailable(At) with
    {
        Flight = new FlightStateTelemetry { OnGround = K(true), IndicatedAirspeedKnots = K(0), AltitudeFeet = K(1100) },
        Warnings = new WarningsTelemetry { Overspeed = K(false), Stall = K(false) },
        Engines =
        [
            new EngineTelemetry
            {
                Index = 1, Running = K(false), N1Percent = K(43.9), N2Percent = K(80.2), EgtCelsius = K(696), FuelFlowKilogramsPerHour = K(0),
                StarterActive = K(false), OilTemperatureCelsius = K(22), OilPressurePsi = K(104), ThrottleLeverPercent = K(14), ReverserEngaged = K(false),
            },
            new EngineTelemetry { Index = 2, Running = K(false), N1Percent = K(43.9) },
        ],
        Apu = new ApuTelemetry { BleedOn = K(false) },
        LandingGear = new LandingGearTelemetry { HandleDown = K(true), AntiskidActive = K(false), BrakeLeftPercent = K(32) },
        FlightControls = new FlightControlsTelemetry { FlapsHandlePercent = K(40), SpeedBrakeDeploymentPercent = K(0) },
        Pressurization = new PressurizationTelemetry { CabinAltitudeFeet = K(990), CabinAltitudeRateFeetPerMinute = K(0) },
        Environment = new EnvironmentTelemetry { OutsideAirTemperatureCelsius = K(21.7) },
    };

    // -- generic policy ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Generic_values_proven_wrong_on_the_a220_are_masked()
    {
        var masked = SynapticGenericTelemetryPolicy.Apply(Generic());
        var e = masked.Engines[0];

        Assert.Equal(ValueState.Unavailable, e.Running.State);
        Assert.Equal(ValueState.Unavailable, e.FuelFlowKilogramsPerHour.State);
        Assert.Equal(ValueState.Unavailable, e.StarterActive.State);
        Assert.Equal(ValueState.Unavailable, e.OilTemperatureCelsius.State);
        Assert.Equal(ValueState.Unavailable, e.OilPressurePsi.State);
        Assert.Equal(ValueState.Unavailable, e.ReverserEngaged.State);
        Assert.Equal(ValueState.Unavailable, masked.Engines[1].Running.State);
        Assert.Equal(ValueState.Unavailable, masked.Apu.BleedOn.State);
        Assert.Equal(ValueState.Unavailable, masked.Pressurization.CabinAltitudeFeet.State);
        Assert.Equal(ValueState.Unavailable, masked.Pressurization.CabinAltitudeRateFeetPerMinute.State);
    }

    [Fact]
    public void Validated_and_untested_generic_values_pass_through()
    {
        var masked = SynapticGenericTelemetryPolicy.Apply(Generic());
        var e = masked.Engines[0];

        Assert.Equal(43.9, e.N1Percent.Value);
        Assert.Equal(80.2, e.N2Percent.Value);
        Assert.Equal(696, e.EgtCelsius.Value);
        Assert.Equal(14, e.ThrottleLeverPercent.Value);
        Assert.False(masked.LandingGear.AntiskidActive.Value); // idem
        Assert.False(masked.Warnings.Overspeed.Value);
        Assert.Equal(1100, masked.Flight.AltitudeFeet.Value);
        Assert.Equal(40, masked.FlightControls.FlapsHandlePercent.Value);
        Assert.Equal(0, masked.FlightControls.SpeedBrakeDeploymentPercent.Value);
        Assert.Equal(32, masked.LandingGear.BrakeLeftPercent.Value);
        Assert.Equal(21.7, masked.Environment.OutsideAirTemperatureCelsius.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_generic_reverser_is_unavailable_whatever_it_says(bool generic)
    {
        // BLOCK 10B.3C: the generic value read false while the EICAS showed REV on both engines.
        var snapshot = Generic() with
        {
            Engines = [new EngineTelemetry { Index = 1, ReverserEngaged = K(generic), ThrottleLeverPercent = K(-20) }, new EngineTelemetry { Index = 2, ReverserEngaged = K(generic) }],
        };

        var masked = SynapticGenericTelemetryPolicy.Apply(snapshot);

        Assert.All(masked.Engines, e => Assert.Equal(ValueState.Unavailable, e.ReverserEngaged.State));
        Assert.Equal(-20, masked.Engines[0].ThrottleLeverPercent.Value); // the lever is still published, never read as a reverser state
    }

    [Fact]
    public void The_composed_session_snapshot_never_publishes_a_reverser_state()
    {
        var snapshot = Generic() with { Engines = [new EngineTelemetry { Index = 1, ReverserEngaged = K(true) }, new EngineTelemetry { Index = 2, ReverserEngaged = K(false) }] };

        var t = SynapticTelemetryComposer.Compose(snapshot, SynapticSystemMapper.Apply([1, 1, 0, 0, 0, 0], At), aircraftReplaced: false, StaleAfter);

        Assert.All(t.Engines, e => Assert.Equal(ValueState.Unavailable, e.ReverserEngaged.State));
    }

    // -- overlay mapping --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, "Off", "false")]
    [InlineData(1.0, "Auto", "unavailable")]
    [InlineData(2.0, "On", "true")]
    public void Boost_pump_positions_map_to_modes_without_flattening_auto(double raw, string mode, string isOn)
    {
        var pump = SynapticSystemMapper.Pump("left", "Left boost pump", raw, At);

        Assert.Equal(Enum.Parse<FuelPumpMode>(mode), pump.Mode.Value);
        Assert.Equal(At, pump.Mode.ObservedAt);
        switch (isOn)
        {
            case "unavailable":
                Assert.Equal(ValueState.Unavailable, pump.IsOn.State);
                break;
            default:
                Assert.Equal(bool.Parse(isOn), pump.IsOn.Value);
                break;
        }

        Assert.Equal(ValueState.Unavailable, pump.Fault.State);
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(-1.0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void An_unexpected_pump_value_is_unknown_in_both_views(double raw)
    {
        var pump = SynapticSystemMapper.Pump("right", "Right boost pump", raw, At);

        Assert.Equal(ValueState.Unknown, pump.Mode.State);
        Assert.Equal(ValueState.Unknown, pump.IsOn.State);
    }

    [Theory]
    [InlineData(0.0, "false")]
    [InlineData(2.0, "true")]
    [InlineData(1.0, "true")] // selector at RUN, observed live in BLOCK 10B.3
    [InlineData(0.5, "unknown")]
    [InlineData(3.0, "unknown")]
    public void The_apu_switch_only_says_off_or_not_off(double raw, string expected)
    {
        var value = SynapticSystemMapper.ApuSwitchOn(raw, At);

        if (expected == "unknown")
        {
            Assert.Equal(ValueState.Unknown, value.State);
        }
        else
        {
            Assert.Equal(bool.Parse(expected), value.Value);
        }
    }

    [Theory]
    [InlineData(0.0, "true")]
    [InlineData(1.0, "false")]
    [InlineData(0.5, "unknown")]
    public void The_apu_bleed_switch_is_a_selection_inverted_from_bleed_off(double raw, string expected)
    {
        var value = SynapticSystemMapper.BleedSelectedOn(raw, At);

        if (expected == "unknown")
        {
            Assert.Equal(ValueState.Unknown, value.State);
        }
        else
        {
            Assert.Equal(bool.Parse(expected), value.Value);
        }
    }

    [Fact]
    public void The_overlay_group_maps_every_variable_and_rejects_a_wrong_count()
    {
        var state = SynapticSystemMapper.Apply([1, 2, 2, 0, 1, 0], At);

        Assert.Equal(["left", "right"], state.FuelPumps.Select(p => p.Id));
        Assert.Equal(FuelPumpMode.Auto, state.FuelPumps[0].Mode.Value);
        Assert.Equal(FuelPumpMode.On, state.FuelPumps[1].Mode.Value);
        Assert.True(state.ApuMasterSwitchOn.Value);
        Assert.True(state.ApuBleedSelectedOn.Value);
        Assert.Equal([(1, true), (2, false)], state.EngineFirePushbuttons.Select(p => (p.Index, p.Pressed.Value)));
        Assert.Throws<ArgumentException>(() => SynapticSystemMapper.Apply([1, 2], At));
    }

    [Fact]
    public void The_overlay_reads_exactly_the_six_documented_variables()
    {
        Assert.Equal(
            ["L:A22X L Boost Pump", "L:A22X R Boost Pump", "L:A22X APU Switch", "L:A22X APU Bleed Off", "L:A22X L Eng Fire", "L:A22X R Eng Fire"],
            SynapticVariables.Systems.Select(v => v.Name));
        Assert.All(SynapticVariables.Systems, v => Assert.Equal("number", v.Unit));
        Assert.Equal(TimeSpan.FromSeconds(2), SynapticVariables.SystemsInterval);
    }

    // -- composition ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Composition_lays_the_overlay_over_the_masked_generic_snapshot()
    {
        var overlay = SynapticSystemMapper.Apply([1, 1, 2, 0, 1, 0], At);

        var t = SynapticTelemetryComposer.Compose(Generic(), overlay, aircraftReplaced: false, StaleAfter);

        Assert.All(t.FuelPumps, p => Assert.Equal(FuelPumpMode.Auto, p.Mode.Value));
        Assert.True(t.Apu.MasterSwitchOn.Value);
        Assert.True(t.Apu.BleedOn.Value);
        Assert.Equal(ValueState.Unavailable, t.Apu.Running.State);
        Assert.Equal(ValueState.Unavailable, t.Apu.Available.State);
        Assert.True(t.Engines[0].FireHandlePulled.Value);
        Assert.False(t.Engines[1].FireHandlePulled.Value);
        Assert.All(t.Engines, e =>
        {
            Assert.Equal(ValueState.Unavailable, e.FireDetected.State);
            Assert.Equal(ValueState.Unavailable, e.FireWarningLit.State);
        });
        Assert.Equal(ValueState.Unavailable, t.Engines[0].FuelFlowKilogramsPerHour.State);
        Assert.Equal(43.9, t.Engines[0].N1Percent.Value);
        Assert.Empty(t.InertialReferences);
        Assert.Empty(t.HydraulicSystems);
        Assert.Empty(t.Batteries);
    }

    [Fact]
    public void Without_an_overlay_reading_the_masked_generic_values_stay_unavailable()
    {
        var t = SynapticTelemetryComposer.Compose(Generic(), SynapticSystemState.Empty, aircraftReplaced: false, StaleAfter);

        Assert.Empty(t.FuelPumps);
        Assert.Equal(ValueState.Unavailable, t.Apu.BleedOn.State);
        Assert.Equal(ValueState.Unavailable, t.Engines[0].FireHandlePulled.State);
    }

    [Fact]
    public void Nothing_is_published_once_another_aircraft_is_loaded()
    {
        var t = SynapticTelemetryComposer.Compose(Generic(), SynapticSystemMapper.Apply([2, 2, 2, 0, 0, 0], At), aircraftReplaced: true, StaleAfter);

        Assert.Empty(t.FuelPumps);
        Assert.Empty(t.Engines);
        Assert.Equal(ValueState.Unavailable, t.Flight.OnGround.State);
    }

    [Fact]
    public void Overlay_values_turn_unknown_after_stale_after_like_generic_ones()
    {
        var generic = Generic() with { Timestamp = At + StaleAfter + TimeSpan.FromSeconds(1) };

        var t = SynapticTelemetryComposer.Compose(generic, SynapticSystemMapper.Apply([2, 2, 2, 0, 0, 0], At), aircraftReplaced: false, StaleAfter);

        Assert.Equal(ValueState.Unknown, t.FuelPumps[0].Mode.State);
        Assert.Equal(ValueState.Unknown, t.FuelPumps[0].IsOn.State);
        Assert.Equal(ValueState.Unknown, t.Apu.MasterSwitchOn.State);
        Assert.Equal(ValueState.Unknown, t.Engines[0].FireHandlePulled.State);
    }
}
