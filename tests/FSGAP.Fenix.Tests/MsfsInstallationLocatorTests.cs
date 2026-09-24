using FSGAP.Fenix.Msfs;
using FSGAP.Fenix.Tests.Fixtures;

namespace FSGAP.Fenix.Tests;

public class MsfsInstallationLocatorTests
{
    [Fact]
    public void Store_installation_is_found_from_user_cfg()
    {
        using var fixture = new FenixInstallFixture();
        var (local, roaming) = Profile(fixture);
        WriteUserCfg(Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache"), fixture.PackagesPath);
        fixture.PackageRoot("Official2024");

        var installation = MsfsInstallationLocator.Locate(local, roaming);

        Assert.NotNull(installation);
        Assert.Equal(fixture.PackagesPath, installation.InstalledPackagesPath);
        Assert.Equal([fixture.Community, Path.Combine(fixture.PackagesPath, "Official2024")], installation.PackageRoots);
    }

    [Fact]
    public void Steam_installation_is_found_from_user_cfg()
    {
        using var fixture = new FenixInstallFixture();
        var (local, roaming) = Profile(fixture);
        WriteUserCfg(Path.Combine(roaming, "Microsoft Flight Simulator 2024"), fixture.PackagesPath);

        Assert.Equal(fixture.PackagesPath, MsfsInstallationLocator.Locate(local, roaming)?.InstalledPackagesPath);
    }

    [Fact]
    public void A_renamed_store_package_is_found_by_the_bounded_fallback()
    {
        using var fixture = new FenixInstallFixture();
        var (local, roaming) = Profile(fixture);
        WriteUserCfg(Path.Combine(local, "Packages", "Microsoft.FlightSimulator2024Next_abc", "LocalCache"), fixture.PackagesPath);

        Assert.Equal(fixture.PackagesPath, MsfsInstallationLocator.Locate(local, roaming)?.InstalledPackagesPath);
    }

    [Fact]
    public void No_user_cfg_or_a_missing_packages_folder_means_not_installed()
    {
        using var fixture = new FenixInstallFixture();
        var (local, roaming) = Profile(fixture);
        Assert.Null(MsfsInstallationLocator.Locate(local, roaming));

        WriteUserCfg(Path.Combine(roaming, "Microsoft Flight Simulator 2024"), Path.Combine(fixture.Root, "does-not-exist"));
        Assert.Null(MsfsInstallationLocator.Locate(local, roaming));
    }

    private static (string Local, string Roaming) Profile(FenixInstallFixture fixture)
    {
        var local = Path.Combine(fixture.Root, "profile", "Local");
        var roaming = Path.Combine(fixture.Root, "profile", "Roaming");
        Directory.CreateDirectory(Path.Combine(local, "Packages"));
        Directory.CreateDirectory(roaming);
        return (local, roaming);
    }

    private static void WriteUserCfg(string folder, string installedPackagesPath)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "UserCfg.opt"), ["{Graphics", "}", $"InstalledPackagesPath \"{installedPackagesPath}\""]);
    }
}
