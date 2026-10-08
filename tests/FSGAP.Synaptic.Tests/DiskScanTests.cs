using System.Text;
using System.Text.Json;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Msfs;
using FSGAP.Synaptic.Catalog;
using Xunit.Abstractions;

namespace FSGAP.Synaptic.Tests;

/// <summary>
/// W3-RC3C-B1 — the installed Synaptic A220-300 liveries enumerated from the MSFS package folders (plain livery.cfg files
/// and the uncompressed entries of unencrypted package archives), with NO simulator and NO livery ever loaded/learned.
/// Fixtures are synthetic package roots built in a temp directory; the archive layout is the one observed on the real
/// streamed packages (RASA v2, JSON header at 32, notEncrypted, livery.cfg stored raw).
/// </summary>
public sealed class DiskScanTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly TempDirectory _data = new();
    private readonly ITestOutputHelper _output;

    public DiskScanTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _root.Dispose();
        _data.Dispose();
    }

    private static string LiveryCfg(string name, string tags = "ext_a223_fuselage", string? panelConfig = null, string? atcId = null) =>
        "[Version]\nmajor = 1\nminor = 0\n\n[General]\n" + (name.Length > 0 ? $"name=\"{name}\"\n" : "") + (atcId is null ? "" : $"atc_id = \"{atcId}\"\n")
        + $"\n[Selection]\nrequired_tags = \"{tags}\"\n"
        + (panelConfig is null ? "" : $"\n[Panel_DynamicParameters]\nparam.0 = \"config,{panelConfig}\"\n");

    private const string Liveries = "simobjects\\airplanes\\synaptic_a220\\liveries\\inibuilds\\";

    /// <summary>Writes an MSFS 2024 package archive: RASA, version 2, JSON header at 32, then the stored entries.</summary>
    internal static void WriteArchive(string path, IEnumerable<(string Path, string Content, bool Compressed)> entries, string scheme = "notEncrypted")
    {
        var data = new MemoryStream();
        var infos = new List<object>();
        foreach (var (entryPath, content, compressed) in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            infos.Add(new Dictionary<string, object>
            {
                ["path"] = entryPath,
                ["byteOffset"] = data.Length,
                ["byteSize"] = bytes.Length,
                ["uncompressed_size"] = compressed ? bytes.Length * 3 : bytes.Length,
                ["hash"] = 1234567890123456789UL,
            });
            data.Write(bytes);
        }

        var header = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["encryptionSetup"] = new Dictionary<string, object> { ["scheme"] = scheme, ["version"] = 0 },
            ["fileInfoList"] = infos,
        }));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        var head = new byte[32];
        "RASA"u8.CopyTo(head);
        BitConverter.GetBytes(2u).CopyTo(head, 4);
        BitConverter.GetBytes(1u).CopyTo(head, 8);
        BitConverter.GetBytes((uint)header.Length).CopyTo(head, 12);
        file.Write(head);
        file.Write(header);
        file.Write(data.ToArray());
    }

    private static void WritePlain(string root, string package, string vendor, string folder, string content, string aircraftFolder = "synaptic_a220")
    {
        var dir = System.IO.Path.Combine(root, package, "SimObjects", "Airplanes", aircraftFolder, "liveries", vendor, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(System.IO.Path.Combine(dir, "livery.cfg"), content);
    }

    /// <summary>
    /// Two roots, like an MSFS install: StreamedPackages (the A220 liveries pack, the aircraft pack, an encrypted archive)
    /// plus a later-sorting duplicate package, and Community (a third-party plain livery, an unrelated Fenix package).
    /// </summary>
    private IReadOnlyList<string> BuildFixture()
    {
        var streamed = System.IO.Path.Combine(_root.Path, "StreamedPackages");
        var community = System.IO.Path.Combine(_root.Path, "Community");
        WriteArchive(System.IO.Path.Combine(streamed, "fs24-inibuilds-a220-liveries", "content", "minimal.fsarchive"),
        [
            ("_texture_headers.bin.fsc", "binary", true),
            (Liveries + "air france f-hzuf\\livery.cfg", LiveryCfg("Air France A220-300", panelConfig: "F-HZUF"), false),
            (Liveries + "air canada g-guac\\livery.cfg", LiveryCfg("Air Canada A220-300", panelConfig: "C-GUAC"), false),
            (Liveries + "korean air hl8315\\livery.cfg", LiveryCfg("Korean Air A220-300", panelConfig: "HL8315"), false),
            (Liveries + "compressed one\\livery.cfg", LiveryCfg("Compressed A220-300", panelConfig: "N1AB"), true),
            (Liveries + "no name\\livery.cfg", LiveryCfg(""), false),
            (Liveries + "a220-100 demo\\livery.cfg", LiveryCfg("A220-100 Demo", tags: "ext_a221_fuselage", panelConfig: "C-GXXX"), false),
        ]);
        WriteArchive(System.IO.Path.Combine(streamed, "fs24-inibuilds-aircraft-a220", "content", "minimal.fsarchive"),
        [
            (Liveries + "a_bcs3_syn_house\\livery.cfg", LiveryCfg("Synaptic House A220-300"), false),
            (Liveries + "white\\livery.cfg", LiveryCfg("White A220-300"), false),
            ("simobjects\\airplanes\\synaptic_a220\\presets\\inibuilds\\a220-300\\config\\ai.cfg", "[x]", false),
        ]);
        WriteArchive(System.IO.Path.Combine(streamed, "fs24-inibuilds-aircraft-a220", "content", "dfxrtata.fsarchive"),
            [(Liveries + "secret\\livery.cfg", LiveryCfg("Secret A220-300", panelConfig: "D-SECR"), false)], scheme: "aes");
        WritePlain(community, "thirdparty-a220-lufthansa", "thirdparty", "lufthansa d-aabc", LiveryCfg("Lufthansa City A220-300", atcId: "D-AABC"));
        WritePlain(streamed, "zz-duplicate-air-france", "copy", "air france copy", LiveryCfg("Air France A220-300", panelConfig: "F-WXYZ"));
        WritePlain(community, "fenix-a320-pack", "fenix", "british airways", LiveryCfg("British Airways A320", tags: "fnx_320"), aircraftFolder: "FNX_320");
        return [community, streamed];
    }

    private SynapticInstalledAircraftCatalog Catalog(IReadOnlyList<string> roots, FakeLiveryService liveries) =>
        new(new FsgapOptions { ApplicationName = "FSGAP.Synaptic.Tests", DataDirectory = _data.Path }, liveries, packageRoots: roots);

    private static FakeLiveryService SimulatorOff() =>
        new() { Failure = new SimulatorServiceException(SimulatorServiceError.SimulatorUnavailable, "not connected") };

    [Fact]
    public void Scanner_finds_every_A220_300_livery_with_its_folder_and_declared_registration()
    {
        var scan = SynapticLiveryDiskScanner.Scan(BuildFixture());

        Assert.Equal(
            [
                ("Lufthansa City A220-300", "lufthansa d-aabc", "D-AABC"),
                ("Air Canada A220-300", "air canada g-guac", "C-GUAC"),
                ("Air France A220-300", "air france f-hzuf", "F-HZUF"),
                ("Korean Air A220-300", "korean air hl8315", "HL8315"),
                ("Synaptic House A220-300", "a_bcs3_syn_house", null),
                ("White A220-300", "white", null),
            ],
            scan.Liveries.Select(l => (l.LiveryName, l.LiveryFolder, l.Registration)));
        // the zz-duplicate copy of Air France sorts after the liveries pack in the same root: the first copy is kept
        Assert.Contains(scan.Skipped, s => s.Contains("ext_a223_fuselage") && s.Contains("a220-100 demo"));
        Assert.Contains(scan.Skipped, s => s.Contains("no [General] name"));
        Assert.Contains(scan.Skipped, s => s.Contains("compressed"));
        Assert.Contains(scan.Skipped, s => s.Contains("duplicate of livery 'Air France A220-300'"));
        Assert.DoesNotContain(scan.Liveries, l => l.LiveryName.Contains("Secret"));          // encrypted archive: never read
        Assert.DoesNotContain(scan.Liveries, l => l.LiveryName.Contains("British Airways")); // another aircraft: never scanned
    }

    [Fact]
    public void Duplicates_keep_the_first_copy_in_root_then_package_order()
    {
        var community = System.IO.Path.Combine(_root.Path, "Community");
        WritePlain(community, "b-pack", "v", "air france b", LiveryCfg("Air France A220-300", panelConfig: "F-HZUB"));
        WritePlain(community, "a-pack", "v", "air france a", LiveryCfg("Air France A220-300", panelConfig: "F-HZUA"));

        var scan = SynapticLiveryDiskScanner.Scan([community]);

        var only = Assert.Single(scan.Liveries);
        Assert.Equal(("air france a", "F-HZUA", "a-pack"), (only.LiveryFolder, only.Registration, only.Package));
    }

    [Fact]
    public async Task Refresh_offline_lists_all_disk_liveries_none_learned_beforehand()
    {
        var roots = BuildFixture();
        var catalog = Catalog(roots, SimulatorOff());
        Assert.Empty(await catalog.GetAllAsync()); // nothing learned, nothing cached

        var scan = await catalog.RefreshAsync();
        var all = await catalog.GetAllAsync();

        Assert.Empty(scan.Errors);                 // the disk alone is a successful refresh
        Assert.Equal(6, scan.AircraftCount);
        Assert.Equal(6, all.Count);
        var france = Assert.Single(all, a => a.Identity.Livery == "Air France A220-300");
        Assert.Equal("F-HZUF", france.Identity.Registration);
        Assert.Equal(RegistrationSource.Authoritative, france.Identity.RegistrationSource);
        Assert.Equal("air france f-hzuf", france.LiveryFolder);
        Assert.All(all, a => Assert.Null(a.ModifiedAt)); // never observed loaded
        Assert.Null(Assert.Single(all, a => a.Identity.Livery == "White A220-300").Identity.Registration);
        // found by the folder the simulator reports, case-insensitively
        Assert.Equal(france.Id, (await catalog.FindByLiveryFolderAsync("AIR FRANCE F-HZUF"))!.Id);
        Assert.Single(await catalog.FindByRegistrationAsync("hl8315"));
    }

    [Fact]
    public async Task Refresh_online_merges_disk_and_simulator_enumeration_by_livery_name()
    {
        var roots = BuildFixture();
        var simulator = new FakeLiveryService
        {
            Rows =
            [
                new() { AircraftTitle = "A220-300", LiveryName = "Air France A220-300" },
                new() { AircraftTitle = "A220-300 - No Cabin", LiveryName = "Air France A220-300" },
                new() { AircraftTitle = "A220-300", LiveryName = "Delta A220-300" }, // streamed, unreadable on this disk
            ],
        };
        var catalog = Catalog(roots, simulator);

        var scan = await catalog.RefreshAsync();
        var all = await catalog.GetAllAsync();

        Assert.Empty(scan.Errors);
        Assert.Equal(7, all.Count); // 6 from disk + Delta from the simulator only
        var delta = Assert.Single(all, a => a.Identity.Livery == "Delta A220-300");
        Assert.Null(delta.Identity.Registration); // the enumeration has none, and nothing is invented
        Assert.Equal("F-HZUF", Assert.Single(all, a => a.Identity.Livery == "Air France A220-300").Identity.Registration);
    }

    [Fact]
    public async Task Learning_a_disk_livery_keeps_its_declared_registration_over_the_folder_derived_one()
    {
        var catalog = Catalog(BuildFixture(), SimulatorOff());
        await catalog.RefreshAsync();

        // The Air Canada folder says G-GUAC; the livery declares C-GUAC. Loading it must not downgrade C-GUAC.
        var resolved = catalog.Learn(new AircraftDescriptor { Title = "A220-300", Livery = "Air Canada A220-300", LiveryFolder = "AIR CANADA G-GUAC", Registration = "" });

        Assert.Equal(("C-GUAC", RegistrationSource.Authoritative), (resolved!.Registration, resolved.Source));
        var canada = Assert.Single(await catalog.GetAllAsync(), a => a.Identity.Livery == "Air Canada A220-300");
        Assert.Equal("C-GUAC", canada.Identity.Registration);
        Assert.Equal("AIR CANADA G-GUAC", canada.LiveryFolder); // the folder the simulator reports wins once observed
    }

    [Fact]
    public async Task Offline_refresh_never_drops_a_known_livery_the_disk_cannot_read()
    {
        var simulator = new FakeLiveryService { Rows = [new() { AircraftTitle = "A220-300", LiveryName = "Delta A220-300" }] };
        var roots = BuildFixture();
        var catalog = Catalog(roots, simulator);
        await catalog.RefreshAsync(); // online: Delta known from the simulator only

        simulator.Failure = new SimulatorServiceException(SimulatorServiceError.SimulatorUnavailable, "not connected");
        await catalog.RefreshAsync(); // offline: disk only

        Assert.Contains(await catalog.GetAllAsync(), a => a.Identity.Livery == "Delta A220-300");
    }

    [Fact]
    public async Task Nothing_on_disk_and_no_simulator_keeps_the_previous_cache_and_reports_why()
    {
        var roots = BuildFixture();
        var catalog = Catalog(roots, SimulatorOff());
        await catalog.RefreshAsync();
        var before = File.ReadAllText(catalog.CachePath);

        var empty = Catalog([System.IO.Path.Combine(_root.Path, "Nothing")], SimulatorOff());
        var scan = await empty.RefreshAsync();

        Assert.Equal(["not connected"], scan.Errors);
        Assert.Equal(6, scan.AircraftCount);              // the previous content, read from the cache
        Assert.Equal(before, File.ReadAllText(catalog.CachePath)); // untouched
    }

    [Fact]
    public async Task No_installation_and_no_simulator_reports_both_reasons()
    {
        var catalog = new SynapticInstalledAircraftCatalog(
            new FsgapOptions { ApplicationName = "FSGAP.Synaptic.Tests", DataDirectory = _data.Path }, SimulatorOff(), packageRoots: []);
        var scan = await catalog.RefreshAsync();
        Assert.Equal(["not connected"], scan.Errors); // an EMPTY root list disables the disk scan (it is not "not found")
    }

    [Fact]
    public async Task A_cancelled_refresh_changes_nothing()
    {
        var catalog = Catalog(BuildFixture(), SimulatorOff());
        await catalog.RefreshAsync();
        var before = File.ReadAllText(catalog.CachePath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.RefreshAsync(cancellationToken: cts.Token));
        Assert.Equal(before, File.ReadAllText(catalog.CachePath));
    }

    [Fact]
    public void The_archive_reader_refuses_encrypted_or_foreign_files_and_compressed_entries()
    {
        var dir = _root.Path;
        Directory.CreateDirectory(dir);
        var encrypted = System.IO.Path.Combine(dir, "enc.fsarchive");
        WriteArchive(encrypted, [("a\\livery.cfg", "x", false)], scheme: "aes");
        var garbage = System.IO.Path.Combine(dir, "garbage.fsarchive");
        File.WriteAllBytes(garbage, [.. "RASA"u8, 2, 0, 0, 0, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F, .. new byte[16], .. "not json"u8]);
        var plain = System.IO.Path.Combine(dir, "ok.fsarchive");
        WriteArchive(plain, [("a\\livery.cfg", "hello", false), ("b.bin", "zzz", true)]);

        Assert.Null(FsArchive.TryReadIndex(encrypted));
        Assert.Null(FsArchive.TryReadIndex(garbage));
        var entries = FsArchive.TryReadIndex(plain)!;
        Assert.Equal("hello", FsArchive.TryReadText(plain, entries[0]));
        Assert.Null(FsArchive.TryReadEntry(plain, entries[1])); // compressed: never decoded
    }

    /// <summary>
    /// LIVE acceptance (opt-in: FSGAP_LIVE_MSFS_DISK=1): scans THIS machine's real MSFS 2024 package folders, read-only, and
    /// prints what it finds. No simulator, no cache written (fresh temp data directory). Does nothing otherwise.
    /// </summary>
    [Fact]
    public async Task Live_disk_acceptance_on_this_machine()
    {
        if (Environment.GetEnvironmentVariable("FSGAP_LIVE_MSFS_DISK") != "1")
        {
            return;
        }

        var msfs = MsfsInstallationLocator.Locate();
        Assert.NotNull(msfs);
        var roots = MsfsInstallationLocator.FindPackageRoots(msfs!.InstalledPackagesPath, SynapticInstalledAircraftCatalog.PackageRootNames);
        _output.WriteLine($"roots: {string.Join(" | ", roots)}");
        var scan = SynapticLiveryDiskScanner.Scan(roots);
        _output.WriteLine($"packages scanned: {scan.PackagesScanned}, A220-300 liveries: {scan.Liveries.Count}");
        foreach (var l in scan.Liveries)
        {
            _output.WriteLine($"  {l.LiveryName} | folder={l.LiveryFolder} | registration={l.Registration ?? "-"} | package={l.Package}");
        }

        foreach (var s in scan.Skipped)
        {
            _output.WriteLine($"  skipped: {s}");
        }

        var catalog = Catalog(roots, SimulatorOff());
        var result = await catalog.RefreshAsync();
        _output.WriteLine($"catalog (simulator off): {result.AircraftCount} entries, errors: {string.Join("; ", result.Errors)}");
        Assert.NotEmpty(scan.Liveries);
    }
}
