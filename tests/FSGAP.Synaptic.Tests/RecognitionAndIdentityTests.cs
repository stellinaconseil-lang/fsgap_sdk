using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Synaptic.Detection;
using FSGAP.Synaptic.Identity;

namespace FSGAP.Synaptic.Tests;

/// <summary>Recognition of the Synaptic A220-300 and its normalized identity.</summary>
public class RecognitionAndIdentityTests
{
    private static readonly SynapticAircraftProvider Provider = new();

    public static TheoryData<string> LiveDescriptors() => new() { "delta", "air-baltic", "air-france" };

    private static AircraftDescriptor Live(string name) => name switch
    {
        "delta" => Descriptors.Delta,
        "air-baltic" => Descriptors.AirBaltic,
        _ => Descriptors.AirFrance,
    };

    [Theory]
    [MemberData(nameof(LiveDescriptors))]
    public void The_three_live_descriptors_are_recognized_as_a_dedicated_match(string name)
    {
        var match = Provider.Match(Live(name));

        Assert.True(match.IsSupported);
        Assert.Equal(MatchSpecificity.Dedicated, match.Specificity);
    }

    public static TheoryData<string, AircraftDescriptor> LookAlikes() => new()
    {
        { "Asobo passive A220-100", new AircraftDescriptor { Title = "Asobo PassiveAircraft A220-100", Model = "A220-100", Manufacturer = "Airbus" } },
        { "Asobo passive A220-300", new AircraftDescriptor { Title = "Asobo PassiveAircraft A220-300", Model = "A220-300", Manufacturer = "223" } },
        { "FSLTL AI A220", new AircraftDescriptor { Title = "FSLTL A220-300 Air France", Model = "A220-300", Manufacturer = "223" } },
        { "arbitrary A220 text", new AircraftDescriptor { Title = "My A220-300 repaint", Model = "A220-300", Manufacturer = "223" } },
        { "exact title, no ATC strings", new AircraftDescriptor { Title = "A220-300" } },
        { "exact title, wrong ATC type", new AircraftDescriptor { Title = "A220-300", Model = "A220-300", Manufacturer = "Airbus" } },
        { "exact title, wrong ATC model", new AircraftDescriptor { Title = "A220-300 - No Cabin", Model = "A220-100", Manufacturer = "223" } },
        { "Fenix A319", Descriptors.FenixA319 },
        { "Fenix A320", Descriptors.FenixA320 },
        { "Fenix A321", Descriptors.FenixA321 },
        { "iniBuilds A380", Descriptors.IniBuildsA380 },
        { "Cessna", Descriptors.Cessna },
    };

    [Theory]
    [MemberData(nameof(LookAlikes))]
    public void Look_alikes_are_not_the_synaptic_a220(string why, AircraftDescriptor aircraft)
    {
        Assert.False(Provider.Match(aircraft).IsSupported, why);
    }

    [Fact]
    public async Task Attaching_an_unrecognized_aircraft_is_refused()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Provider.AttachAsync(Descriptors.FenixA320));
    }

    [Fact]
    public void Titles_are_matched_exactly_ignoring_case_and_surrounding_spaces()
    {
        Assert.True(SynapticA220Recognizer.IsPresetTitle("  a220-300 - no cabin "));
        Assert.False(SynapticA220Recognizer.IsPresetTitle("A220-300 - No Cabin (custom)"));
    }

    [Fact]
    public void The_identity_is_the_normalized_a220_300_never_the_simulator_atc_type()
    {
        var identity = Provider.Match(Descriptors.AirFrance).Identity!;

        Assert.Equal("Synaptic Simulations", identity.Developer);
        Assert.Equal("Airbus", identity.Manufacturer);
        Assert.Equal("A220", identity.Family);
        Assert.Equal("A220-300", identity.Model);
        Assert.Equal("BCS3", identity.IcaoType);
        Assert.NotEqual("223", identity.IcaoType);
        Assert.Equal("PW1500G", identity.EngineVariant);
        Assert.Null(identity.Variant);
        Assert.Null(identity.WingtipConfiguration);
        Assert.Null(identity.OperatorIcao);
        Assert.Equal("Air France A220-300", identity.Livery);
    }

    [Fact]
    public void The_match_registration_comes_from_the_folder_and_ignores_a_stale_atc_id()
    {
        var identity = Provider.Match(Descriptors.Delta).Identity!;

        Assert.Equal("N324DU", identity.Registration);
        Assert.Equal(RegistrationSource.Derived, identity.RegistrationSource);
    }

    [Fact]
    public void A_livery_without_a_registration_has_no_registration_source()
    {
        var identity = SynapticIdentity.Create(null, RegistrationSource.Derived, "Synaptic House A220-300");

        Assert.Null(identity.Registration);
        Assert.Null(identity.RegistrationSource);
    }
}
