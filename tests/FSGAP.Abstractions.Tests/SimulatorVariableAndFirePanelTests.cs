using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

/// <summary>BLOCK 6 contract additions: simulator variables and the neutral fire panel fields.</summary>
public class SimulatorVariableAndFirePanelTests
{
    [Fact]
    public void A_variable_keeps_its_name_and_unit_and_compares_by_value()
    {
        var variable = new SimulatorVariable("HYDRAULIC PRESSURE:1", "Psi");

        Assert.Equal("HYDRAULIC PRESSURE:1", variable.Name);
        Assert.Equal("Psi", variable.Unit);
        Assert.Equal(new SimulatorVariable("HYDRAULIC PRESSURE:1", "Psi"), variable);
    }

    [Theory]
    [InlineData(null, "number")]
    [InlineData("", "number")]
    [InlineData(" ", "number")]
    [InlineData("L:X", null)]
    [InlineData("L:X", "")]
    public void A_variable_needs_a_name_and_a_unit(string? name, string? unit)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SimulatorVariable(name!, unit!));
    }

    [Fact]
    public void Fire_panel_fields_default_to_unavailable_and_are_distinct_from_fire_detection()
    {
        var engine = new EngineTelemetry { Index = 1, FireWarningLit = TelemetryValue<bool>.Known(true, DateTimeOffset.UnixEpoch) };
        var apu = new ApuTelemetry();

        Assert.Equal(ValueState.Unavailable, engine.FireHandlePulled.State);
        Assert.True(engine.FireWarningLit.Value);
        Assert.Equal(ValueState.Unavailable, engine.FireDetected.State);
        Assert.Equal(ValueState.Unavailable, apu.FireHandlePulled.State);
    }
}
