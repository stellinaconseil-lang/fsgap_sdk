using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Tests;

public class FailureKeyTests
{
    [Theory]
    [InlineData("engine.fire")]
    [InlineData("navigation.adf.1")]
    [InlineData("hydraulic.pump.engine-driven")]
    [InlineData("a.b")]
    [InlineData("a1.b2.c3.d4.e5.f6.g7.h8")]
    public void Valid_keys_parse(string text)
    {
        var key = FailureKey.Parse(text);

        Assert.Equal(text, key.Value);
        Assert.Equal(text, key.ToString());
    }

    [Theory]
    [InlineData("F_FIRE_FDU1")] // Fenix-style id
    [InlineData("123")] // numeric vendor id
    [InlineData("487")]
    [InlineData("FENIX_FAILURE_ID_1234")]
    [InlineData("fire")] // a single segment is a category, not a failure
    [InlineData("Engine.Fire")] // upper case
    [InlineData("engine_fire.left")] // underscore
    [InlineData("engine..fire")]
    [InlineData(".engine.fire")]
    [InlineData("engine.fire.")]
    [InlineData("engine.-fire")]
    [InlineData("engine.fire-")]
    [InlineData("1engine.fire")]
    [InlineData("engine fire.x")]
    [InlineData("a.b.c.d.e.f.g.h.i")] // nine segments
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_keys_are_rejected(string text)
    {
        Assert.False(FailureKey.TryParse(text, out var key));
        Assert.Null(key);
        Assert.Throws<FormatException>(() => FailureKey.Parse(text));
    }

    [Fact]
    public void Null_and_oversized_keys_are_rejected()
    {
        Assert.False(FailureKey.TryParse(null, out _));
        Assert.False(FailureKey.TryParse("a." + new string('b', FailureKey.MaxLength), out _));
    }

    [Fact]
    public void Keys_have_value_equality()
    {
        var first = FailureKey.Parse("engine.fire");
        var second = FailureKey.Parse("engine.fire");
        var other = FailureKey.Parse("engine.failure");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.True(first != other);
    }

    [Fact]
    public void Keys_work_as_dictionary_keys()
    {
        var counts = new Dictionary<FailureKey, int> { [FailureKey.Parse("engine.fire")] = 1 };

        counts[FailureKey.Parse("engine.fire")]++;

        Assert.Equal(2, Assert.Single(counts).Value);
    }
}
