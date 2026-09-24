using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Resolution;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Fenix.Tests;

// Descriptors below are synthetic test inputs, modelled on the formats observed live.
public class FenixAircraftProviderTests
{
    private readonly FenixAircraftProvider _provider = new();

    public static TheoryData<string, string> FenixTitles => new()
    {
        { "FenixA319", "A319" },
        { "FenixA320", "A320" },
        { "FenixA321", "A321" },
        { "Fenix A319", "A319" },
        { "Fenix A320", "A320" },
        { "Fenix A321", "A321" },
        { "FenixA320 CFM", "A320" },
        { "FenixA320 CFM WF", "A320" },
        { "FenixA321 IAE WF SC", "A321" },
        { "FenixA319 IAE SL SD", "A319" },
        { "fenix-a320 air test", "A320" },
    };

    public static TheoryData<AircraftDescriptor> OtherAircraft => new()
    {
        new AircraftDescriptor { Title = "PMDG 737-800 Test Livery", Manufacturer = "Boeing", Model = "737-800", IcaoType = "B738" },
        new AircraftDescriptor { Title = "iniBuilds A320neo V2 Test Livery", IcaoType = "A20N" },
        new AircraftDescriptor { Title = "Airbus A320neo Asobo", IcaoType = "A20N" },
        new AircraftDescriptor { Title = "Airbus A320", IcaoType = "A320", LiveryFolder = "a320-generic" },
        new AircraftDescriptor { Title = "FlyByWire A320neo Test Livery", IcaoType = "A20N" },
        new AircraftDescriptor { Title = "Cessna Skyhawk C172 Test Livery", Manufacturer = "Cessna", IcaoType = "C172" },
        new AircraftDescriptor { Title = "Fenix 3200 test", LiveryFolder = "unrelated" },
        new AircraftDescriptor(),
    };

    [Theory]
    [MemberData(nameof(FenixTitles))]
    public void Fenix_titles_are_recognized(string title, string expectedModel)
    {
        var match = _provider.Match(new AircraftDescriptor { Title = title });

        Assert.True(match.IsSupported);
        Assert.True(((IAircraftProvider)_provider).CanHandle(new AircraftDescriptor { Title = title }));
        Assert.Equal(MatchSpecificity.Dedicated, match.Specificity);
        Assert.Equal(expectedModel, match.Identity.Model);
    }

    [Theory]
    [MemberData(nameof(OtherAircraft))]
    public void Other_aircraft_are_never_matched_even_when_they_say_A320(AircraftDescriptor aircraft)
    {
        Assert.False(_provider.Match(aircraft).IsSupported);
    }

    [Fact]
    public void Livery_folder_is_the_fallback_when_the_title_is_not_conclusive()
    {
        var match = _provider.Match(new AircraftDescriptor { Title = "Custom Airliner", LiveryFolder = "Fenix_A321_house" });

        Assert.True(match.IsSupported);
        Assert.Equal("A321", match.Identity.Model);
    }

    [Fact]
    public void Title_wins_over_livery_folder_for_the_model()
    {
        var match = _provider.Match(new AircraftDescriptor { Title = "FenixA319 CFM SL SD", LiveryFolder = "Fenix_A321_house" });

        Assert.Equal("A319", match.Identity!.Model);
    }

    [Fact]
    public void Identity_is_normalized()
    {
        var identity = _provider.Match(new AircraftDescriptor { Title = "FenixA321 IAE WF SC", Registration = " sx-dnh ", Livery = "Test Livery" }).Identity!;

        Assert.Equal("Fenix Simulations", identity.Developer);
        Assert.Equal("Airbus", identity.Manufacturer);
        Assert.Equal("A320", identity.Family);
        Assert.Equal("A321", identity.Model);
        Assert.Equal("A321", identity.IcaoType); // inferred from the recognized model
        Assert.Null(identity.Variant); // the sub-series is not known
        Assert.Equal("IAE", identity.EngineVariant);
        Assert.Equal("WingtipFence", identity.WingtipConfiguration);
        Assert.Equal("SX-DNH", identity.Registration);
        Assert.Equal("Test Livery", identity.Livery);
        Assert.Null(identity.OperatorIcao);
    }

    [Theory]
    [InlineData("FenixA320 CFM WF", "CFM", "WingtipFence")]
    [InlineData("FenixA320 IAE SL", "IAE", "Sharklets")]
    [InlineData("FenixA320", null, null)] // unknown stays unknown, never defaulted to CFM
    [InlineData("FenixA320 CFM IAE WF SL", null, null)] // contradictory tokens: unknown, not a guess
    [InlineData("FenixA320 CFMX WFX", null, null)] // whole words only
    public void Engine_and_wingtip_come_from_exact_title_words(string title, string? engine, string? wingtip)
    {
        var identity = _provider.Match(new AircraftDescriptor { Title = title }).Identity!;

        Assert.Equal(engine, identity.EngineVariant);
        Assert.Equal(wingtip, identity.WingtipConfiguration);
    }

    [Fact]
    public async Task Session_has_a_real_identity_but_still_no_capability()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = new FenixAircraftProvider(timeProvider: clock);

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA321 IAE WF SC", Registration = "SX-DNH" });
        var snapshot = await session.Telemetry.GetSnapshotAsync();
        var engineFire = new FailureCommand(FailureKey.Parse("engine.fire"), FailureTarget.Engine(1));
        var trigger = await session.Failures.TriggerAsync(engineFire);

        Assert.Equal(FenixAircraftProvider.Id, session.ProviderId);
        Assert.Equal("A321", session.Identity.Model);
        Assert.Equal("SX-DNH", session.Identity.Registration);
        Assert.Same(AircraftCapabilities.None, session.Capabilities);
        Assert.Equal(clock.GetUtcNow(), snapshot.Timestamp);
        Assert.Equal(ValueState.Unavailable, snapshot.Apu.Running.State);
        Assert.Equal(FailureCommandStatus.NotSupported, trigger.Status);
        Assert.False(session.Capabilities.Failures.CanTrigger(engineFire));
        Assert.Empty(session.Capabilities.Failures.Catalog);
    }

    [Fact]
    public async Task Attaching_to_an_unsupported_aircraft_throws()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.AttachAsync(new AircraftDescriptor { Title = "PMDG 737-800" }));
    }

    [Fact]
    public void Registry_resolves_a_Fenix_aircraft_to_the_Fenix_provider()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(_provider);

        var fenix = registry.Resolve(new AircraftDescriptor { Title = "FenixA319 CFM SL SD" });
        var other = registry.Resolve(new AircraftDescriptor { Title = "iniBuilds A320neo" });

        Assert.True(fenix.IsResolved);
        Assert.Equal("fenix", fenix.Selected.Provider.ProviderId);
        Assert.Equal(ProviderResolutionStatus.NotSupported, other.Status);
    }
}
