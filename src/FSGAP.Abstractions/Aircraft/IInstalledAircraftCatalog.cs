namespace FSGAP.Abstractions.Aircraft;

/// <summary>
/// Aircraft installed on this machine, read from their files by a provider: the local source of facts the simulator
/// does not report (registration, engine variant, available liveries...).
/// </summary>
/// <remarks>
/// Lookups read the last scan result (typically cached on disk) and never trigger a scan; call
/// <see cref="RefreshAsync"/> to rescan. Implementations must be thread-safe.
/// </remarks>
public interface IInstalledAircraftCatalog
{
    /// <summary>All installed aircraft known from the last scan.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<InstalledAircraft>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the entry whose <see cref="InstalledAircraft.LiveryFolder"/> matches, ignoring case.</summary>
    /// <param name="liveryFolder">Livery folder reported by the simulator (<see cref="AircraftDescriptor.LiveryFolder"/>).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null"/> when none matches.</returns>
    Task<InstalledAircraft?> FindByLiveryFolderAsync(string liveryFolder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the entries with this registration. The comparison ignores case, spaces and hyphens, so <c>F-GKXY</c>
    /// matches <c>FGKXY</c>. Several entries can share a registration (duplicate liveries).
    /// </summary>
    /// <param name="registration">Registration to look for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<InstalledAircraft>> FindByRegistrationAsync(string registration, CancellationToken cancellationToken = default);

    /// <summary>Rescans the installed files and replaces the catalog content.</summary>
    /// <param name="progress">Optional progress receiver.</param>
    /// <param name="cancellationToken">Cancels the scan; the previous content is kept.</param>
    Task<CatalogScanResult> RefreshAsync(IProgress<CatalogScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
