using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FSGAP.Abstractions.Failures;

/// <summary>
/// Immutable set of the failures a provider supports, indexed by <see cref="FailureKey"/>. Each provider builds its
/// own catalog, so the set of keys is extensible without any change to FSGAP.Abstractions.
/// </summary>
public sealed class FailureCatalog : IReadOnlyCollection<FailureDefinition>
{
    private readonly ImmutableArray<FailureDefinition> _definitions;
    private readonly FrozenDictionary<FailureKey, FailureDefinition> _byKey;

    /// <summary>Creates a catalog.</summary>
    /// <param name="definitions">Definitions, in display order.</param>
    /// <exception cref="ArgumentException">A definition is null, or two definitions share a key.</exception>
    public FailureCatalog(IEnumerable<FailureDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = [.. definitions];

        var byKey = new Dictionary<FailureKey, FailureDefinition>(_definitions.Length);
        foreach (var definition in _definitions)
        {
            if (definition is null)
            {
                throw new ArgumentException("A failure catalog cannot contain null definitions.", nameof(definitions));
            }

            if (!byKey.TryAdd(definition.Key, definition))
            {
                throw new ArgumentException($"Duplicate failure key '{definition.Key}'.", nameof(definitions));
            }
        }

        _byKey = byKey.ToFrozenDictionary();
    }

    /// <summary>An empty catalog.</summary>
    public static FailureCatalog Empty { get; } = new([]);

    /// <inheritdoc />
    public int Count => _definitions.Length;

    /// <summary>Whether the catalog defines <paramref name="key"/>.</summary>
    public bool Contains(FailureKey key) => _byKey.ContainsKey(key);

    /// <summary>Gets the definition of <paramref name="key"/>.</summary>
    public bool TryGet(FailureKey key, [MaybeNullWhen(false)] out FailureDefinition definition) =>
        _byKey.TryGetValue(key, out definition);

    /// <inheritdoc />
    public IEnumerator<FailureDefinition> GetEnumerator() => ((IEnumerable<FailureDefinition>)_definitions).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
