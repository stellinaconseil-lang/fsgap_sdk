using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FSGAP.Abstractions.Degradations;

/// <summary>
/// Immutable set of the controlled degradations a provider supports, indexed by <see cref="DegradationKey"/>. Each
/// provider builds its own catalog.
/// </summary>
public sealed class DegradationCatalog : IReadOnlyCollection<DegradationDescriptor>
{
    private readonly ImmutableArray<DegradationDescriptor> _descriptors;
    private readonly FrozenDictionary<DegradationKey, DegradationDescriptor> _byKey;

    /// <summary>Creates a catalog.</summary>
    /// <param name="descriptors">Descriptors, in display order.</param>
    /// <exception cref="ArgumentException">A descriptor is null, or two descriptors share a key.</exception>
    public DegradationCatalog(IEnumerable<DegradationDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _descriptors = [.. descriptors];
        var byKey = new Dictionary<DegradationKey, DegradationDescriptor>(_descriptors.Length);
        foreach (var descriptor in _descriptors)
        {
            if (descriptor is null)
            {
                throw new ArgumentException("A degradation catalog cannot contain null descriptors.", nameof(descriptors));
            }

            if (!byKey.TryAdd(descriptor.Key, descriptor))
            {
                throw new ArgumentException($"Duplicate degradation key '{descriptor.Key}'.", nameof(descriptors));
            }
        }

        _byKey = byKey.ToFrozenDictionary();
    }

    /// <summary>An empty catalog.</summary>
    public static DegradationCatalog Empty { get; } = new([]);

    /// <inheritdoc />
    public int Count => _descriptors.Length;

    /// <summary>Whether the catalog defines <paramref name="key"/>.</summary>
    public bool Contains(DegradationKey key) => _byKey.ContainsKey(key);

    /// <summary>Gets the descriptor of <paramref name="key"/>.</summary>
    public bool TryGet(DegradationKey key, [MaybeNullWhen(false)] out DegradationDescriptor descriptor) =>
        _byKey.TryGetValue(key, out descriptor);

    /// <inheritdoc />
    public IEnumerator<DegradationDescriptor> GetEnumerator() => ((IEnumerable<DegradationDescriptor>)_descriptors).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
