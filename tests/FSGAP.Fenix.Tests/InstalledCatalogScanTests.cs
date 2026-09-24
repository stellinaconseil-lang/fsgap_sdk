using FSGAP.Fenix.Tests.Fixtures;

namespace FSGAP.Fenix.Tests;

public class InstalledCatalogScanTests
{
    [Fact]
    public async Task Empty_installation_yields_an_empty_catalog_without_errors()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Package("some-scenery-package");

        var result = await fixture.Catalog().RefreshAsync();

        Assert.Equal(0, result.AircraftCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task A_community_livery_is_read_from_livery_cfg()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320-liveries");
        fixture.Livery(package, "aca-c-ftst-0001", FenixInstallFixture.LiveryCfg("A320,CFM,SL", "Test Air C-FTST", "C-FTST", "aca"));
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();

        var aircraft = Assert.Single(await catalog.GetAllAsync());
        Assert.Equal("aca-c-ftst-0001", aircraft.LiveryFolder);
        Assert.Equal("fnx-aircraft-320-liveries", aircraft.PackageName);
        Assert.StartsWith("fenix:", aircraft.Id, StringComparison.Ordinal);
        Assert.NotNull(aircraft.ModifiedAt);
        Assert.Equal("Fenix Simulations", aircraft.Identity.Developer);
        Assert.Equal("A320", aircraft.Identity.Model);
        Assert.Equal("CFM", aircraft.Identity.EngineVariant);
        Assert.Equal("Sharklets", aircraft.Identity.WingtipConfiguration);
        Assert.Equal("C-FTST", aircraft.Identity.Registration);
        Assert.Equal("ACA", aircraft.Identity.OperatorIcao);
        Assert.Equal("Test Air C-FTST", aircraft.Identity.Livery);
    }

    [Fact]
    public async Task A_classic_aircraft_cfg_livery_is_read_too()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fenix-a320-legacy-livery");
        fixture.AircraftCfgLivery(package, "legacy", FenixInstallFixture.AircraftCfg("FenixA320 IAE WF Test", "F-HTST", "Test Legacy Livery"));
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();

