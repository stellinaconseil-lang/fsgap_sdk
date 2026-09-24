using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Telemetry;
using FSGAP.Fenix.Variables;
using Idx = FSGAP.Fenix.Variables.FenixVariables.CockpitIndex;

namespace FSGAP.Fenix.Tests;

/// <summary>Raw Fenix readings to normalized sections (pure, no simulator).</summary>
public class FenixSystemMappingTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static double[] Cockpit(Action<double[]>? set = null)
    {
        var raw = new double[FenixVariables.Cockpit.Count];
        set?.Invoke(raw);
        return raw;
    }

    private static FenixSystemState ApplyCockpit(double[] raw) => FenixSystemMapper.ApplyCockpit(FenixSystemState.Empty, raw, At);

    [Theory]
    [InlineData(0.0, InertialReferenceMode.Off)]
    [InlineData(1.0, InertialReferenceMode.Navigation)]
    [InlineData(2.0, InertialReferenceMode.Attitude)]
    public void Ir_selector_positions_map_to_modes(double raw, InertialReferenceMode mode)
    {
        var value = FenixSystemMapper.IrMode(raw, At);

        Assert.Equal(mode, value.Value);
        Assert.Equal(At, value.ObservedAt);
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(-1.0)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    public void An_unproven_ir_raw_value_is_unknown_never_guessed(double raw)
    {
        Assert.Equal(ValueState.Unknown, FenixSystemMapper.IrMode(raw, At).State);
    }

    [Fact]
    public void All_three_irs_are_reported_as_a_collection_with_alignment_and_fault_unavailable()
    {
        var state = ApplyCockpit(Cockpit(r => { r[(int)Idx.Ir1Mode] = 1; r[(int)Idx.Ir2Mode] = 2; r[(int)Idx.Ir3Mode] = 0; }));

        Assert.Equal([1, 2, 3], state.InertialReferences.Select(i => i.Index));
        Assert.Equal(
            [InertialReferenceMode.Navigation, InertialReferenceMode.Attitude, InertialReferenceMode.Off],
            state.InertialReferences.Select(i => i.Mode.Value));
        Assert.All(state.InertialReferences, i =>
        {
            Assert.Equal(ValueState.Unavailable, i.Aligned.State);
            Assert.Equal(ValueState.Unavailable, i.Fault.State);
            Assert.Equal(At, i.Mode.ObservedAt);
        });
    }

    [Fact]
    public void Six_pumps_with_stable_ids_report_the_switch_position_and_no_fault()
    {
        var state = ApplyCockpit(Cockpit(r => { r[(int)Idx.FuelLeft1] = 1; r[(int)Idx.FuelCenter2] = 1; r[(int)Idx.FuelRight2] = 1; }));

        Assert.Equal(["left-1", "left-2", "center-1", "center-2", "right-1", "right-2"], state.FuelPumps.Select(p => p.Id));
        Assert.Equal([true, false, false, true, false, true], state.FuelPumps.Select(p => p.IsOn.Value));
        Assert.All(state.FuelPumps, p => Assert.Equal(ValueState.Unavailable, p.Fault.State));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    public void Two_state_switches_map_exactly(double raw, bool expected)
    {
        Assert.Equal(expected, FenixSystemMapper.Discrete(raw, At).Value);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(0.3)]
    [InlineData(double.NaN)]
    public void Any_other_switch_value_is_unknown(double raw)
    {
        Assert.Equal(ValueState.Unknown, FenixSystemMapper.Discrete(raw, At).State);
    }

    [Fact]
    public void Fire_handles_and_lights_map_per_engine_and_apu()
    {
        var state = ApplyCockpit(Cockpit(r => { r[(int)Idx.Eng1FireHandle] = 1; r[(int)Idx.Eng2FireLight] = 1; r[(int)Idx.ApuFireHandle] = 1; }));

        Assert.Equal([1, 2], state.EngineFirePanels.Select(p => p.Index));
        Assert.True(state.EngineFirePanels[0].HandlePulled.Value);
        Assert.False(state.EngineFirePanels[0].WarningLit.Value);
        Assert.False(state.EngineFirePanels[1].HandlePulled.Value);
        Assert.True(state.EngineFirePanels[1].WarningLit.Value);
        Assert.True(state.ApuFireHandlePulled.Value);
    }

    [Fact]
    public void A_lit_fire_warning_during_a_test_never_becomes_fire_detected()
    {
        // FIRE TEST pressed: both engine fire lights come on (proven live), and they are indistinguishable from a
        // real fire indication. The composed snapshot says "warning lit", never "fire detected".
        var state = ApplyCockpit(Cockpit(r => { r[(int)Idx.Eng1FireLight] = 1; r[(int)Idx.Eng2FireLight] = 1; }));

        var composed = FenixTelemetryComposer.Compose(AircraftTelemetry.Unavailable(At), state, aircraftReplaced: false, TimeSpan.FromSeconds(15));

        Assert.All(composed.Engines, e =>
        {
            Assert.True(e.FireWarningLit.Value);
            Assert.Equal(ValueState.Unavailable, e.FireDetected.State);
        });
        Assert.Equal(ValueState.Unavailable, composed.Apu.FireDetected.State);
        Assert.Empty(composed.FireZones);
    }

    [Fact]
    public void Hydraulics_report_green_and_blue_pressure_and_leave_yellow_unavailable()
    {
        var state = FenixSystemMapper.ApplyHydraulics(FenixSystemState.Empty, [2933.29, 2896.07], At);

        Assert.Equal(["green", "blue", "yellow"], state.HydraulicSystems.Select(h => h.Id));
        Assert.Equal(2933.29, state.HydraulicSystems[0].PressurePsi.Value);
        Assert.Equal(2896.07, state.HydraulicSystems[1].PressurePsi.Value);
        Assert.Equal(ValueState.Unavailable, state.HydraulicSystems[2].PressurePsi.State);
        Assert.All(state.HydraulicSystems, h => Assert.Equal(ValueState.Unavailable, h.Pressurized.State));
    }

    [Fact]
    public void A_group_read_replaces_only_its_own_sections()
    {
        var withHydraulics = FenixSystemMapper.ApplyHydraulics(FenixSystemState.Empty, [3000, 3000], At);

        var both = FenixSystemMapper.ApplyCockpit(withHydraulics, Cockpit(), At.AddSeconds(1));

        Assert.Same(withHydraulics.HydraulicSystems, both.HydraulicSystems);
        Assert.Equal(At.AddSeconds(1), both.InertialReferences[0].Mode.ObservedAt);
        Assert.Equal(At, both.HydraulicSystems[0].PressurePsi.ObservedAt);
    }

    [Fact]
    public void A_raw_list_of_the_wrong_length_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => FenixSystemMapper.ApplyCockpit(FenixSystemState.Empty, [1.0], At));
        Assert.Throws<ArgumentException>(() => FenixSystemMapper.ApplyHydraulics(FenixSystemState.Empty, [1.0, 2.0, 3.0], At));
    }

    [Fact]
    public void Variable_groups_are_fixed_distinct_and_read_only_names()
    {
        Assert.Equal(14, FenixVariables.Cockpit.Count);
        Assert.Equal(2, FenixVariables.Hydraulics.Count);
        Assert.Equal(Enum.GetValues<Idx>().Length, FenixVariables.Cockpit.Count);
        Assert.Equal(FenixVariables.Cockpit.Count, FenixVariables.Cockpit.Select(v => v.Name).Distinct().Count());
        Assert.All(FenixVariables.Cockpit, v => Assert.StartsWith("L:", v.Name, StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromSeconds(1), FenixVariables.CockpitInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), FenixVariables.HydraulicsInterval);
    }
}
