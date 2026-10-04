using System.Reflection;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Cockpit;

namespace FSGAP.Abstractions.Tests;

/// <summary>
/// The normalized cockpit observation contract: keys are vendor-neutral dotted identifiers, values are a boolean or an
/// integer with a known/unknown/unavailable state, and no raw vendor variable name is anywhere in the public surface.
/// </summary>
public class CockpitObservationContractTests
{
    [Theory]
    [InlineData("fire-test.engine-1")]
    [InlineData("adirs.ir-1.mode")]
    [InlineData("fuel-pump.left-1")]
    [InlineData("master-warning.first-officer.light-l")]
    [InlineData("a.b.c.d.e.f.g.h")] // eight segments, the maximum
    public void Valid_keys_parse(string value)
    {
        Assert.True(CockpitObservationKey.TryParse(value, out var key));
        Assert.Equal(value, key.Value);
        Assert.Equal(value, CockpitObservationKey.Parse(value).ToString());
    }

    [Theory]
    [InlineData("L:S_OH_FIRE_ENG1_TEST")] // a vendor L:Var is deliberately rejected
    [InlineData("Fire-Test.Engine-1")] // upper-case
    [InlineData("firetest")] // single segment
    [InlineData("a.b.c.d.e.f.g.h.i")] // nine segments, one too many
    [InlineData("fire..engine")] // empty segment
    [InlineData("fire-.engine")] // trailing hyphen in a segment
    [InlineData("1fire.engine")] // first segment starts with a digit
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_keys_are_rejected(string? value)
    {
        Assert.False(CockpitObservationKey.TryParse(value, out _));
        Assert.Throws<FormatException>(() => CockpitObservationKey.Parse(value!));
    }

    [Fact]
    public void A_key_longer_than_the_maximum_is_rejected()
    {
        var tooLong = "a." + string.Join('.', Enumerable.Repeat("segment", 40));

        Assert.True(tooLong.Length > CockpitObservationKey.MaxLength);
        Assert.False(CockpitObservationKey.TryParse(tooLong, out _));
    }

    [Fact]
    public void Keys_compare_by_value()
    {
        Assert.Equal(CockpitObservationKey.Parse("fuel-pump.left-1"), CockpitObservationKey.Parse("fuel-pump.left-1"));
        Assert.NotEqual(CockpitObservationKey.Parse("fuel-pump.left-1"), CockpitObservationKey.Parse("fuel-pump.left-2"));
    }

    [Fact]
    public void A_boolean_value_round_trips_and_refuses_the_integer_accessor()
    {
        var on = CockpitObservationValue.Boolean(true);

        Assert.Equal(CockpitObservationValueKind.Boolean, on.Kind);
        Assert.Equal(CockpitObservationState.Known, on.State);
        Assert.True(on.IsKnown);
        Assert.True(on.TryGetBoolean(out var b) && b);
        Assert.False(on.TryGetInteger(out _));
    }

    [Fact]
    public void An_integer_value_round_trips_and_refuses_the_boolean_accessor()
    {
        var mode = CockpitObservationValue.Integer(2);

        Assert.Equal(CockpitObservationValueKind.Integer, mode.Kind);
        Assert.True(mode.IsKnown);
        Assert.True(mode.TryGetInteger(out var v) && v == 2);
        Assert.False(mode.TryGetBoolean(out _));
    }

    [Fact]
    public void Unknown_and_unavailable_values_keep_their_kind_but_yield_nothing()
    {
        var unknown = CockpitObservationValue.Unknown(CockpitObservationValueKind.Integer);
        var unavailable = CockpitObservationValue.Unavailable(CockpitObservationValueKind.Boolean);

        Assert.Equal(CockpitObservationState.Unknown, unknown.State);
        Assert.False(unknown.IsKnown);
        Assert.False(unknown.TryGetInteger(out _));

        Assert.Equal(CockpitObservationState.Unavailable, unavailable.State);
        Assert.False(unavailable.IsKnown);
        Assert.False(unavailable.TryGetBoolean(out _));
    }

    [Fact]
    public void Default_value_is_an_unavailable_boolean()
    {
        CockpitObservationValue value = default;

        Assert.Equal(CockpitObservationValueKind.Boolean, value.Kind);
        Assert.Equal(CockpitObservationState.Unavailable, value.State);
        Assert.False(value.IsKnown);
    }

    [Fact]
    public void No_cockpit_observation_capability_supports_nothing()
    {
        var none = CockpitObservationCapabilities.None;

        Assert.False(none.CanObserve);
        Assert.Empty(none.Keys);
        Assert.False(none.Supports(CockpitObservationKey.Parse("fire-test.engine-1")));
    }

    [Fact]
    public void A_populated_capability_answers_per_key()
    {
        var supported = CockpitObservationKey.Parse("fire-test.engine-1");
        var capability = new CockpitObservationCapabilities { CanObserve = true, Keys = [supported] };

        Assert.True(capability.CanObserve);
        Assert.True(capability.Supports(supported));
        Assert.False(capability.Supports(CockpitObservationKey.Parse("fire-test.apu")));
    }

    [Fact]
    public void A_snapshot_returns_unavailable_for_a_key_it_does_not_carry()
    {
        var key = CockpitObservationKey.Parse("fire-test.engine-1");
        var snapshot = new CockpitObservationSnapshot
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Values = new Dictionary<CockpitObservationKey, CockpitObservationValue> { [key] = CockpitObservationValue.Boolean(true) },
        };

        Assert.True(snapshot.Get(key).TryGetBoolean(out var on) && on);
        Assert.Equal(CockpitObservationState.Unavailable, snapshot.Get(CockpitObservationKey.Parse("fire-test.apu")).State);
    }

    [Fact]
    public void An_empty_snapshot_carries_only_its_timestamp()
    {
        var snapshot = CockpitObservationSnapshot.Empty(DateTimeOffset.UnixEpoch);

        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.Timestamp);
        Assert.Empty(snapshot.Values);
    }

    [Fact]
    public void The_abstractions_assembly_carries_no_raw_vendor_variable_name()
    {
        // The dotted keys are the ONLY cockpit vocabulary that crosses the public boundary; the vendor L:Var names
        // live inside the provider packages. No "L:" prefixed variable string may be embedded here.
        var assembly = typeof(CockpitObservationKey).Assembly;

        Assert.False(BinaryContains(assembly, "L:S_OH"));
        Assert.False(BinaryContains(assembly, "L:I_"));
        Assert.False(BinaryContains(assembly, "L:S_MIP"));
    }

    private static bool BinaryContains(Assembly assembly, string text)
    {
        var bytes = File.ReadAllBytes(assembly.Location);
        return bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(text)) >= 0
            || bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(text)) >= 0;
    }
}
