using System.Collections.Frozen;
using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Capabilities;

/// <summary>Failure operations and failure types a provider supports.</summary>
public sealed record FailureCapabilities
{
    private readonly FrozenSet<FailureType> _triggerableTypes = FrozenSet<FailureType>.Empty;
    private readonly FrozenSet<FailureType> _clearableTypes = FrozenSet<FailureType>.Empty;

    /// <summary>No failure capability.</summary>
    public static FailureCapabilities None { get; } = new();

    /// <summary>Whether <see cref="IFailureProvider.GetActiveFailuresAsync"/> is supported.</summary>
    public bool CanReadActiveFailures { get; init; }

    /// <summary>Failure types that can be triggered. The assigned collection is copied.</summary>
    public IReadOnlySet<FailureType> TriggerableTypes
    {
        get => _triggerableTypes;
        init => _triggerableTypes = value.ToFrozenSet();
    }

    /// <summary>Failure types that can be cleared. The assigned collection is copied.</summary>
    public IReadOnlySet<FailureType> ClearableTypes
    {
        get => _clearableTypes;
        init => _clearableTypes = value.ToFrozenSet();
    }

    /// <summary>Whether at least one failure type can be triggered.</summary>
    public bool CanTriggerAny => _triggerableTypes.Count > 0;

    /// <summary>Whether at least one failure type can be cleared.</summary>
    public bool CanClearAny => _clearableTypes.Count > 0;

    /// <summary>Whether <paramref name="type"/> can be triggered.</summary>
    public bool CanTrigger(FailureType type) => _triggerableTypes.Contains(type);

    /// <summary>Whether <paramref name="type"/> can be cleared.</summary>
    public bool CanClear(FailureType type) => _clearableTypes.Contains(type);
}
