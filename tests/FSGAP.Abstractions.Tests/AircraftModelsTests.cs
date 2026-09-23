using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Abstractions.Tests;

public class AircraftModelsTests
{
    [Fact]
    public void Livery_folder_is_distinct_from_the_livery_display_name()
    {
        var descriptor = new AircraftDescriptor
        {
            Title = "Test Airliner",
            Livery = "Test Airways - F-TEST",
            LiveryFolder = "TST-F-TEST-0001",
            Registration = null, // some aircraft report an empty ATC id
        };

        Assert.Equal("Test Airways - F-TEST", descriptor.Livery);
        Assert.Equal("TST-F-TEST-0001", descriptor.LiveryFolder);
        Assert.Null(descriptor.Registration);
    }

    [Fact]
    public void Changing_only_the_livery_folder_is_a_different_aircraft()
    {
        var first = new AircraftDescriptor { Title = "Test Airliner", LiveryFolder = "livery-a" };
        var same = new AircraftDescriptor { Title = "Test Airliner", LiveryFolder = "livery-a" };
        var other = first with { LiveryFolder = "livery-b" };

        Assert.Equal(first, same);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Installed_aircraft_links_a_livery_folder_to_an_identity()
    {
        var installed = new InstalledAircraft
        {
            Id = "catalog-entry-1",
            Identity = new AircraftIdentity { Family = "A320", Model = "A320", EngineVariant = "CFM", Registration = "F-TEST" },
            LiveryFolder = "TST-F-TEST-0001",
            PackageName = "test-package",
        };

        Assert.Equal("F-TEST", installed.Identity.Registration);
        Assert.Equal("TST-F-TEST-0001", installed.LiveryFolder);
        Assert.Null(installed.ModifiedAt);
    }

    [Fact]
    public void Scan_result_copies_its_errors()
    {
        var errors = new List<string> { "broken livery" };
        var result = new CatalogScanResult { AircraftCount = 3, CompletedAt = DateTimeOffset.UnixEpoch, Errors = errors };

        errors.Add("another");

        Assert.Equal(["broken livery"], result.Errors);
        Assert.Empty(new CatalogScanResult { AircraftCount = 0, CompletedAt = DateTimeOffset.UnixEpoch }.Errors);
    }

    [Fact]
    public void Scan_progress_reports_an_unknown_total_while_discovering()
    {
        var discovering = new CatalogScanProgress(CatalogScanPhase.Discovering, 0, null);
        var scanning = new CatalogScanProgress(CatalogScanPhase.Scanning, 5, 20);

        Assert.Null(discovering.Total);
        Assert.Equal(20, scanning.Total);
    }
}
