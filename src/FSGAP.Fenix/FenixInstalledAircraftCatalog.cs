using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Fenix.Catalog;
using FSGAP.Fenix.Msfs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Fenix;

/// <summary>
/// The Fenix A319/A320/A321 liveries installed on this machine, read from the MSFS 2024 package folders.
/// </summary>
/// <remarks>
/// <para>
/// The installed files are the source of truth; the catalog never writes to them. <see cref="RefreshAsync"/>
/// rescans them. Lookups serve the last scan and never trigger one. After the first use in a process, the last scan
/// is restored from a JSON cache under <c>FsgapOptions.DataDirectory/fenix/</c>, so lookups work before any rescan.
/// </para>
/// <para>
/// Thread-safe. A refresh builds a new immutable snapshot and swaps it in atomically, so readers keep the previous
/// one meanwhile. Concurrent refreshes run one after the other.
/// </para>
/// <para>
/// A cancelled refresh, or one that cannot find MSFS at all, keeps the previous content. A broken livery is skipped
/// and reported in <see cref="CatalogScanResult.Errors"/>, without failing the scan.
/// </para>
/// </remarks>
public sealed class FenixInstalledAircraftCatalog : IInstalledAircraftCatalog
{
    internal const string MsfsNotFound = "MSFS 2024 installation not found (no UserCfg.opt with a valid InstalledPackagesPath).";

    private readonly Func<IReadOnlyList<string>?> _packageRoots;
    private readonly CatalogCache _cache;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _loadGate = new();
    private CatalogSnapshot? _snapshot;

    /// <summary>Creates the catalog. Nothing is read until the first lookup or refresh.</summary>
    /// <param name="options">Host options; the cache lives under <see cref="FsgapOptions.DataDirectory"/>.</param>
    /// <param name="packageRoots">
    /// Package roots to scan (e.g. a Community folder chosen by the user), or <see langword="null"/> to locate
    /// MSFS 2024 automatically from <c>UserCfg.opt</c>.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock for scan timestamps; <see cref="TimeProvider.System"/> by default.</param>
    /// <exception cref="ArgumentException"><paramref name="options"/> is invalid.</exception>
    public FenixInstalledAircraftCatalog(
        FsgapOptions options,
        IReadOnlyList<string>? packageRoots = null,
        ILogger<FenixInstalledAircraftCatalog>? logger = null,
        TimeProvider? timeProvider = null)
        : this(options, packageRoots is null ? () => MsfsInstallationLocator.Locate()?.PackageRoots : () => packageRoots, logger, timeProvider)
    {
    }

    internal FenixInstalledAircraftCatalog(FsgapOptions options, Func<IReadOnlyList<string>?> packageRoots, ILogger? logger, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(packageRoots);
        options.Validate();
        _packageRoots = packageRoots;
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _cache = new CatalogCache(Path.Combine(options.DataDirectory, "fenix", "installed-aircraft.json"), _logger);
    }

    /// <summary>Path of the cache file.</summary>
    internal string CachePath => _cache.Path;

    private CatalogSnapshot Snapshot
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if (snapshot is not null)
            {
                return snapshot;
            }

            lock (_loadGate)
            {
                _snapshot ??= _cache.TryLoad() is { } cached ? new CatalogSnapshot(cached) : CatalogSnapshot.Empty;
                return _snapshot;
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledAircraft>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledAircraft>>(Snapshot.All);

    /// <inheritdoc />
    /// <remarks>Returns <see langword="null"/> as well when the folder name is ambiguous (installed twice).</remarks>
    public Task<InstalledAircraft?> FindByLiveryFolderAsync(string liveryFolder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(liveryFolder);
        return Task.FromResult(string.IsNullOrWhiteSpace(liveryFolder) ? null : Snapshot.FindByLiveryFolder(liveryFolder));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledAircraft>> FindByRegistrationAsync(string registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return Task.FromResult(Snapshot.FindByRegistration(registration));
    }

    /// <inheritdoc />
    public async Task<CatalogScanResult> RefreshAsync(IProgress<CatalogScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var outcome = await Task.Run(
                () => _packageRoots() is { } roots ? FenixLiveryScanner.Scan(roots, progress, cancellationToken) : null,
                cancellationToken).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            if (outcome is null)
            {
                _logger.LogWarning("Fenix livery scan skipped: {Reason}", MsfsNotFound);
                return new CatalogScanResult { AircraftCount = Snapshot.All.Length, CompletedAt = now, Errors = [MsfsNotFound] };
            }

            var snapshot = new CatalogSnapshot(outcome.Aircraft);
            var errors = outcome.Errors
                .Concat(snapshot.AmbiguousLiveryFolders.Select(f => $"Livery folder '{f}' is installed more than once; it cannot identify a livery."))
                .ToArray();
            Volatile.Write(ref _snapshot, snapshot);
            _cache.Save(snapshot.All, now);
            _logger.LogInformation(
                "Fenix livery scan: {Count} liveries, {Skipped} skipped, {Errors} errors",
                snapshot.All.Length,
                outcome.Skipped,
                errors.Length);
            return new CatalogScanResult { AircraftCount = snapshot.All.Length, CompletedAt = now, Errors = errors };
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
