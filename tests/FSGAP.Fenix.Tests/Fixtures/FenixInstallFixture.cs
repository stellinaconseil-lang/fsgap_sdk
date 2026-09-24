using FSGAP.Abstractions.Configuration;

namespace FSGAP.Fenix.Tests.Fixtures;

/// <summary>
/// A synthetic MSFS 2024 installation in a temporary folder: package roots, Fenix and non-Fenix packages, and
/// minimal livery files containing only the fragments the catalog reads. No proprietary content.
/// </summary>
internal sealed class FenixInstallFixture : IDisposable
{
    public const string LiveriesPath = "SimObjects/Airplanes/FNX_32X/liveries/fnx";

    public FenixInstallFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "fsgap-fenix-tests", Guid.NewGuid().ToString("N"));
        PackagesPath = Path.Combine(Root, "packages");
        Community = Path.Combine(PackagesPath, "Community");
        Directory.CreateDirectory(Community);
        DataDirectory = Path.Combine(Root, "data");
    }

    public string Root { get; }

    /// <summary>The <c>InstalledPackagesPath</c> of the synthetic installation.</summary>
    public string PackagesPath { get; }

    public string Community { get; }

    public string DataDirectory { get; }

    public FsgapOptions Options => new() { ApplicationName = "FenixTests", DataDirectory = DataDirectory };

    public string PackageRoot(string name)
    {
        var path = Path.Combine(PackagesPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string Package(string name, string? root = null, string? manifestJson = null)
    {
        var path = Path.Combine(root ?? Community, name);
        Directory.CreateDirectory(path);
        if (manifestJson is not null)
        {
            File.WriteAllText(Path.Combine(path, "manifest.json"), manifestJson);
        }

        return path;
    }

    /// <summary>A Fenix community-style livery: a folder with a single <c>livery.cfg</c>.</summary>
    public string Livery(string package, string folder, string liveryCfg, string relativeParent = LiveriesPath)
    {
        var path = Path.Combine(package, relativeParent, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "livery.cfg"), liveryCfg);
        return path;
    }

    /// <summary>A classic livery: a folder with an <c>aircraft.cfg</c>.</summary>
    public string AircraftCfgLivery(string package, string folder, string aircraftCfg, string relativeParent = LiveriesPath)
    {
        var path = Path.Combine(package, relativeParent, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "aircraft.cfg"), aircraftCfg);
        return path;
    }

    public static string LiveryCfg(string? tags = null, string? name = null, string? atcId = null, string? icaoAirline = null)
    {
        var lines = new List<string>();
        if (tags is not null)
        {
            lines.AddRange(["[SELECTION]", $"required_tags = \"{tags}\""]);
        }

        if (name is not null)
        {
            lines.AddRange(["[GENERAL]", $"name = \"{name}\""]);
        }

        lines.Add("[FLTSIM]");
        if (atcId is not null)
        {
            lines.Add($"atc_id = \"{atcId}\"");
        }

        if (icaoAirline is not null)
        {
            lines.Add($"icao_airline = \"{icaoAirline}\"");
        }

        lines.Add("fnx_some_option = 1 // unrelated Fenix option");
        return string.Join(Environment.NewLine, lines);
    }

    public static string AircraftCfg(string title, string? atcId = null, string? uiVariation = null) => string.Join(
        Environment.NewLine,
        "[VERSION]",
        "major = 1",
        "[FLTSIM.0]",
        $"title = \"{title}\"",
        atcId is null ? "// no atc_id" : $"atc_id = \"{atcId}\"",
        uiVariation is null ? "// no ui_variation" : $"ui_variation = \"{uiVariation}\"");

    /// <summary>Every file of the installation with its size and write time, to prove the scan never writes there.</summary>
    public IReadOnlyDictionary<string, (long Length, DateTime Written)> InstallationFiles() =>
        Directory.GetFiles(PackagesPath, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => (new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)));

    public FenixInstalledAircraftCatalog Catalog(Func<IReadOnlyList<string>?>? roots = null, Microsoft.Extensions.Logging.ILogger? logger = null) =>
        new(Options, roots ?? (() => [Community]), logger, null);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a temporary folder left behind is harmless.
        }
    }
}
