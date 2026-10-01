using FSGAP.Abstractions.Degradations;

namespace FSGAP.Abstractions.Capabilities;

/// <summary>
/// Controlled-degradation operations a provider supports, independent from <see cref="FailureCapabilities"/>. A consumer
/// checks these capabilities, never the aircraft vendor, before relying on degradations.
/// </summary>
public sealed record DegradationCapabilities
{
    /// <summary>No degradation capability.</summary>
    public static DegradationCapabilities None { get; } = new();

    /// <summary>Degradations the provider knows, with the operations it supports for each.</summary>
    public DegradationCatalog Catalog { get; init; } = DegradationCatalog.Empty;

    /// <summary>
    /// How many degradations this session may hold applied at the same time; 0 when none can be applied. Combinations are
    /// only allowed once they have been qualified.
    /// </summary>
    public int MaxActive { get; init; }

    /// <summary>Whether <paramref name="key"/> can be applied.</summary>
    public bool CanApply(DegradationKey key) => Allows(key, DegradationOperations.Apply);

    /// <summary>Whether <paramref name="key"/> can be restored.</summary>
    public bool CanRestore(DegradationKey key) => Allows(key, DegradationOperations.Restore);

    /// <summary>Whether the state of <paramref name="key"/> can be read.</summary>
    public bool CanReadState(DegradationKey key) => Allows(key, DegradationOperations.ReadState);

    private bool Allows(DegradationKey key, DegradationOperations operation)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Catalog.TryGet(key, out var descriptor) && descriptor.Operations.HasFlag(operation);
    }
}
