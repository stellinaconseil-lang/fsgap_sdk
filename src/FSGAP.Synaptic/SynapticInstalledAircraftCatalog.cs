using System.Security.Cryptography;
using System.Text;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Msfs;
using FSGAP.Synaptic.Catalog;
using FSGAP.Synaptic.Detection;
using FSGAP.Synaptic.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic;

/// <summary>
/// The Synaptic A220-300 liveries installed on this machine, one entry per logical livery, built from the MSFS package
/// folders (<see cref="SynapticLiveryDiskScanner"/>), the simulator's own livery enumeration
/// (<see cref="IInstalledLiveryService"/>) when it is connected, and what was learned while liveries were flown.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two sources, merged.</b> The disk scan (W3-RC3C-B1) reads every installed A220-300 <c>livery.cfg</c> — plain files and
/// the uncompressed entries of the streamed packages' unencrypted archives — with no simulator and no livery ever loaded:
/// it gives the name, the folder and the registration the livery DECLARES. The simulator enumeration (BLOCK 10A.5) lists
/// every livery the simulator can load, Marketplace content included, but with no folder or registration. Either source
/// alone is enough; neither is required. No AI aircraft is ever created to probe a livery.
/// </para>
/// <para>
/// <b>One entry per logical livery.</b> Rows are kept only for the two Synaptic preset titles, and merged by livery name:
/// the cabin and no-cabin presets of the same livery are one aircraft, not two. Rows without a livery name (the preset's
/// unnamed default) are not listed. Liveries without a registration (house, white) are listed with
/// <c>Registration = null</c>.
/// </para>
/// <para>
/// <b>Registration.</b> Never read from the enumeration (it has none). The disk scan records the registration a livery
/// declares (<see cref="RegistrationSource.Authoritative"/>). When the user loads a livery, the provider hands the descriptor
/// to <see cref="Learn"/>, which records its livery folder and resolves the registration conservatively
/// (<c>RegistrationResolver</c>: authoritative — the declared one —, observed only when corroborated, derived from the folder,
/// cached, or none). What was learned is cached under <c>FsgapOptions.DataDirectory/synaptic/</c> and keeps its original source.
/// </para>
/// <para>Thread-safe. Lookups serve the last snapshot and never trigger a refresh.</para>
/// </remarks>
public sealed class SynapticInstalledAircraftCatalog : IInstalledAircraftCatalog
{
    /// <summary>The package roots the disk scan reads: the four standard ones plus StreamedPackages (where the A220 lives).</summary>
    internal static readonly IReadOnlyList<string> PackageRootNames =
        ["Community", "Community2024", "Official2024", "Official2020", MsfsInstallationLocator.StreamedPackagesRootName];

    internal const string MsfsNotFound = "MSFS 2024 installation not found (no UserCfg.opt with a valid InstalledPackagesPath).";

    private readonly IInstalledLiveryService _liveries;
    private readonly Func<IReadOnlyList<string>?> _packageRoots;
    private readonly LearnedLiveryCache _cache;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _gate = new();
    private Dictionary<string, LearnedLivery>? _entries;

    /// <summary>Creates the catalog. Nothing is read until the first lookup, learn or refresh.</summary>
    /// <param name="options">Host options; the cache lives under <see cref="FsgapOptions.DataDirectory"/>.</param>
    /// <param name="liveries">The simulator's livery enumeration, typically the <c>SimConnectSimulator</c>.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock for timestamps; <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="packageRoots">MSFS package folders to scan; <see langword="null"/> locates the MSFS installation (tests pass their own, an empty list disables the disk scan).</param>
    /// <exception cref="ArgumentException"><paramref name="options"/> is invalid.</exception>
    public SynapticInstalledAircraftCatalog(
        FsgapOptions options,
        IInstalledLiveryService liveries,
        ILogger<SynapticInstalledAircraftCatalog>? logger = null,
        TimeProvider? timeProvider = null,
        IReadOnlyList<string>? packageRoots = null)
    {
        _packageRoots = packageRoots is null
            ? () => MsfsInstallationLocator.Locate() is { } msfs ? MsfsInstallationLocator.FindPackageRoots(msfs.InstalledPackagesPath, PackageRootNames) : null
            : () => packageRoots;
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(liveries);
        options.Validate();
        _liveries = liveries;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _cache = new LearnedLiveryCache(Path.Combine(options.DataDirectory, "synaptic", "installed-aircraft.json"), _logger);
    }

