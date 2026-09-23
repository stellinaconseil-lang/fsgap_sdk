using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Core.Resolution;

namespace FSGAP.Core.Tests;

public class AircraftProviderRegistryTests
{
    private static readonly AircraftDescriptor Airliner = new() { Title = "Test Airliner 300" };

    [Fact]
    public void Compatible_provider_is_selected()
    {
        var registry = new AircraftProviderRegistry();
        var airliner = new FakeAircraftProvider("airliner", "Airliner");
        registry.Register(new FakeAircraftProvider("glider", "Glider"));
        registry.Register(airliner);

        var resolution = registry.Resolve(Airliner);

        Assert.Equal(ProviderResolutionStatus.Resolved, resolution.Status);
        Assert.True(resolution.IsResolved);
        Assert.Same(airliner, resolution.Selected.Provider);
        Assert.Equal("Airliner", resolution.Selected.Match.Identity!.Model);
    }

    [Fact]
    public void Incompatible_provider_is_never_a_candidate()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new FakeAircraftProvider("glider", "Glider"));
        registry.Register(new FakeAircraftProvider("airliner", "Airliner"));

        var resolution = registry.Resolve(Airliner);

        Assert.DoesNotContain(resolution.Candidates, c => c.Provider.ProviderId == "glider");
    }

    [Fact]
    public void No_compatible_provider_resolves_to_not_supported()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new FakeAircraftProvider("glider", "Glider"));

        var resolution = registry.Resolve(Airliner);

        Assert.Equal(ProviderResolutionStatus.NotSupported, resolution.Status);
        Assert.False(resolution.IsResolved);
        Assert.Null(resolution.Selected);
        Assert.Empty(resolution.Candidates);
    }

    [Fact]
    public void Empty_registry_resolves_to_not_supported()
    {
        var resolution = new AircraftProviderRegistry().Resolve(Airliner);

        Assert.Equal(ProviderResolutionStatus.NotSupported, resolution.Status);
    }

    [Fact]
    public void Two_equally_specific_providers_are_ambiguous()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new FakeAircraftProvider("first", "Airliner"));
        registry.Register(new FakeAircraftProvider("second", "Airliner"));

        var resolution = registry.Resolve(Airliner);

        Assert.Equal(ProviderResolutionStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.Selected);
        Assert.Equal(["first", "second"], resolution.Candidates.Select(c => c.Provider.ProviderId));
    }

    [Fact]
    public void Dedicated_provider_wins_over_generic_one_whatever_the_registration_order()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new FakeAircraftProvider("generic", "", MatchSpecificity.Generic));
        registry.Register(new FakeAircraftProvider("dedicated", "Airliner"));

        var resolution = registry.Resolve(Airliner);

        Assert.True(resolution.IsResolved);
        Assert.Equal("dedicated", resolution.Selected.Provider.ProviderId);
        Assert.Equal(["dedicated", "generic"], resolution.Candidates.Select(c => c.Provider.ProviderId));
    }

    [Fact]
    public void Duplicate_provider_id_is_rejected_case_insensitively()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new FakeAircraftProvider("fenix", "A"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeAircraftProvider("FENIX", "B")));
        Assert.Single(registry.Providers);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_provider_id_is_rejected(string providerId)
    {
        var registry = new AircraftProviderRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(new FakeAircraftProvider(providerId, "A")));
    }
}
