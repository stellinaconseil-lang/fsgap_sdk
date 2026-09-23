using FSGAP.Abstractions.Internal;

namespace FSGAP.Abstractions.Failures;

/// <summary>
/// A failure a provider knows about: its normalized key, how to present it, which system instances it applies to
/// and what the provider can do with it.
/// </summary>
/// <remarks>
/// Contains no vendor data. The mapping from <see cref="Key"/> to a vendor failure identifier lives inside the
/// provider.
/// </remarks>
public sealed record FailureDefinition
{
    private readonly string _displayName = string.Empty;
    private readonly IReadOnlyList<FailureTarget> _supportedTargets = [FailureTarget.Aircraft];

    /// <summary>Normalized identity of the failure.</summary>
    public required FailureKey Key { get; init; }

    /// <summary>Human-readable name, for display.</summary>
    /// <exception cref="ArgumentException">Set to an empty or white-space value.</exception>
    public required string DisplayName
    {
        get => _displayName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _displayName = value;
        }
    }

    /// <summary>Coarse classification, for grouping only.</summary>
    public FailureCategory Category { get; init; }

    /// <summary>
    /// System instances the failure can be applied to. Defaults to <see cref="FailureTarget.Aircraft"/> alone for
    /// failures that are not instance-specific. The assigned collection is copied; it must not be empty.
    /// </summary>
    public IReadOnlyList<FailureTarget> SupportedTargets
    {
        get => _supportedTargets;
        init
        {
            var copy = ReadOnlyCopy.Of(value, nameof(SupportedTargets));
            if (copy.Count == 0 || copy.Any(target => target is null))
            {
                throw new ArgumentException("A failure definition needs at least one non-null target.", nameof(SupportedTargets));
            }

            _supportedTargets = copy;
        }
    }

    /// <summary>Operations the provider supports for this failure. Defaults to <see cref="FailureOperations.None"/>.</summary>
    public FailureOperations Operations { get; init; }

    /// <summary>Whether this failure can be applied to <paramref name="target"/>.</summary>
    public bool Supports(FailureTarget target) => _supportedTargets.Contains(target);
}
