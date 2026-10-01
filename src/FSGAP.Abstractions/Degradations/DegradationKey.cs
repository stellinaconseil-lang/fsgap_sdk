using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FSGAP.Abstractions.Degradations;

/// <summary>
/// Normalized, vendor-neutral identity of a controlled degradation (for example <c>electrical.generator.1.forced-off</c>).
/// </summary>
/// <remarks>
/// <para>
/// A key names what FSGAP deliberately <em>does</em> to the aircraft (a system control forced into a degraded
/// configuration), never a component failure: keys end with the action (<c>forced-off</c>), not with <c>failed</c>.
/// Providers map keys to their own controls internally; those never appear in the public API.
/// </para>
/// <para>
/// Same format as <see cref="Failures.FailureKey"/> (2 to 8 lower-case dotted segments, 128 characters at most), but a
/// distinct type: a degradation key is never accepted where a failure key is expected, and the reverse.
/// </para>
/// </remarks>
public sealed partial record DegradationKey
{
    /// <summary>Maximum length of a key.</summary>
    public const int MaxLength = 128;

    private DegradationKey(string value) => Value = value;

    /// <summary>The key text.</summary>
    public string Value { get; }

    /// <summary>Parses a key.</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not a valid key.</exception>
    public static DegradationKey Parse(string value) =>
        TryParse(value, out var key)
            ? key
            : throw new FormatException($"'{value}' is not a valid degradation key (expected lower-case dotted segments, e.g. 'electrical.generator.1.forced-off').");

    /// <summary>Tries to parse a key.</summary>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid key.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out DegradationKey? key)
    {
        key = value is { Length: > 0 and <= MaxLength } && KeyPattern().IsMatch(value) ? new DegradationKey(value) : null;
        return key is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*){1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
