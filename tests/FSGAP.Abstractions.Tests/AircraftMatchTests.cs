using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Abstractions.Tests;

public class AircraftMatchTests
{
    [Fact]
    public void Not_supported_has_no_identity()
    {
        var match = AircraftMatch.NotSupported;

        Assert.False(match.IsSupported);
        Assert.Equal(MatchSpecificity.None, match.Specificity);
        Assert.Null(match.Identity);
    }

    [Fact]
    public void Supported_carries_identity_and_specificity()
    {
        var identity = new AircraftIdentity { Manufacturer = "Cessna", Model = "172" };

        var match = AircraftMatch.Supported(identity, MatchSpecificity.Generic);

        Assert.True(match.IsSupported);
        Assert.Equal(MatchSpecificity.Generic, match.Specificity);
        Assert.Same(identity, match.Identity);
    }

    [Theory]
    [InlineData(MatchSpecificity.None)]
    [InlineData((MatchSpecificity)42)]
    public void Supported_requires_a_real_specificity(MatchSpecificity specificity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AircraftMatch.Supported(new AircraftIdentity(), specificity));
    }
}
