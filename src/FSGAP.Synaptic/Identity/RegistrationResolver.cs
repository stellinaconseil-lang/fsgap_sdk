using System.Text.RegularExpressions;
using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Synaptic.Identity;

/// <summary>A registration and where it came from.</summary>
internal sealed record ResolvedRegistration(string Registration, RegistrationSource Source);

/// <summary>
/// Resolves the registration of a Synaptic A220 livery, conservatively, from the evidence FSGAP can see.
/// </summary>
/// <remarks>
/// <para>Precedence (first hit wins):</para>
/// <list type="number">
/// <item><description>
/// <b>Authoritative</b>: a registration declared by an accessible livery file (<c>livery.cfg atc_id</c>). The resolver
/// accepts it when given; no file scanner exists yet because no A220 livery was ever found as readable files (marketplace
/// liveries are packed).
/// </description></item>
/// <item><description>
/// <b>Observed</b>: the simulator's <c>ATC ID</c>, only when it is <i>coherent</i>: it must equal (ignoring case, spaces
/// and hyphens) the registration read from the current livery folder. BLOCK 10A-LIVE saw <c>ATC ID</c> stale from a
/// previous livery (<c>C-FFCO</c> on a Delta and an Air Baltic livery) or empty, so an uncorroborated value is never used.
/// </description></item>
/// <item><description><b>Derived</b>: the strict registration token of the livery folder (<see cref="ParseFolder"/>).</description></item>
/// <item><description><b>Cached</b>: an earlier result, with its original source unchanged.</description></item>
/// <item><description>Otherwise none (<see langword="null"/>): house and white liveries legitimately have no registration.</description></item>
/// </list>
/// </remarks>
internal static partial class RegistrationResolver
{
    /// <summary>ICAO nationality prefixes written with a hyphen (a pragmatic subset, not a registry).</summary>
    private static readonly HashSet<string> HyphenPrefixes = new(StringComparer.Ordinal)
    {
        "F", "G", "D", "C", "I", "B", "EC", "EI", "HB", "OO", "PH", "YL", "9H", "OE", "OY", "SE", "LN", "OH", "SP", "OK", "OM",
        "HA", "YR", "LZ", "SX", "TC", "UR", "ES", "LY", "S5", "9A", "CS", "VH", "ZK", "A6", "A7", "4X", "VT", "9V", "HS", "PK",
        "RP", "HZ", "SU", "CN", "5B", "TS", "EP", "VN", "ZS", "LV", "CC", "PR", "PP", "PT", "XA", "TF", "LX", "EW", "4L", "4K",
        "UK", "EK", "JY", "AP", "ET", "5Y", "3B", "D2", "7T",
    };

    public static ResolvedRegistration? Resolve(
        string? authoritative,
        string? liveryFolder,
        string? atcId,
        ResolvedRegistration? cached)
    {
        if (Normalize(authoritative) is { } declared)
        {
            return new ResolvedRegistration(declared, RegistrationSource.Authoritative);
        }

        var fromFolder = ParseFolder(liveryFolder);
        if (fromFolder is not null && Normalize(atcId) is { } reported && Key(reported) == Key(fromFolder))
        {
            return new ResolvedRegistration(fromFolder, RegistrationSource.Observed);
        }

        if (fromFolder is not null)
        {
            return new ResolvedRegistration(fromFolder, RegistrationSource.Derived);
        }

        return cached;
    }

    /// <summary>
    /// The registration token of a livery folder, or <see langword="null"/>. Whole tokens only (split on spaces,
    /// underscores and dots). Exactly one token must have a known registration shape: a known hyphenated nationality
    /// prefix (<c>F-HZUF</c>, <c>YL-CSM</c>, <c>HB-JCO</c>), a US N-number (<c>N324DU</c>), or an unhyphenated Korean or
    /// Japanese registration (<c>HL8315</c>). Unknown prefixes, several candidates or none give <see langword="null"/>.
    /// The token is reported as it is written, never "corrected" (<c>AIR CANADA G-GUAC</c> gives <c>G-GUAC</c>).
    /// </summary>
    public static string? ParseFolder(string? liveryFolder)
    {
        if (string.IsNullOrWhiteSpace(liveryFolder))
        {
            return null;
        }

        var candidates = TokenSeparator().Split(liveryFolder.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0 && IsRegistration(t))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>A registration for display: trimmed, upper-case; <see langword="null"/> when empty.</summary>
    public static string? Normalize(string? registration) =>
        string.IsNullOrWhiteSpace(registration) ? null : registration.Trim().ToUpperInvariant();

    /// <summary>Comparison key: letters and digits only, upper-case (<c>F-HZUF</c> = <c>fhzuf</c>).</summary>
    public static string Key(string registration) =>
        new(registration.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>True when <paramref name="value"/>, normalized, has a known registration shape (the same rule as
    /// <see cref="ParseFolder"/>). Used to accept a registration DECLARED by livery metadata, never to invent one.</summary>
    public static bool IsRegistrationShape(string? value) => Normalize(value) is { } n && IsRegistration(n);

    private static bool IsRegistration(string token) =>
        UsNNumber().IsMatch(token)
        || UnhyphenatedAsia().IsMatch(token)
        || (HyphenRegistration().Match(token) is { Success: true } m && HyphenPrefixes.Contains(m.Groups["prefix"].Value));

    [GeneratedRegex(@"[\s_.]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSeparator();

    [GeneratedRegex(@"^N[1-9][0-9]{0,4}[A-HJ-NP-Z]{0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsNNumber();

    [GeneratedRegex(@"^(HL[0-9]{4}|JA[0-9]{2,4}[A-Z]{0,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex UnhyphenatedAsia();

    [GeneratedRegex(@"^(?<prefix>[A-Z0-9]{1,2})-[A-Z0-9]{2,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex HyphenRegistration();
}
