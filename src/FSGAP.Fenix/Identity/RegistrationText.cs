using System.Text;

namespace FSGAP.Fenix.Identity;

/// <summary>Registration normalization: a display form that keeps the dash, and a lookup key that does not.</summary>
internal static class RegistrationText
{
    /// <summary>
    /// Display form: trimmed, surrounding quotes removed, internal whitespace collapsed, upper-cased. A dash is kept
    /// if present and never invented (<c>N123AB</c> stays <c>N123AB</c>). Null when empty.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = string.Join(' ', raw.Trim().Trim('"').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length == 0 ? null : value.ToUpperInvariant();
    }

    /// <summary>
    /// Lookup key: letters and digits only, upper-cased, so <c>F-GKXY</c>, <c>FGKXY</c> and <c>f gkxy</c> match
    /// (the <c>IInstalledAircraftCatalog</c> contract: case, spaces and hyphens are ignored).
    /// </summary>
    public static string? LookupKey(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>An operator ICAO designator: exactly three ASCII letters, upper-cased; anything else is rejected.</summary>
    public static string? OperatorIcao(string? raw)
    {
        var value = raw?.Trim();
        return value is { Length: 3 } && value.All(char.IsAsciiLetter) ? value.ToUpperInvariant() : null;
    }
}
