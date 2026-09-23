using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

public class TelemetryValueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_value_is_unavailable_not_false()
    {
        TelemetryValue<bool> value = default;

        Assert.Equal(ValueState.Unavailable, value.State);
        Assert.False(value.IsKnown);
        Assert.Null(value.ObservedAt);
        Assert.False(value.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => value.Value);
    }

    [Fact]
    public void Known_false_is_distinct_from_unavailable_and_unknown()
    {
        var knownFalse = TelemetryValue<bool>.Known(false, T0);

        Assert.True(knownFalse.IsKnown);
        Assert.False(knownFalse.Value);
        Assert.NotEqual(TelemetryValue<bool>.Unavailable, knownFalse);
        Assert.NotEqual(TelemetryValue<bool>.Unknown, knownFalse);
        Assert.NotEqual(TelemetryValue<bool>.Unknown, TelemetryValue<bool>.Unavailable);
    }

    [Fact]
    public void Known_zero_is_readable_and_carries_its_observation_time()
    {
        var value = TelemetryValue<double>.Known(0d, T0);

        Assert.True(value.TryGetValue(out var read));
        Assert.Equal(0d, read);
        Assert.Equal(T0, value.ObservedAt);
        Assert.Equal(TelemetryValue<double>.Known(0d, T0), value);
    }

    [Fact]
    public void Unknown_value_uses_fallback()
    {
        var value = TelemetryValue<double>.Unknown;

        Assert.Equal(ValueState.Unknown, value.State);
        Assert.Null(value.ObservedAt);
        Assert.Equal(-1d, value.GetValueOrDefault(-1d));
        Assert.Equal("Unknown", value.ToString());
    }

    [Fact]
    public void Age_is_measured_from_the_observation()
    {
        var value = TelemetryValue<double>.Known(250d, T0);

        Assert.Equal(TimeSpan.FromSeconds(3), value.GetAge(T0.AddSeconds(3)));
        Assert.Null(TelemetryValue<double>.Unavailable.GetAge(T0));
    }

    [Fact]
    public void Fresh_known_value_stays_known()
    {
        var value = TelemetryValue<double>.Known(250d, T0);

        var checkedValue = value.ExpireIfOlderThan(T0.AddSeconds(15), TimeSpan.FromSeconds(15));

        Assert.Equal(value, checkedValue);
        Assert.Equal(250d, checkedValue.Value);
    }

    [Fact]
    public void Stale_known_value_becomes_unknown_but_keeps_its_age()
    {
        var value = TelemetryValue<double>.Known(250d, T0);
        var now = T0.AddSeconds(16);

        var expired = value.ExpireIfOlderThan(now, TimeSpan.FromSeconds(15));

        Assert.Equal(ValueState.Unknown, expired.State);
        Assert.False(expired.IsKnown);
        Assert.Throws<InvalidOperationException>(() => expired.Value);
        Assert.Equal(T0, expired.ObservedAt);
        Assert.Equal(TimeSpan.FromSeconds(16), expired.GetAge(now));
    }

    [Fact]
    public void Expiry_never_turns_unavailable_or_unknown_into_something_else()
    {
        var later = T0.AddHours(1);

        Assert.Equal(TelemetryValue<bool>.Unavailable, TelemetryValue<bool>.Unavailable.ExpireIfOlderThan(later, TimeSpan.Zero));
        Assert.Equal(TelemetryValue<bool>.Unknown, TelemetryValue<bool>.Unknown.ExpireIfOlderThan(later, TimeSpan.Zero));
    }

    [Fact]
    public void Negative_max_age_is_rejected()
    {
        var value = TelemetryValue<bool>.Known(true, T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => value.ExpireIfOlderThan(T0, TimeSpan.FromSeconds(-1)));
    }
}
