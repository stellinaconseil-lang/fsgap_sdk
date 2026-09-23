using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Resolution;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Fenix.Tests;

// Descriptors below are synthetic test inputs, not strings captured from the simulator.
public class FenixAircraftProviderTests
{
    private readonly FenixAircraftProvider _provider = new();

    public static TheoryData<AircraftDescriptor, string> FenixAircraft => new()
    {
        { new AircraftDescriptor { Title = "Fenix A319 Test Livery", IcaoType = "A319" }, "A319" },
        { new AircraftDescriptor { Title = "Fenix A320 Test Livery", IcaoType = "A320" }, "A320" },
        { new AircraftDescriptor { Title = "Fenix A321 Test Livery", IcaoType = "A321" }, "A321" },
        { new AircraftDescriptor { Title = "FenixA320 Test Livery" }, "A320" },
        { new AircraftDescriptor { Title = "Test Livery", PackagePath = "fenix-test-package", Model = "A321" }, "A321" },
    };

    public static TheoryData<AircraftDescriptor> OtherAircraft => new()
    {
        new AircraftDescriptor { Title = "PMDG 737-800 Test Livery", Manufacturer = "Boeing", Model = "737-800", IcaoType = "B738" },
        new AircraftDescriptor { Title = "Cessna Skyhawk C172 Test Livery", Manufacturer = "Cessna", IcaoType = "C172" },
        new AircraftDescriptor { Title = "Other Developer A320 Test Livery", Manufacturer = "Airbus", IcaoType = "A320" },
        new AircraftDescriptor { Title = "Fenix Test Aircraft", IcaoType = "A3200" },
        new AircraftDescriptor(),
    };

    [Theory]
    [MemberData(nameof(FenixAircraft))]
    public void Fenix_A320_family_is_supported(AircraftDescriptor aircraft, string expectedModel)
    {
        var match = _provider.Match(aircraft);

        Assert.True(match.IsSupported);
        Assert.True(((IAircraftProvider)_provider).CanHandle(aircraft));
        Assert.Equal(MatchSpecificity.Dedicated, match.Specificity);
        Assert.Equal("Fenix Simulations", match.Identity.Developer);
        Assert.Equal("Airbus", match.Identity.Manufacturer);
        Assert.Equal("A320", match.Identity.Family);
        Assert.Equal(expectedModel, match.Identity.Model);
        Assert.Equal(expectedModel, match.Identity.IcaoType);
    }

    [Theory]
    [MemberData(nameof(OtherAircraft))]
    public void Other_aircraft_are_not_supported(AircraftDescriptor aircraft)
    {
        Assert.False(_provider.Match(aircraft).IsSupported);
    }

    [Fact]
    public void Identity_keeps_registration_and_livery_and_does_not_guess_the_rest()
    {
        var aircraft = new AircraftDescriptor { Title = "Fenix A320", Registration = "F-TEST", Livery = "Test Livery" };

        var identity = _provider.Match(aircraft).Identity!;

        Assert.Equal("F-TEST", identity.Registration);
        Assert.Equal("Test Livery", identity.Livery);
        Assert.Null(identity.Variant);
        Assert.Null(identity.EngineVariant);
    }

    [Fact]
    public async Task Session_declares_no_capability_yet()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = new FenixAircraftProvider(clock);

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "Fenix A321" });
        var snapshot = await session.Telemetry.GetSnapshotAsync();
        var engineFire = new FailureCommand(FailureKey.Parse("engine.fire"), FailureTarget.Engine(1));
        var trigger = await session.Failures.TriggerAsync(engineFire);

        Assert.Equal(FenixAircraftProvider.Id, session.ProviderId);
        Assert.Equal("A321", session.Identity.Model);
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

        var fenix = registry.Resolve(new AircraftDescriptor { Title = "Fenix A319" });
        var cessna = registry.Resolve(new AircraftDescriptor { Title = "Cessna C172" });

        Assert.True(fenix.IsResolved);
        Assert.Equal("fenix", fenix.Selected.Provider.ProviderId);
        Assert.Equal(ProviderResolutionStatus.NotSupported, cessna.Status);
    }
}
