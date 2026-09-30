using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

/// <summary>BLOCK 10B.1: the normalized fuel pump mode (off / auto / on) and its relation to the binary IsOn.</summary>
public class FuelPumpModeTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_mode_has_exactly_off_auto_on_and_no_unknown_member()
    {
        Assert.Equal(["Off", "Auto", "On"], Enum.GetNames<FuelPumpMode>());
        Assert.Equal([0, 1, 2], Enum.GetValues<FuelPumpMode>().Select(m => (int)m));
    }

    [Fact]
    public void A_pump_built_the_09_way_still_compiles_and_reports_mode_unavailable()
    {
        // Source compatibility: an initializer without Mode (every 0.9 provider) stays valid, and says nothing.
        var pump = new FuelPumpTelemetry { Id = "left-1", Name = "Left tank pump 1", IsOn = TelemetryValue<bool>.Known(true, At) };

        Assert.True(pump.IsOn.Value);
        Assert.Equal(ValueState.Unavailable, pump.Mode.State);
        Assert.Equal(ValueState.Unavailable, pump.Fault.State);
    }

    [Theory]
    [InlineData(FuelPumpMode.Off)]
    [InlineData(FuelPumpMode.Auto)]
    [InlineData(FuelPumpMode.On)]
    public void Every_mode_is_representable_as_a_known_value(FuelPumpMode mode)
    {
        var pump = new FuelPumpTelemetry { Id = "p", Name = "P", Mode = TelemetryValue<FuelPumpMode>.Known(mode, At) };

        Assert.Equal(mode, pump.Mode.Value);
        Assert.Equal(At, pump.Mode.ObservedAt);
    }

    [Fact]
    public void Auto_is_carried_by_mode_while_is_on_stays_unavailable()
    {
        // The contract never flattens AUTO into true or false: a consumer of IsOn only sees "cannot tell".
        var pump = new FuelPumpTelemetry { Id = "left", Name = "Left boost pump", Mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.Auto, At) };

        Assert.Equal(FuelPumpMode.Auto, pump.Mode.Value);
        Assert.Equal(ValueState.Unavailable, pump.IsOn.State);
        Assert.False(pump.IsOn.TryGetValue(out _));
    }

    [Fact]
    public void An_unreadable_mode_uses_the_telemetry_value_states()
    {
        var unknown = new FuelPumpTelemetry { Id = "p", Name = "P", Mode = TelemetryValue<FuelPumpMode>.Unknown };

        Assert.Equal(ValueState.Unknown, unknown.Mode.State);
        Assert.Throws<InvalidOperationException>(() => unknown.Mode.Value);
        Assert.Equal(FuelPumpMode.Off, unknown.Mode.GetValueOrDefault(FuelPumpMode.Off));
    }

    [Fact]
    public void Mode_takes_part_in_value_equality_and_with_expressions()
    {
        var on = new FuelPumpTelemetry { Id = "p", Name = "P", Mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.On, At) };
        var auto = on with { Mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.Auto, At) };

        Assert.NotEqual(on, auto);
        Assert.Equal(on, on with { });
        Assert.Equal(FuelPumpMode.On, on.Mode.Value);
    }

    [Fact]
    public void A_stale_mode_turns_unknown_and_keeps_its_observation_time()
    {
        var mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.Auto, At);

        var aged = mode.ExpireIfOlderThan(At.AddSeconds(16), TimeSpan.FromSeconds(15));

        Assert.Equal(ValueState.Unknown, aged.State);
        Assert.Equal(At, aged.ObservedAt);
    }
}