        var aircraft = Assert.Single(await catalog.GetAllAsync());
        Assert.Equal("A320", aircraft.Identity.Model);
        Assert.Equal("IAE", aircraft.Identity.EngineVariant);
        Assert.Equal("F-HTST", aircraft.Identity.Registration);
        Assert.Equal("Test Legacy Livery", aircraft.Identity.Livery);
    }

    [Fact]
    public async Task Liveries_are_found_across_packages_and_package_roots()
    {
        using var fixture = new FenixInstallFixture();
        var official = fixture.PackageRoot("Official2024");
        fixture.Livery(fixture.Package("fnx-aircraft-319-liveries"), "a", FenixInstallFixture.LiveryCfg("A319,IAE,WF,Cabin_A319_SD", atcId: "D-AAAA"));
        fixture.Livery(fixture.Package("fnx-aircraft-321-liveries"), "b", FenixInstallFixture.LiveryCfg("A321,CFM,SL", atcId: "D-BBBB"));
        fixture.Livery(fixture.Package("fenix-store-package", official), "c", FenixInstallFixture.LiveryCfg("A320,CFM,WF", atcId: "D-CCCC"));

        var catalog = fixture.Catalog(() => [fixture.Community, official]);
        var result = await catalog.RefreshAsync();

        Assert.Equal(3, result.AircraftCount);
        Assert.Equal(["A319", "A321", "A320"], (await catalog.GetAllAsync()).Select(a => a.Identity.Model));
    }

    [Fact]
    public async Task Missing_fields_stay_unknown()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "bare", "[FLTSIM]\r\nfnx_option = 1");
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();

        var identity = Assert.Single(await catalog.GetAllAsync()).Identity;
        Assert.Null(identity.Model);
        Assert.Null(identity.EngineVariant);
        Assert.Null(identity.WingtipConfiguration);
        Assert.Null(identity.Registration);
        Assert.Null(identity.OperatorIcao);
    }

    [Fact]
    public async Task Registration_and_model_are_never_guessed_from_names()
    {
        using var fixture = new FenixInstallFixture();
        // The package name mentions 319 and 321 and the folder looks like a registration, but nothing declares them.
        fixture.Livery(fixture.Package("fnx-aircraft-319-321"), "dlh-d-aibx-0001", "[GENERAL]\r\nname = \"Test D-AIBX\"");
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();

        var identity = Assert.Single(await catalog.GetAllAsync()).Identity;
        Assert.Null(identity.Model);
        Assert.Null(identity.Registration);
    }

    [Fact]
    public async Task Tags_win_over_the_aircraft_cfg_title_inside_a_livery()
    {
        using var fixture = new FenixInstallFixture();
        var folder = fixture.AircraftCfgLivery(fixture.Package("fnx-mixed"), "mixed", FenixInstallFixture.AircraftCfg("FenixA320 CFM SL"));
        File.WriteAllText(Path.Combine(folder, "livery.cfg"), FenixInstallFixture.LiveryCfg("A320,IAE,WF"));
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();

        var identity = Assert.Single(await catalog.GetAllAsync()).Identity;
        Assert.Equal("IAE", identity.EngineVariant);
        Assert.Equal("WingtipFence", identity.WingtipConfiguration);
    }

    [Fact]
    public async Task Disabled_liveries_are_skipped()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320");
        fixture.Livery(package, "FNX_320_Fenix", FenixInstallFixture.LiveryCfg("Disabled"));
        fixture.Livery(package, "FNX_320_Fenix_CFM_WF", FenixInstallFixture.LiveryCfg("A320,CFM,WF"));
        var catalog = fixture.Catalog();

        var result = await catalog.RefreshAsync();

        Assert.Empty(result.Errors);
        Assert.Equal("FNX_320_Fenix_CFM_WF", Assert.Single(await catalog.GetAllAsync()).LiveryFolder);
    }

    [Fact]
    public async Task Fenix_internal_trees_are_not_liveries()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320");
        const string airplane = "SimObjects/Airplanes/FNX_32X";
        fixture.AircraftCfgLivery(package, "FNX_320_CFM_WF/config", FenixInstallFixture.AircraftCfg("FenixA320 CFM WF", "G-FENX"), $"{airplane}/presets/fnx");
        fixture.AircraftCfgLivery(package, "Function_Exterior_Test/config", FenixInstallFixture.AircraftCfg("attachment", "G-SMOL"), $"{airplane}/attachments/fnx");
        fixture.Livery(package, "presets-collection", FenixInstallFixture.LiveryCfg("A320", atcId: "F-KEEP"));
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();

        Assert.Equal("F-KEEP", Assert.Single(await catalog.GetAllAsync()).Identity.Registration);
    }

    [Fact]
    public async Task Only_Fenix_packages_are_scanned()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("pmdg-aircraft-738-liveries"), "pmdg", FenixInstallFixture.LiveryCfg("B738", atcId: "N-PMDG"));
        fixture.Livery(fixture.Package("other-a320-liveries", manifestJson: "{\"title\":\"A320 Liveries\",\"creator\":\"Someone\"}"), "x", FenixInstallFixture.LiveryCfg("A320", atcId: "F-OTHR"));
        fixture.Package("q-dsn-aicopilot-fnx-A320"); // mentions fnx, has no livery
        fixture.Livery(fixture.Package("acme-a32x-pack", manifestJson: "{\"creator\":\"Fenix Simulations\"}"), "y", FenixInstallFixture.LiveryCfg("A320", atcId: "F-MANI"));
        fixture.Livery(fixture.Package("broken-manifest-pack", manifestJson: "{not json"), "z", FenixInstallFixture.LiveryCfg("A320", atcId: "F-BRKN"));
        var catalog = fixture.Catalog();

        var result = await catalog.RefreshAsync();

        Assert.Empty(result.Errors);
        Assert.Equal("F-MANI", Assert.Single(await catalog.GetAllAsync()).Identity.Registration);
    }

    [Fact]
    public async Task A_broken_livery_is_reported_and_does_not_stop_the_scan()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320-liveries");
        fixture.Livery(package, "good", FenixInstallFixture.LiveryCfg("A320", atcId: "F-GOOD"));
        var locked = fixture.Livery(package, "locked", FenixInstallFixture.LiveryCfg("A320", atcId: "F-LOCK"));
        fixture.Livery(package, "garbage", "\u0000\u0001 not a cfg [[[ ===\r\n[SELECTION\r\nrequired_tags = \"A320\"");
        using var hold = new FileStream(Path.Combine(locked, "livery.cfg"), FileMode.Open, FileAccess.Read, FileShare.None);
        var catalog = fixture.Catalog();

        var result = await catalog.RefreshAsync();

        Assert.Equal(2, result.AircraftCount); // good + garbage (tolerantly parsed, fields unknown)
        var error = Assert.Single(result.Errors);
        Assert.Contains("locked", error, StringComparison.Ordinal);
        Assert.Contains(await catalog.GetAllAsync(), a => a.Identity.Registration == "F-GOOD");
    }

    [Fact]
    public async Task Scanning_never_writes_to_the_installation()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "a", FenixInstallFixture.LiveryCfg("A320", atcId: "F-AAAA"));
        var before = fixture.InstallationFiles();

        await fixture.Catalog().RefreshAsync();

        Assert.Equal(before, fixture.InstallationFiles());
        Assert.True(File.Exists(Path.Combine(fixture.DataDirectory, "fenix", "installed-aircraft.json")));
    }

    [Fact]
    public async Task Ids_are_stable_across_rescans()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "a", FenixInstallFixture.LiveryCfg("A320"));
        var catalog = fixture.Catalog();

        await catalog.RefreshAsync();
        var first = Assert.Single(await catalog.GetAllAsync()).Id;
        await catalog.RefreshAsync();

        Assert.Equal(first, Assert.Single(await catalog.GetAllAsync()).Id);
    }
}
