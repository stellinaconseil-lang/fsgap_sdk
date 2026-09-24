using FSGAP.Abstractions.Aircraft;
using FSGAP.Fenix.Tests.Fixtures;

namespace FSGAP.Fenix.Tests;

public class InstalledCatalogBehaviourTests
{
    [Fact]
    public async Task Livery_folder_lookup_ignores_case()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-321-liveries"), "aee-sx-dnh-7f2f", FenixInstallFixture.LiveryCfg("A321", atcId: "SX-DNH"));
        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();

        Assert.Equal("SX-DNH", (await catalog.FindByLiveryFolderAsync("AEE-SX-DNH-7F2F"))?.Identity.Registration);
        Assert.NotNull(await catalog.FindByLiveryFolderAsync(" aee-sx-dnh-7f2f "));
        Assert.Null(await catalog.FindByLiveryFolderAsync("unknown"));
        Assert.Null(await catalog.FindByLiveryFolderAsync(""));
    }

    [Theory]
    [InlineData("F-GKXY")]
    [InlineData("FGKXY")]
    [InlineData("f gkxy")]
    [InlineData("f-gkxy")]
    public async Task Registration_lookup_ignores_case_spaces_and_hyphens_but_keeps_the_display_form(string query)
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "afr", FenixInstallFixture.LiveryCfg("A320", atcId: "f-gkxy"));
        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();

        var match = Assert.Single(await catalog.FindByRegistrationAsync(query));

        Assert.Equal("F-GKXY", match.Identity.Registration);
    }

    [Fact]
    public async Task Duplicate_registrations_are_all_returned()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-321-liveries");
        fixture.Livery(package, "aee-sx-dtst-0001", FenixInstallFixture.LiveryCfg("A321", atcId: "SX-DTST"));
        fixture.Livery(package, "aee-sx-dtst-0002", FenixInstallFixture.LiveryCfg("A321", atcId: "SX-DTST"));
        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();

        var matches = await catalog.FindByRegistrationAsync("SX-DTST");

        Assert.Equal(["aee-sx-dtst-0001", "aee-sx-dtst-0002"], matches.Select(m => m.LiveryFolder));
    }

    [Fact]
    public async Task A_duplicate_livery_folder_is_ambiguous_not_arbitrarily_resolved()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "shared-folder", FenixInstallFixture.LiveryCfg("A320", atcId: "F-AAAA"));
        fixture.Livery(fixture.Package("fnx-extra-liveries"), "SHARED-FOLDER", FenixInstallFixture.LiveryCfg("A320", atcId: "F-BBBB"));
        var catalog = fixture.Catalog();

        var result = await catalog.RefreshAsync();

        Assert.Equal(2, result.AircraftCount);
        Assert.Contains(result.Errors, e => e.Contains("shared-folder", StringComparison.OrdinalIgnoreCase));
        Assert.Null(await catalog.FindByLiveryFolderAsync("shared-folder"));
        var provider = new FenixAircraftProvider(catalog);
        await using var session = await provider.AttachAsync(new AircraftDescriptor { Title = "FenixA320", Registration = "F-ATC", LiveryFolder = "shared-folder" });
        Assert.Equal("F-ATC", session.Identity.Registration); // falls back to the ATC id instead of guessing
    }

    [Fact]
    public async Task Refresh_replaces_the_content()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320-liveries");
        var first = fixture.Livery(package, "first", FenixInstallFixture.LiveryCfg("A320", atcId: "F-FRST"));
        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();

        Directory.Delete(first, recursive: true);
        fixture.Livery(package, "second", FenixInstallFixture.LiveryCfg("A320", atcId: "F-SCND"));
        Assert.NotNull(await catalog.FindByLiveryFolderAsync("first")); // lookups never rescan
        await catalog.RefreshAsync();

        Assert.Null(await catalog.FindByLiveryFolderAsync("first"));
        Assert.NotNull(await catalog.FindByLiveryFolderAsync("second"));
    }

    [Fact]
    public async Task Msfs_not_found_keeps_the_previous_content()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "a", FenixInstallFixture.LiveryCfg("A320", atcId: "F-AAAA"));
        IReadOnlyList<string>? roots = [fixture.Community];
        var catalog = fixture.Catalog(() => roots);
        await catalog.RefreshAsync();

        roots = null;
        var result = await catalog.RefreshAsync();

        Assert.Equal(FenixInstalledAircraftCatalog.MsfsNotFound, Assert.Single(result.Errors));
        Assert.Equal(1, result.AircraftCount);
        Assert.Single(await catalog.GetAllAsync());
    }

    [Fact]
    public async Task Cancelled_refresh_keeps_the_previous_content_and_cache()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320-liveries");
        for (var i = 0; i < 30; i++)
        {
            fixture.Livery(package, $"livery-{i:00}", FenixInstallFixture.LiveryCfg("A320", atcId: $"F-T{i:000}"));
        }

        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();
        var cacheWritten = File.GetLastWriteTimeUtc(catalog.CachePath);
        fixture.Livery(package, "new-one", FenixInstallFixture.LiveryCfg("A320", atcId: "F-NEW"));
        using var cts = new CancellationTokenSource();
        var progress = new RecordingProgress<CatalogScanProgress>
        {
            OnReport = p =>
            {
                if (p is { Phase: CatalogScanPhase.Scanning, Processed: > 0 })
                {
                    cts.Cancel();
                }
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.RefreshAsync(progress, cts.Token));

        Assert.Equal(30, (await catalog.GetAllAsync()).Count);
        Assert.Equal(cacheWritten, File.GetLastWriteTimeUtc(catalog.CachePath));
    }

    [Fact]
    public async Task Progress_is_throttled_monotonic_and_complete()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320-liveries");
        for (var i = 0; i < 100; i++)
        {
            fixture.Livery(package, $"livery-{i:000}", FenixInstallFixture.LiveryCfg("A320"));
        }

        var progress = new RecordingProgress<CatalogScanProgress>();
        await fixture.Catalog().RefreshAsync(progress);

        var reports = progress.Reports;
        Assert.Equal(new CatalogScanProgress(CatalogScanPhase.Discovering, 0, null), reports[0]);
        var scanning = reports.Skip(1).ToArray();
        Assert.All(scanning, r => Assert.Equal(100, r.Total));
        Assert.Equal(scanning.Select(r => r.Processed).OrderBy(p => p), scanning.Select(r => r.Processed));
        Assert.Equal(100, scanning[^1].Processed);
        Assert.InRange(reports.Count, 3, 25);
    }

    [Fact]
    public async Task Readers_never_see_a_half_built_catalog()
    {
        using var fixture = new FenixInstallFixture();
        var package = fixture.Package("fnx-aircraft-320-liveries");
        for (var i = 0; i < 50; i++)
        {
            fixture.Livery(package, $"old-{i:00}", FenixInstallFixture.LiveryCfg("A320"));
        }

        var catalog = fixture.Catalog();
        await catalog.RefreshAsync();
        for (var i = 0; i < 50; i++)
        {
            fixture.Livery(package, $"new-{i:00}", FenixInstallFixture.LiveryCfg("A320"));
        }

        var counts = new List<int>();
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                counts.Add((await catalog.GetAllAsync()).Count);
            }
        });
        await catalog.RefreshAsync();
        stop.Cancel();
        await reader;

        Assert.All(counts, c => Assert.True(c is 50 or 100, $"saw {c} entries"));
        Assert.Equal(100, (await catalog.GetAllAsync()).Count);
    }

    [Fact]
    public async Task The_last_scan_is_restored_from_the_cache_by_a_new_instance()
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-321-liveries"), "aee-sx-dnh-7f2f", FenixInstallFixture.LiveryCfg("A321,IAE,WF", "Test", "SX-DNH", "AEE"));
        await fixture.Catalog().RefreshAsync();

        var restarted = fixture.Catalog(() => throw new InvalidOperationException("must not scan"));
        var restored = await restarted.FindByLiveryFolderAsync("AEE-SX-DNH-7F2F");

        Assert.NotNull(restored);
        Assert.Equal("SX-DNH", restored.Identity.Registration);
        Assert.Equal("IAE", restored.Identity.EngineVariant);
        Assert.Equal("AEE", restored.Identity.OperatorIcao);
        Assert.StartsWith(fixture.DataDirectory, restarted.CachePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{\"SchemaVersion\":999,\"SavedAt\":\"2026-01-01T00:00:00Z\",\"Aircraft\":[]}")]
    [InlineData("[]")]
    public async Task A_corrupt_or_foreign_cache_is_ignored_and_rebuilt(string cacheContent)
    {
        using var fixture = new FenixInstallFixture();
        fixture.Livery(fixture.Package("fnx-aircraft-320-liveries"), "a", FenixInstallFixture.LiveryCfg("A320", atcId: "F-AAAA"));
        var catalog = fixture.Catalog();
        Directory.CreateDirectory(Path.GetDirectoryName(catalog.CachePath)!);
        await File.WriteAllTextAsync(catalog.CachePath, cacheContent);

        Assert.Empty(await catalog.GetAllAsync());
        await catalog.RefreshAsync();

        Assert.Single(await fixture.Catalog(() => null).GetAllAsync());
    }
}