    /// <summary>Path of the cache file.</summary>
    internal string CachePath => _cache.Path;

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledAircraft>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<InstalledAircraft>>(Snapshot().Select(ToInstalledAircraft).ToArray());
    }

    /// <inheritdoc />
    public Task<InstalledAircraft?> FindByLiveryFolderAsync(string liveryFolder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveryFolder);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = Snapshot().Where(l => string.Equals(l.LiveryFolder, liveryFolder.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult(matches.Length == 1 ? ToInstalledAircraft(matches[0]) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledAircraft>> FindByRegistrationAsync(string registration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registration);
        cancellationToken.ThrowIfCancellationRequested();
        var key = RegistrationResolver.Key(registration);
        return Task.FromResult<IReadOnlyList<InstalledAircraft>>(Snapshot()
            .Where(l => l.Registration is not null && RegistrationResolver.Key(l.Registration) == key)
            .Select(ToInstalledAircraft)
            .ToArray());
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One disk scan of the MSFS package folders (no simulator needed) and, when the simulator is connected, one livery
    /// enumeration. The result is their union, merged by livery name: the disk adds the folder and the declared
    /// registration (a declared registration supersedes a derived or observed one; a learned folder is kept), the
    /// enumeration adds the presets. Learned folders and registrations survive a refresh.
    /// </para>
    /// <para>
    /// A livery disappears only when the enumeration answered and neither source lists it any more. With the simulator
    /// unavailable, the disk result is merged INTO the previous content (nothing is lost offline). When neither source
    /// answers (simulator unavailable and no installation found, or nothing found on disk), the previous content is kept
    /// and the reasons are reported in <see cref="CatalogScanResult.Errors"/>. A cancellation changes nothing.
    /// </para>
    /// </remarks>
    public async Task<CatalogScanResult> RefreshAsync(IProgress<CatalogScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            progress?.Report(new CatalogScanProgress(CatalogScanPhase.Discovering, 0, null));

            DiskLiveryScan? disk = null;
            string? diskError = null;
            var roots = _packageRoots();
            if (roots is null)
            {
                diskError = MsfsNotFound;
            }
            else if (roots.Count > 0)
            {
                disk = await Task.Run(() => SynapticLiveryDiskScanner.Scan(roots, cancellationToken), cancellationToken).ConfigureAwait(false);
                foreach (var reason in disk.Skipped)
                {
                    _logger.LogInformation("Synaptic livery not listed: {Reason}", reason);
                }
            }

            IReadOnlyList<InstalledLivery>? rows = null;
            string? simulatorError = null;
            try
            {
                rows = await _liveries.GetInstalledAircraftLiveriesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SimulatorServiceException ex)
            {
                simulatorError = ex.Message;
                _logger.LogInformation(ex, "The simulator livery enumeration is unavailable; using the disk scan only");
            }

            var diskLiveries = disk?.Liveries ?? [];
            if (rows is null && diskLiveries.Count == 0)
            {
                _logger.LogWarning("The Synaptic livery catalog could not be refreshed (no simulator, nothing on disk); keeping the previous content");
                return new CatalogScanResult
                {
                    AircraftCount = Snapshot().Count,
                    CompletedAt = _time.GetUtcNow(),
                    Errors = new[] { simulatorError, diskError }.OfType<string>().ToArray(),
                };
            }

            var groups = rows is null ? [] : Group(rows);
            progress?.Report(new CatalogScanProgress(CatalogScanPhase.Scanning, groups.Count + diskLiveries.Count, groups.Count + diskLiveries.Count));
            IReadOnlyList<LearnedLivery> merged;
            lock (_gate)
            {
                var previous = LoadedEntries();
                var next = new Dictionary<string, LearnedLivery>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, presets) in groups)
                {
                    next[name] = previous.TryGetValue(name, out var known)
                        ? known with { Presets = presets }
                        : new LearnedLivery(name, presets, null, null, null, null);
                }

                foreach (var found in diskLiveries)
                {
                    var current = next.GetValueOrDefault(found.LiveryName)
                        ?? previous.GetValueOrDefault(found.LiveryName)
                        ?? new LearnedLivery(found.LiveryName, [], null, null, null, null);
                    next[found.LiveryName] = found.Registration is null
                        ? current with { LiveryFolder = current.LiveryFolder ?? found.LiveryFolder }
                        : current with
                        {
                            LiveryFolder = current.LiveryFolder ?? found.LiveryFolder,
                            Registration = found.Registration,
                            RegistrationSource = RegistrationSource.Authoritative,
                        };
                }

                if (rows is null)
                {
                    // Offline: the disk can only ADD to what is known (a streamed livery it cannot read must not vanish).
                    foreach (var (name, known) in previous)
                    {
                        next.TryAdd(name, known);
                    }
                }

                _entries = next;
                merged = Ordered(next);
            }

            _cache.Save(merged, _time.GetUtcNow());
            return new CatalogScanResult { AircraftCount = merged.Count, CompletedAt = _time.GetUtcNow() };
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Records what the loaded Synaptic A220 tells about its livery (folder, registration) and returns the resolved
    /// registration. Called by the provider when a session is attached; passive (reads nothing from the simulator).
    /// </summary>
    internal ResolvedRegistration? Learn(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        var name = aircraft.Livery?.Trim();
        IReadOnlyList<LearnedLivery> merged;
        ResolvedRegistration? resolved;
        lock (_gate)
        {
            var entries = LoadedEntries();
            var known = name is null ? null : entries.GetValueOrDefault(name);
            var cached = known is { Registration: { } r, RegistrationSource: { } s } ? new ResolvedRegistration(r, s) : null;
            // A registration the livery DECLARES (disk scan) stays authoritative: never downgraded to the folder-derived one.
            var declared = known is { RegistrationSource: RegistrationSource.Authoritative } ? known.Registration : null;
            resolved = RegistrationResolver.Resolve(declared, aircraft.LiveryFolder, aircraft.Registration, cached);
            if (name is null)
            {
                return resolved;
            }

            var title = aircraft.Title?.Trim();
            var presets = (known?.Presets ?? []).Concat(title is null ? [] : [title]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
            entries[name] = new LearnedLivery(
                known?.LiveryName ?? name,
                presets,
                aircraft.LiveryFolder?.Trim() ?? known?.LiveryFolder,
                resolved?.Registration,
                resolved?.Source,
                _time.GetUtcNow());
            merged = Ordered(entries);
        }

        _cache.Save(merged, _time.GetUtcNow());
        return resolved;
    }

    /// <summary>Synaptic preset rows grouped by livery name (case-insensitive), with their preset titles. Unnamed rows are skipped.</summary>
    internal static IReadOnlyList<(string LiveryName, IReadOnlyList<string> Presets)> Group(IReadOnlyList<InstalledLivery> rows) =>
        rows.Where(r => SynapticA220Recognizer.IsPresetTitle(r.AircraftTitle) && !string.IsNullOrWhiteSpace(r.LiveryName))
            .GroupBy(r => r.LiveryName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().LiveryName!.Trim(), (IReadOnlyList<string>)g.Select(r => r.AircraftTitle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(g => g.Item1, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Stable, opaque id of a logical livery: model + livery name (never the registration, which may be unknown).</summary>
    internal static string StableId(string liveryName)
    {
        var input = $"{SynapticIdentity.Model}|{liveryName.Trim()}".ToLowerInvariant();
        return "synaptic:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32].ToLowerInvariant();
    }

    private static InstalledAircraft ToInstalledAircraft(LearnedLivery livery) => new()
    {
        Id = StableId(livery.LiveryName),
        Identity = SynapticIdentity.Create(livery.Registration, livery.RegistrationSource, livery.LiveryName),
        LiveryFolder = livery.LiveryFolder,
        ModifiedAt = livery.LastObserved,
    };

    private static IReadOnlyList<LearnedLivery> Ordered(Dictionary<string, LearnedLivery> entries) =>
        entries.Values.OrderBy(l => l.LiveryName, StringComparer.OrdinalIgnoreCase).ToArray();

    private IReadOnlyList<LearnedLivery> Snapshot()
    {
        lock (_gate)
        {
            return Ordered(LoadedEntries());
        }
    }

    /// <summary>The entries, restored from the cache on first use. Caller holds <see cref="_gate"/>.</summary>
    private Dictionary<string, LearnedLivery> LoadedEntries() =>
        _entries ??= (_cache.TryLoad() ?? [])
            .GroupBy(l => l.LiveryName, StringComparer.OrdinalIgnoreCase) // a hand-edited cache may repeat a name
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
}
