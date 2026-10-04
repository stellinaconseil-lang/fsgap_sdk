using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FSGAP.Abstractions.Cockpit;

/// <summary>
/// Normalized, vendor-neutral identity of an observable cockpit signal (for example <c>fire-test.engine-1</c> or
/// <c>adirs.ir-1.mode</c>).
/// </summary>
/// <remarks>
/// <para>
/// A key names a <em>semantic</em> cockpit observation. Providers map keys to their own technical variables
/// internally; those variable names (vendor L:Vars) never appear in the public API and a key is never a vendor
/// identifier.
/// </para>
/// <para>
/// Format: 2 to 8 dot-separated segments, 128 characters at most. Each segment is lower-case ASCII letters and
/// digits, optionally joined by single hyphens. The first segment starts with a letter. This deliberately rejects
/// vendor-style identifiers such as <c>L:S_OH_FIRE_ENG1_TEST</c>.
/// </para>
/// <para>
/// Keys are not defined here: each provider publishes the keys it supports through its cockpit observation
/// capability.
/// </para>
/// </remarks>
public sealed partial record CockpitObservationKey
{
    /// <summary>Maximum length of a key.</summary>
    public const int MaxLength = 128;

    private CockpitObservationKey(string value) => Value = value;

    /// <summary>The key text.</summary>
    public string Value { get; }

    /// <summary>Parses a key.</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not a valid key.</exception>
    public static CockpitObservationKey Parse(string value) =>
        TryParse(value, out var key)
            ? key
            : throw new FormatException(
                $"'{value}' is not a valid cockpit observation key (expected lower-case dotted segments, e.g. 'fire-test.engine-1').");

    /// <summary>Tries to parse a key.</summary>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid key.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out CockpitObservationKey? key)
    {
        if (value is not null && value.Length <= MaxLength && KeyPattern().IsMatch(value))
        {
            key = new CockpitObservationKey(value);
            return true;
        }

        key = null;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*){1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
