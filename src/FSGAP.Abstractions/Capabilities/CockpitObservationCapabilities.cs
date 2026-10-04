using FSGAP.Abstractions.Cockpit;

namespace FSGAP.Abstractions.Capabilities;

/// <summary>
/// What cockpit observations a provider offers for an aircraft. Defaults to "none": a provider lists only the keys
/// it actually reads, and a consumer checks a key is supported before relying on it.
/// </summary>
public sealed record CockpitObservationCapabilities
{
    /// <summary>No cockpit observation at all.</summary>
    public static CockpitObservationCapabilities None { get; } = new();

    /// <summary>Whether the provider can read any cockpit observation for this aircraft.</summary>
    public bool CanObserve { get; init; }

    /// <summary>The normalized keys the provider offers (the catalog). Empty when unsupported.</summary>
    public IReadOnlyList<CockpitObservationKey> Keys { get; init; } = [];

    /// <summary>Whether <paramref name="key"/> is in the catalog.</summary>
    public bool Supports(CockpitObservationKey key) => Keys.Contains(key);
}
