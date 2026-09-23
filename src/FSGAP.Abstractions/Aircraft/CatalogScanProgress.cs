namespace FSGAP.Abstractions.Aircraft;

/// <summary>Phase of an installed-aircraft scan.</summary>
public enum CatalogScanPhase
{
    /// <summary>Enumerating what to inspect; the total is not known yet.</summary>
    Discovering = 0,

    /// <summary>Inspecting each item; <see cref="CatalogScanProgress.Total"/> is known.</summary>
    Scanning,
}

/// <summary>Progress of <see cref="IInstalledAircraftCatalog.RefreshAsync"/>.</summary>
/// <param name="Phase">Current phase.</param>
/// <param name="Processed">Items processed so far.</param>
/// <param name="Total">Total items to process, or <see langword="null"/> while not known.</param>
public sealed record CatalogScanProgress(CatalogScanPhase Phase, int Processed, int? Total);
