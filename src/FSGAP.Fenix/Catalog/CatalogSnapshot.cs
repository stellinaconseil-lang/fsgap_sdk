using System.Collections.Frozen;
using System.Collections.Immutable;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Fenix.Identity;

namespace FSGAP.Fenix.Catalog;

/// <summary>
/// Immutable, indexed view of one scan result. A refresh builds a new snapshot and swaps it in atomically, so readers
/// never see a half-built catalog.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Livery folder lookup is case-insensitive: MSFS reported <c>AEE-SX-DNH-7F2F</c> for the folder <c>aee-sx-dnh-7f2f</c>.</description></item>
/// <item><description>
/// A livery folder name normally identifies one livery. If two installed liveries share it (case-insensitively),
/// the folder is <b>ambiguous</b>: lookup returns nothing rather than an arbitrary pick, and the refresh reports it.
/// </description></item>
/// <item><description>A registration may legitimately belong to several liveries; lookup returns all of them.</description></item>
/// </list>
/// </remarks>
internal sealed class CatalogSnapshot
{
    private readonly FrozenDictionary<string, InstalledAircraft> _byLiveryFolder;
    private readonly FrozenDictionary<string, ImmutableArray<InstalledAircraft>> _byRegistration;

    public CatalogSnapshot(IEnumerable<InstalledAircraft> aircraft)
    {
        All = [.. aircraft.OrderBy(a => a.LiveryFolder, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal)];

        var byFolder = All.Where(a => a.LiveryFolder is not null)
            .GroupBy(a => a.LiveryFolder!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AmbiguousLiveryFolders = byFolder.Where(g => g.Count() > 1).Select(g => g.Key).ToImmutableArray();
        _byLiveryFolder = byFolder.Where(g => g.Count() == 1)
            .ToFrozenDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        _byRegistration = All
            .Select(a => (Key: RegistrationText.LookupKey(a.Identity.Registration), Aircraft: a))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.Select(x => x.Aircraft).ToImmutableArray(), StringComparer.Ordinal);
    }

    public static CatalogSnapshot Empty { get; } = new([]);

    public ImmutableArray<InstalledAircraft> All { get; }

    public ImmutableArray<string> AmbiguousLiveryFolders { get; }

    public InstalledAircraft? FindByLiveryFolder(string liveryFolder) =>
        _byLiveryFolder.TryGetValue(liveryFolder.Trim(), out var aircraft) ? aircraft : null;

    public IReadOnlyList<InstalledAircraft> FindByRegistration(string registration) =>
        RegistrationText.LookupKey(registration) is { } key && _byRegistration.TryGetValue(key, out var matches)
            ? matches
            : [];
}
