using FSGAP.Abstractions.Internal;

namespace FSGAP.Abstractions.Aircraft;

/// <summary>Outcome of <see cref="IInstalledAircraftCatalog.RefreshAsync"/>.</summary>
public sealed record CatalogScanResult
{
    private readonly IReadOnlyList<string> _errors = [];

    /// <summary>Number of installed aircraft in the catalog after the scan.</summary>
    public required int AircraftCount { get; init; }

    /// <summary>When the scan completed.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Human-readable descriptions of items that could not be read. A broken item never aborts a scan. The assigned
    /// collection is copied.
    /// </summary>
    public IReadOnlyList<string> Errors
    {
        get => _errors;
        init => _errors = ReadOnlyCopy.Of(value, nameof(Errors));
    }
}
