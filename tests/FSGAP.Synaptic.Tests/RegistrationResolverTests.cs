using FSGAP.Abstractions.Aircraft;
using FSGAP.Synaptic.Identity;

namespace FSGAP.Synaptic.Tests;

/// <summary>Conservative registration resolution: folder parsing and source precedence.</summary>
public class RegistrationResolverTests
{
    [Theory]
    [InlineData("AIR FRANCE F-HZUF", "F-HZUF")]
    [InlineData("AIR BALTIC YL-CSM", "YL-CSM")]
    [InlineData("DELTA N324DU", "N324DU")]
    [InlineData("BREEZE AIRWAYS N214BZ", "N214BZ")]
    [InlineData("ITA AIRWAYS EI-HHU", "EI-HHU")]
    [InlineData("JETBLUE N3115J", "N3115J")]
    [InlineData("KOREAN AIR HL8315", "HL8315")]
    [InlineData("SWISS HB-JCO", "HB-JCO")]
    [InlineData("AIR CANADA G-GUAC", "G-GUAC")] // reported as written, never "corrected"
    [InlineData("air france f-hzuf", "F-HZUF")]
    public void Live_folders_give_their_registration_token(string folder, string expected)
    {
        Assert.Equal(expected, RegistrationResolver.ParseFolder(folder));
    }

    [Theory]
    [InlineData("A_BCS3_SYN_HOUSE")] // Synaptic House: no registration
    [InlineData("WHITE")]
    [InlineData("ACME QQ-ABC")] // unknown nationality prefix
    [InlineData("TWO F-HZUF F-HZUG")] // two candidates
    [InlineData("A220-300")]
    [InlineData("")]
    [InlineData(null)]
    public void Folders_without_one_strict_registration_give_none(string? folder)
    {
        Assert.Null(RegistrationResolver.ParseFolder(folder));
    }

    [Fact]
    public void An_authoritative_registration_wins()
    {
        var resolved = RegistrationResolver.Resolve("f-hzuf", "AIR FRANCE F-HZUG", "C-FFCO", null);

        Assert.Equal(new ResolvedRegistration("F-HZUF", RegistrationSource.Authoritative), resolved);
    }

    [Fact]
    public void An_atc_id_corroborated_by_the_folder_is_observed()
    {
        Assert.Equal(
            new ResolvedRegistration("F-HZUF", RegistrationSource.Observed),
            RegistrationResolver.Resolve(null, "AIR FRANCE F-HZUF", "FHZUF", null));
    }

    [Fact]
    public void A_stale_atc_id_contradicted_by_the_folder_is_ignored()
    {
        // BLOCK 10A-LIVE: C-FFCO reported on the Delta livery.
        Assert.Equal(
            new ResolvedRegistration("N324DU", RegistrationSource.Derived),
            RegistrationResolver.Resolve(null, "DELTA N324DU", "C-FFCO", null));
    }

    [Fact]
    public void An_uncorroborated_atc_id_is_never_used_even_without_a_folder_registration()
    {
        Assert.Null(RegistrationResolver.Resolve(null, "A_BCS3_SYN_HOUSE", "C-FFCO", null));
        Assert.Null(RegistrationResolver.Resolve(null, null, "C-FFCO", null));
    }

    [Fact]
    public void A_cached_result_is_used_only_without_current_evidence_and_keeps_its_source()
    {
        var cached = new ResolvedRegistration("HB-JCO", RegistrationSource.Observed);

        Assert.Equal(cached, RegistrationResolver.Resolve(null, null, null, cached));
        Assert.Equal(new ResolvedRegistration("HB-JCP", RegistrationSource.Derived), RegistrationResolver.Resolve(null, "SWISS HB-JCP", null, cached));
    }

    [Fact]
    public void Nothing_known_gives_no_registration()
    {
        Assert.Null(RegistrationResolver.Resolve(null, null, "   ", null));
    }
}
