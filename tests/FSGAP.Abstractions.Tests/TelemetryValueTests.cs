using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

public class TelemetryValueTests
{
    [Fact]
    public void Default_value_is_unavailable_not_false()
    {
        TelemetryValue<bool> value = default;

        Assert.Equal(ValueState.Unavailable, value.State);
        Assert.False(value.IsKnown);
        Assert.False(value.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => value.Value);
    }

    [Fact]
    public void Known_false_is_distinct_from_unavailable_and_unknown()
    {
        TelemetryValue<bool> knownFalse = false;

        Assert.True(knownFalse.IsKnown);
        Assert.False(knownFalse.Value);
        Assert.NotEqual(TelemetryValue<bool>.Unavailable, knownFalse);
        Assert.NotEqual(TelemetryValue<bool>.Unknown, knownFalse);
        Assert.NotEqual(TelemetryValue<bool>.Unknown, TelemetryValue<bool>.Unavailable);
    }

    [Fact]
    public void Known_zero_is_readable()
    {
        var value = TelemetryValue<double>.Known(0d);

        Assert.True(value.TryGetValue(out var read));
        Assert.Equal(0d, read);
        Assert.Equal(TelemetryValue<double>.Known(0d), value);
    }

    [Fact]
    public void Unknown_value_uses_fallback()
    {
        var value = TelemetryValue<double>.Unknown;

        Assert.Equal(ValueState.Unknown, value.State);
        Assert.Equal(-1d, value.GetValueOrDefault(-1d));
        Assert.Equal("Unknown", value.ToString());
    }
}
