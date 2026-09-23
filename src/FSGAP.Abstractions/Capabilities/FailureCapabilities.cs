using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Capabilities;

/// <summary>Failure operations a provider supports, and the catalog of failures it knows.</summary>
public sealed record FailureCapabilities
{
    /// <summary>No failure capability.</summary>
    public static FailureCapabilities None { get; } = new();

    /// <summary>Whether <see cref="IFailureProvider.GetActiveFailuresAsync"/> is supported.</summary>
    public bool CanReadActiveFailures { get; init; }

    /// <summary>Failures the provider knows, with the operations it supports for each.</summary>
    public FailureCatalog Catalog { get; init; } = FailureCatalog.Empty;

    /// <summary>Whether at least one failure can be triggered.</summary>
    public bool CanTriggerAny => Catalog.Any(definition => definition.Operations.HasFlag(FailureOperations.Trigger));

    /// <summary>Whether at least one failure can be cleared.</summary>
    public bool CanClearAny => Catalog.Any(definition => definition.Operations.HasFlag(FailureOperations.Clear));

    /// <summary>Whether <paramref name="key"/> can be triggered on at least one of its targets.</summary>
    public bool CanTrigger(FailureKey key) => Allows(key, FailureOperations.Trigger);

    /// <summary>Whether <paramref name="key"/> can be cleared on at least one of its targets.</summary>
    public bool CanClear(FailureKey key) => Allows(key, FailureOperations.Clear);

    /// <summary>Whether <paramref name="command"/> can be triggered: known key, supported target, trigger allowed.</summary>
    public bool CanTrigger(FailureCommand command) => Allows(command, FailureOperations.Trigger);

    /// <summary>Whether <paramref name="command"/> can be cleared: known key, supported target, clear allowed.</summary>
    public bool CanClear(FailureCommand command) => Allows(command, FailureOperations.Clear);

    private bool Allows(FailureKey key, FailureOperations operation)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Catalog.TryGet(key, out var definition) && definition.Operations.HasFlag(operation);
    }

    private bool Allows(FailureCommand command, FailureOperations operation)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Catalog.TryGet(command.Key, out var definition)
            && definition.Operations.HasFlag(operation)
            && definition.Supports(command.Target);
    }
}
