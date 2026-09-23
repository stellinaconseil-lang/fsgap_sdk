using System.Collections.Immutable;

namespace FSGAP.Abstractions.Internal;

/// <summary>
/// Defensive copies for collections exposed by public models, so that neither the caller who built a model nor a
/// consumer who reads it can mutate it afterwards.
/// </summary>
internal static class ReadOnlyCopy
{
    /// <summary>Copies <paramref name="source"/> into an immutable list.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public static IReadOnlyList<T> Of<T>(IEnumerable<T> source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        return source.ToImmutableArray();
    }
}
