using System.Text.Json;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Synaptic.Tests;

/// <summary>The installed Synaptic A220 catalog: enumeration filtering, dedupe, learning and its cache.</summary>
public sealed class CatalogTests : IDisposable
{
    private readonly TempDirectory _data = new();
    private readonly FakeLiveryService _liveries = new() { Rows = LoadFixture() };

    public void Dispose() => _data.Dispose();

    /// <summary>The BLOCK 10A.5 live enumeration rows for the two presets, plus rows of other aircraft.</summary>
    private static IReadOnlyList<InstalledLivery> LoadFixture()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "a220-livery-enumeration.json")));
        var a220 = json.RootElement.GetProperty("a220Rows").EnumerateArray()
            .Select(r => new InstalledLivery { AircraftTitle = r.GetProperty("AircraftTitle").GetString()!, LiveryName = r.GetProperty("LiveryName").GetString() });
        InstalledLivery[] others =
        [
            new() { AircraftTitle = "FenixA320 IAE WF", LiveryName = "British Airways" },
            new() { AircraftTitle = "Asobo PassiveAircraft A220-300", LiveryName = "Air France A220-300" },
            new() { AircraftTitle = "FSLTL A220-300 Swiss", LiveryName = "Swiss A220-300 FSLTL" },
        ];
        return [.. a220, .. others];
    }

    private SynapticInstalledAircraftCatalog NewCatalog() => new(new FsgapOptions { ApplicationName = "FSGAP.Synaptic.Tests", DataDirectory = _data.Path }, _liveries);

    [Fact]
    public async Task The_live_enumeration_gives_eleven_logical_liveries_each_under_both_presets()
    {
        var catalog = NewCatalog();

        var scan = await catalog.RefreshAsync();
        var all = await catalog.GetAllAsync();

        Assert.Equal(11, scan.AircraftCount);
        Assert.Empty(scan.Errors);
        Assert.Equal(
            ["Air Baltic A220-300", "Air Canada A220-300", "Air France A220-300", "Breeze A220-300", "Delta A220-300", "ITA Airways A220-300",
             "JetBlue A220-300", "Korean Air A220-300", "Swiss A220-300", "Synaptic House A220-300", "White A220-300"],
            all.Select(a => a.Identity.Livery));
        Assert.All(all, a =>
        {
            Assert.Equal("BCS3", a.Identity.IcaoType);
            Assert.Equal("synaptic", a.Id.Split(':')[0]);
            Assert.Null(a.Identity.Registration); // the enumeration carries none
            Assert.Null(a.LiveryFolder);
        });
        Assert.Equal(11, all.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Grouping_keeps_only_the_presets_and_merges_cabin_and_no_cabin_by_livery_name()
    {
        var groups = SynapticInstalledAircraftCatalog.Group(LoadFixture());

        Assert.Equal(11, groups.Count);
        Assert.All(groups, g => Assert.Equal(["A220-300", "A220-300 - No Cabin"], g.Presets));
        Assert.DoesNotContain(groups, g => g.LiveryName.Contains("FSLTL", StringComparison.Ordinal) || g.LiveryName == "British Airways");
    }

    [Fact]
    public void Unnamed_rows_are_not_listed_and_case_differences_are_the_same_livery()
    {
        var groups = SynapticInstalledAircraftCatalog.Group(
        [
            new() { AircraftTitle = "A220-300", LiveryName = "" },
            new() { AircraftTitle = "A220-300 - No Cabin", LiveryName = null },
            new() { AircraftTitle = "A220-300", LiveryName = "Delta A220-300" },
            new() { AircraftTitle = "a220-300 - no cabin", LiveryName = "DELTA A220-300 " },
        ]);

        var only = Assert.Single(groups);
        Assert.Equal("Delta A220-300", only.LiveryName);
        Assert.Equal(2, only.Presets.Count);
    }

    [Fact]
    public void Stable_ids_depend_on_the_livery_name_only()
    {
        Assert.Equal(SynapticInstalledAircraftCatalog.StableId("Delta A220-300"), SynapticInstalledAircraftCatalog.StableId(" delta a220-300"));
        Assert.NotEqual(SynapticInstalledAircraftCatalog.StableId("Delta A220-300"), SynapticInstalledAircraftCatalog.StableId("Swiss A220-300"));
    }

    [Fact]
    public async Task Learning_a_loaded_livery_records_its_folder_and_registration_and_finds_it()
    {
        var catalog = NewCatalog();
        await catalog.RefreshAsync();

        var resolved = catalog.Learn(Descriptors.Delta);
        var byFolder = await catalog.FindByLiveryFolderAsync("delta n324du");
        var byRegistration = await catalog.FindByRegistrationAsync("n-324du");

        Assert.Equal(new("N324DU", RegistrationSource.Derived), resolved);
        Assert.NotNull(byFolder);
        Assert.Equal("N324DU", byFolder.Identity.Registration);
        Assert.Equal(RegistrationSource.Derived, byFolder.Identity.RegistrationSource);
        Assert.NotNull(byFolder.ModifiedAt);
        Assert.Equal(byFolder.Id, Assert.Single(byRegistration).Id);
    }

    [Fact]
    public async Task House_and_white_liveries_have_no_registration_even_when_the_simulator_reports_an_atc_id()
    {
        var catalog = NewCatalog();
        await catalog.RefreshAsync();

        Assert.Null(catalog.Learn(Descriptors.A220("A220-300", "A_BCS3_SYN_HOUSE", "Synaptic House A220-300", "C-FFCO")));
        Assert.Null(catalog.Learn(Descriptors.A220("A220-300", "WHITE", "White A220-300", "C-FFCO")));

        var all = await catalog.GetAllAsync();
        Assert.Null(all.Single(a => a.Identity.Livery == "Synaptic House A220-300").Identity.Registration);
        Assert.Null(all.Single(a => a.Identity.Livery == "White A220-300").Identity.Registration);
        Assert.Empty(await catalog.FindByRegistrationAsync("C-FFCO"));
    }

    [Fact]
    public async Task The_cache_roundtrips_learned_data_and_keeps_the_original_source()
    {
        var first = NewCatalog();
        await first.RefreshAsync();
        first.Learn(Descriptors.A220("A220-300", "SWISS HB-JCO", "Swiss A220-300", "HB-JCO")); // corroborated: observed

        _liveries.Failure = new SimulatorServiceException(SimulatorServiceError.SimulatorUnavailable, "no simulator");
        var second = NewCatalog();
        var all = await second.GetAllAsync();
        var swiss = all.Single(a => a.Identity.Livery == "Swiss A220-300");

        Assert.Equal(11, all.Count);
        Assert.Equal("HB-JCO", swiss.Identity.Registration);
        Assert.Equal(RegistrationSource.Observed, swiss.Identity.RegistrationSource);
        Assert.Equal("SWISS HB-JCO", swiss.LiveryFolder);

        // Loaded later without any current evidence (no folder, no ATC id): the cached value and its source are kept.
        Assert.Equal(new("HB-JCO", RegistrationSource.Observed), second.Learn(Descriptors.A220("A220-300", null, "Swiss A220-300", null)));
        Assert.Equal(1, _liveries.Calls); // only the first catalog enumerated
    }

    [Fact]
    public async Task A_learned_livery_survives_a_refresh()
    {
        var catalog = NewCatalog();
        await catalog.RefreshAsync();
        catalog.Learn(Descriptors.AirFrance);

        await catalog.RefreshAsync();

        Assert.Equal("F-HZUF", (await catalog.FindByLiveryFolderAsync("AIR FRANCE F-HZUF"))?.Identity.Registration);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"SchemaVersion\":99,\"Liveries\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Liveries\":[{\"LiveryName\":\"\",\"Presets\":[]}]}")]
    [InlineData("")]
    public async Task A_corrupt_or_foreign_cache_is_ignored_and_rewritten(string content)
    {
        var catalog = NewCatalog();
        Directory.CreateDirectory(Path.GetDirectoryName(catalog.CachePath)!);
        await File.WriteAllTextAsync(catalog.CachePath, content);

        Assert.Empty(await catalog.GetAllAsync());
        var scan = await catalog.RefreshAsync();

        Assert.Equal(11, scan.AircraftCount);
        Assert.Equal(11, (await NewCatalog().GetAllAsync()).Count);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(catalog.CachePath)!, "*.tmp"));
    }

    [Fact]
    public async Task A_failed_enumeration_keeps_the_previous_content_and_reports_the_error()
    {
        var catalog = NewCatalog();
        await catalog.RefreshAsync();
        _liveries.Failure = new SimulatorServiceException(SimulatorServiceError.QueryFailed, "enumeration failed");

        var scan = await catalog.RefreshAsync();

        Assert.Equal(11, scan.AircraftCount);
        Assert.Equal(["enumeration failed"], scan.Errors);
        Assert.Equal(11, (await catalog.GetAllAsync()).Count);
    }

    [Fact]
    public async Task A_folder_shared_by_two_liveries_is_not_resolved()
    {
        var catalog = NewCatalog();
        await catalog.RefreshAsync();
        catalog.Learn(Descriptors.A220("A220-300", "SHARED", "Delta A220-300"));
        catalog.Learn(Descriptors.A220("A220-300", "SHARED", "Breeze A220-300"));

        Assert.Null(await catalog.FindByLiveryFolderAsync("SHARED"));
    }
}
