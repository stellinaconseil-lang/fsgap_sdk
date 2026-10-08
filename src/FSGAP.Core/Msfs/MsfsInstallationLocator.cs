using System.Text.RegularExpressions;

namespace FSGAP.Core.Msfs;

/// <summary>Where MSFS 2024 keeps its packages on this machine.</summary>
public sealed record MsfsInstallation(string UserCfgPath, string InstalledPackagesPath, IReadOnlyList<string> PackageRoots);

/// <summary>
/// Finds the MSFS 2024 package roots from <c>UserCfg.opt</c>, never from a hard-coded Community path. Ported from the
/// audited applications (<c>Msfs2024InstallationLocator</c>).
/// </summary>
/// <remarks>
/// <para>
/// This logic is generic to MSFS, not to any vendor: a building block for providers that read installed packages. Each
/// provider chooses which roots it scans; <see cref="MsfsInstallation.PackageRoots"/> keeps the historical four. Strictly
/// read-only.
/// </para>
/// <para>Search order for <c>UserCfg.opt</c>:</para>
/// <list type="number">
/// <item><description>the Microsoft Store location;</description></item>
/// <item><description>the Steam location;</description></item>
/// <item><description>
/// a one-level search for renamed Store or Steam folders (never a disk scan).
/// </description></item>
/// </list>
/// <para>
/// The package roots are those of <c>Community</c>, <c>Community2024</c>, <c>Official2024</c> and
/// <c>Official2020</c> that exist under <c>InstalledPackagesPath</c>.
/// </para>
/// </remarks>
public static partial class MsfsInstallationLocator
{
    private static readonly string[] KnownPackageRootNames = ["Community", "Community2024", "Official2024", "Official2020"];

    /// <summary>The root holding streamed (Marketplace) packages, e.g. the Synaptic A220 and its liveries. Opt-in per provider.</summary>
    public const string StreamedPackagesRootName = "StreamedPackages";

    /// <summary>Locates MSFS 2024 for the current user.</summary>
    public static MsfsInstallation? Locate() => Locate(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>Locates MSFS 2024 under the given profile folders; null when not installed or not configured.</summary>
    public static MsfsInstallation? Locate(string localAppData, string roamingAppData)
    {
        var userCfg = FindUserCfg(localAppData, roamingAppData);
        if (userCfg is null)
        {
            return null;
        }

        var packagesPath = ReadInstalledPackagesPath(userCfg);
        return packagesPath is not null && Directory.Exists(packagesPath)
            ? new MsfsInstallation(userCfg, packagesPath, FindPackageRoots(packagesPath))
            : null;
    }

    /// <summary>The known package roots that exist under <paramref name="installedPackagesPath"/>.</summary>
    public static IReadOnlyList<string> FindPackageRoots(string installedPackagesPath) =>
        FindPackageRoots(installedPackagesPath, KnownPackageRootNames);

    /// <summary>The named package roots that exist under <paramref name="installedPackagesPath"/>, in the given order.</summary>
    public static IReadOnlyList<string> FindPackageRoots(string installedPackagesPath, IEnumerable<string> rootNames) =>
        rootNames.Select(name => Path.Combine(installedPackagesPath, name)).Where(Directory.Exists).ToArray();

    private static string? FindUserCfg(string localAppData, string roamingAppData)
    {
        var packages = Path.Combine(localAppData, "Packages");
        string[] exact =
        [
            Path.Combine(packages, "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            Path.Combine(roamingAppData, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
        ];
        var fallbacks = Subdirectories(packages)
            .Where(d => Path.GetFileName(d).Contains("Limitless", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(d).Contains("FlightSimulator", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.Combine(d, "LocalCache", "UserCfg.opt"))
            .Concat(Subdirectories(roamingAppData)
                .Where(d => Path.GetFileName(d).Contains("Flight Simulator", StringComparison.OrdinalIgnoreCase))
                .Select(d => Path.Combine(d, "UserCfg.opt")));

        return exact.Concat(fallbacks).FirstOrDefault(File.Exists);
    }

    private static string? ReadInstalledPackagesPath(string userCfgPath)
    {
        try
        {
            return File.ReadLines(userCfgPath)
                .Select(line => InstalledPackagesPathPattern().Match(line))
                .FirstOrDefault(match => match.Success)?.Groups[1].Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Subdirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex("InstalledPackagesPath\\s+\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex InstalledPackagesPathPattern();
}
