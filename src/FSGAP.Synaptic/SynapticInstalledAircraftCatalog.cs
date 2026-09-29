using System.Security.Cryptography;
using System.Text;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Synaptic.Catalog;
using FSGAP.Synaptic.Detection;
using FSGAP.Synaptic.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic;

/// <summary>
/// The Synaptic A220-300 liveries the simulator can load, one entry per logical livery, built passively from the
/// simulator's own livery enumeration (<see cref="IInstalledLiveryService"/>) and from what was learned while liveries
/// were flown.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the enumeration.</b> The A220 and its liveries are marketplace content, unreadable on disk; the simulator
/// enumerates them all (BLOCK 10A.5). No AI aircraft is ever created to probe a livery.
/// </para>
/// <para>
/// <b>One entry per logical livery.</b> Rows are kept only for the two Synaptic preset titles, and merged by livery name:
/// the cabin and no-cabin presets of the same livery are one aircraft, not two. Rows without a livery name (the preset's
/// unnamed default) are not listed. Liveries without a registration (house, white) are listed with
/// <c>Registration = null</c>.
/// </para>
/// <para>
/// <b>Registration.</b> Never read from the enumeration (it has none). When the user loads a livery, the provider hands the
/// descriptor to <see cref="Learn"/>, which records its livery folder and resolves the registration conservatively
/// (<c>RegistrationResolver</c>: authoritative, observed only when corroborated, derived from the folder, cached,
/// or none). What was learned is cached under <c>FsgapOptions.DataDirectory/synaptic/</c> and keeps its original source.
/// </para>
/// <para>Thread-safe. Lookups serve the last snapshot and never trigger a refresh.</para>
/// </remarks>
public sealed class SynapticInstalledAircraftCatalog : IInstalledAircraftCatalog
{
    private readonly IInstalledLiveryService _liveries;
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
    /// <exception cref="ArgumentException"><paramref name="options"/> is invalid.</exception>
    public SynapticInstalledAircraftCatalog(
        FsgapOptions options,
        IInstalledLiveryService liveries,
        ILogger<SynapticInstalledAircraftCatalog>? logger = null,
        TimeProvider? timeProvider = null)
    {
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
    /// One livery enumeration. When the simulator cannot answer, the previous content is kept and the error is reported
    /// in <see cref="CatalogScanResult.Errors"/>. Learned folders and registrations survive a refresh; a livery no longer
    /// enumerated disappears.
    /// </remarks>
    public async Task<CatalogScanResult> RefreshAsync(IProgress<CatalogScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            progress?.Report(new CatalogScanProgress(CatalogScanPhase.Discovering, 0, null));
            IReadOnlyList<InstalledLivery> rows;
            try
            {
                rows = await _liveries.GetInstalledAircraftLiveriesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SimulatorServiceException ex)
            {
                _logger.LogWarning(ex, "The Synaptic livery catalog could not be refreshed; keeping the previous content");
                return new CatalogScanResult { AircraftCount = Snapshot().Count, CompletedAt = _time.GetUtcNow(), Errors = [ex.Message] };
            }

            var groups = Group(rows);
            progress?.Report(new CatalogScanProgress(CatalogScanPhase.Scanning, groups.Count, groups.Count));
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
            resolved = RegistrationResolver.Resolve(null, aircraft.LiveryFolder, aircraft.Registration, cached);
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
