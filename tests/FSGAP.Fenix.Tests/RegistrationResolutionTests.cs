using FSGAP.Abstractions.Aircraft;
using FSGAP.Fenix.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace FSGAP.Fenix.Tests;

public class RegistrationResolutionTests
{
    [Fact]
    public async Task Live_case_resolves_the_A321_from_its_installed_livery()
    {
        // Reproduces the live BLOCK 3 observation: MSFS reports the folder upper-case, the disk has it lower-case.
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-321-liveries");
        fixture.Livery(package, "aee-sx-dnh-7f2f", FenixInstallFixture.LiveryCfg(
            "A321,IAE,WF,Cabin_A321_SingleClass", "Aegean Test Livery SX-DNH", "SX-DNH", "AEE"));
        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();
        var provider = new FenixAircraftProvider(catalog);

        await using var session = await provider.AttachAsync(new AircraftDescriptor
        {
            Title = "FenixA321 IAE WF SC",
            Registration = "SX-DNH",
            LiveryFolder = "AEE-SX-DNH-7F2F",
            Livery = "Aegean Test Livery SX-DNH",
        });

        var identity = session.Identity;
        Assert.Equal("A321", identity.Model);
        Assert.Equal("IAE", identity.EngineVariant);
        Assert.Equal("WingtipFence", identity.WingtipConfiguration);
        Assert.Equal("SX-DNH", identity.Registration);
        Assert.Equal("AEE", identity.OperatorIcao);
        Assert.Equal("Aegean Test Livery SX-DNH", identity.Livery);
    }

    [Fact]
    public async Task Livery_folder_resolves_the_registration_when_the_atc_id_is_empty()
    {
        var provider = new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("aca-c-ftst-0001", "C-FTST")));

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320 CFM WF", LiveryFolder = "ACA-C-FTST-0001" });

        Assert.Equal("C-FTST", session.Identity.Registration);
    }

    [Fact]
    public async Task Installed_livery_registration_wins_over_the_atc_id()
    {
        var provider = new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("afr-f-htst", "F-HTST")));

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320", Registration = "F-GENERIC", LiveryFolder = "afr-f-htst" });

        Assert.Equal("F-HTST", session.Identity.Registration);
    }

    [Fact]
    public async Task Atc_id_is_used_when_the_catalog_does_not_resolve()
    {
        var provider = new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("other-folder", "F-OTHR")));

        await using var unknownFolder = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320", Registration = " f-gkxy ", LiveryFolder = "not-installed" });
        await using var noRegistrationInLivery = await new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("no-reg", null)))
            .AttachAsync(new AircraftDescriptor { Title = "FenixA320", Registration = "F-GKXY", LiveryFolder = "no-reg" });

        Assert.Equal("F-GKXY", unknownFolder.Identity.Registration);
        Assert.Equal("F-GKXY", noRegistrationInLivery.Identity.Registration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Registration_is_unknown_when_neither_source_has_one(string? atcId)
    {
        var provider = new FenixAircraftProvider(new InMemoryCatalog());

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320", Registration = atcId, LiveryFolder = "unknown-folder" });

        Assert.Null(session.Identity.Registration);
    }

    [Fact]
    public async Task Without_a_catalog_the_atc_id_is_the_only_source()
    {
        await using var session = await new FenixAircraftProvider().AttachAsync(new AircraftDescriptor { Title = "FenixA319", Registration = "d-atst", LiveryFolder = "dlh-d-atst" });

        Assert.Equal("D-ATST", session.Identity.Registration);
    }

    [Fact]
    public async Task A_failing_catalog_lookup_does_not_prevent_the_session()
    {
        var logger = new ListLogger<FenixAircraftProvider>();
        var provider = new FenixAircraftProvider(new InMemoryCatalog { Failure = new IOException("cache locked") }, logger: logger);

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320", Registration = "F-GKXY", LiveryFolder = "x" });

        Assert.Equal("F-GKXY", session.Identity.Registration);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Title_wins_an_engine_conflict_and_the_conflict_is_logged()
    {
        var logger = new ListLogger<FenixAircraftProvider>();
        var provider = new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("f", "F-TEST", engine: "IAE", wingtip: "Sharklets")), logger: logger);

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320 CFM WF", LiveryFolder = "f" });

        Assert.Equal("CFM", session.Identity.EngineVariant);
        Assert.Equal("WingtipFence", session.Identity.WingtipConfiguration);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("conflict", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Catalog_fills_what_the_title_does_not_say()
    {
        var provider = new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("f", "F-TEST", engine: "IAE", wingtip: "Sharklets", operatorIcao: "AFR")));

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320", LiveryFolder = "f" });

        Assert.Equal("IAE", session.Identity.EngineVariant);
        Assert.Equal("Sharklets", session.Identity.WingtipConfiguration);
        Assert.Equal("AFR", session.Identity.OperatorIcao);
    }

    [Fact]
    public async Task Model_conflict_keeps_the_loaded_model_and_logs_it()
    {
        var logger = new ListLogger<FenixAircraftProvider>();
        var provider = new FenixAircraftProvider(new InMemoryCatalog(InMemoryCatalog.Livery("f", "F-TEST", model: "A319")), logger: logger);

        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA321 IAE WF SC", LiveryFolder = "f" });

        Assert.Equal("A321", session.Identity.Model);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("A319", StringComparison.Ordinal));
    }
}
