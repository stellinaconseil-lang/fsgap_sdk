using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FSGAP.Abstractions.Failures;

/// <summary>
/// Normalized, vendor-neutral identity of a failure (for example <c>engine.fire</c> or <c>navigation.adf.1</c>).
/// </summary>
/// <remarks>
/// <para>
/// A key names a <em>semantic</em> failure. Providers map keys to their own technical failure identifiers
/// internally; those identifiers never appear in the public API and a key is never a vendor identifier.
/// </para>
/// <para>
/// Format: 2 to 8 dot-separated segments, 128 characters at most. Each segment is lower-case ASCII letters and
/// digits, optionally joined by single hyphens. The first segment starts with a letter. The format deliberately
/// rejects vendor-style identifiers such as <c>F_FIRE_FDU1</c> or <c>123</c>.
/// </para>
/// <para>
/// Keys are not defined here: each provider publishes the keys it supports through its
/// <see cref="FailureCatalog"/>.
/// </para>
/// </remarks>
public sealed partial record FailureKey
{
    /// <summary>Maximum length of a key.</summary>
    public const int MaxLength = 128;

    private FailureKey(string value) => Value = value;

    /// <summary>The key text.</summary>
    public string Value { get; }

    /// <summary>Parses a key.</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not a valid key.</exception>
    public static FailureKey Parse(string value) =>
        TryParse(value, out var key)
            ? key
            : throw new FormatException($"'{value}' is not a valid failure key (expected lower-case dotted segments, e.g. 'engine.fire').");

    /// <summary>Tries to parse a key.</summary>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid key.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out FailureKey? key)
    {
        key = value is { Length: > 0 and <= MaxLength } && KeyPattern().IsMatch(value) ? new FailureKey(value) : null;
        return key is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*){1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
