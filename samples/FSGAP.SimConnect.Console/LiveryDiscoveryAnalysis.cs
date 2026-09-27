// BLOCK 10A.5 — pure, deterministic analysis helpers of the livery / registration discovery experiment. Sample-only (never
// packaged); linked into FSGAP.SimConnect.Tests so they are unit-tested without MSFS. Nothing here is a production
// catalog or a production registration resolver.
using System.Text.RegularExpressions;

namespace FSGAP.SimConnect.Console;

/// <summary>How much a registration value can be trusted, by where it came from.</summary>
internal enum RegistrationSource
{
    /// <summary>No value.</summary>
    None,

    /// <summary>Parsed out of a name (livery folder): a guess with a confidence, never authoritative.</summary>
    Derived,

    /// <summary>Read from the simulator at run time (ATC ID) for a specific object.</summary>
    Observed,

    /// <summary>Declared by the livery's own metadata (livery.cfg atc_id).</summary>
    Authoritative,
}

/// <summary>Confidence of a derived registration.</summary>
internal enum ParserConfidence
{
    None,
    Low,
    High,
}

/// <summary>A registration candidate and why.</summary>
internal sealed record RegistrationCandidate(string? Registration, RegistrationSource Source, ParserConfidence Confidence, string Reason);

/// <summary>Outcome of an AI probe created with an empty tail number (BLOCK 10A.5 §13).</summary>
internal enum EmptyTailOutcome
{
    /// <summary>A: ATC ID equals the livery's registration (as derived from its folder).</summary>
    LiveryRegistration,

    /// <summary>B: ATC ID blank.</summary>
    Blank,

    /// <summary>C: ATC ID is some other value (generated or default).</summary>
    GeneratedOrDefault,

    /// <summary>D: ATC ID equals the user aircraft's value (inherited / stale).</summary>
    InheritedStale,

    /// <summary>E: the creation was rejected.</summary>
    CreationRejected,

    /// <summary>F: anything else (no read, no object).</summary>
    Other,
}

internal static partial class LiveryDiscoveryAnalysis
{
    /// <summary>The two Synaptic A220-300 preset titles observed live in BLOCK 10A-LIVE. Exact match only.</summary>
    internal static readonly IReadOnlyList<string> SynapticA220Titles = ["A220-300", "A220-300 - No Cabin"];

    /// <summary>ICAO nationality prefixes written with a hyphen (a pragmatic subset, not a registry).</summary>
    private static readonly HashSet<string> HyphenPrefixes = new(StringComparer.Ordinal)
    {
        "F", "G", "D", "C", "I", "B", "EC", "EI", "HB", "OO", "PH", "YL", "9H", "OE", "OY", "SE", "LN", "OH", "SP", "OK", "OM",
        "HA", "YR", "LZ", "SX", "TC", "UR", "ES", "LY", "S5", "9A", "CS", "VH", "ZK", "A6", "A7", "4X", "VT", "9V", "HS", "PK",
        "RP", "HZ", "SU", "CN", "5B", "TS", "EP", "VN", "ZS", "LV", "CC", "PR", "PP", "PT", "XA", "TF", "LX", "EW", "4L", "4K",
        "UK", "EK", "JY", "AP", "ET", "5Y", "3B", "D2", "7T",
    };

    internal static bool IsSynapticA220(string? aircraftTitle) =>
        aircraftTitle is not null && SynapticA220Titles.Any(t => string.Equals(t, aircraftTitle.Trim(), StringComparison.OrdinalIgnoreCase));

    internal sealed record EnumerationSummary(int Rows, int UniqueTitles, int UniqueLiveries);

    internal static EnumerationSummary Summarize(IReadOnlyList<(string Title, string Livery)> rows) => new(
        rows.Count,
        rows.Select(r => r.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        rows.Select(r => r.Livery).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    /// <summary>One livery name with the presets (titles) it appears under, and how many identical rows exist.</summary>
    internal sealed record LiveryGroup(string LiveryName, IReadOnlyList<string> Titles, int ExactDuplicateRows);

    /// <summary>
    /// Groups Synaptic A220 rows by livery name (case-insensitive): a livery offered under both presets appears once,
    /// with both titles. Rows repeated verbatim are counted, not dropped silently.
    /// </summary>
    internal static IReadOnlyList<LiveryGroup> GroupA220Liveries(IReadOnlyList<(string Title, string Livery)> rows) =>
        rows.Where(r => IsSynapticA220(r.Title) && !string.IsNullOrWhiteSpace(r.Livery))
            .GroupBy(r => r.Livery.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new LiveryGroup(
                g.First().Livery.Trim(),
                g.Select(r => r.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
                g.Count() - g.Select(r => r.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .OrderBy(g => g.LiveryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Audit-only registration candidate from a livery folder name. Whole tokens only (split on spaces, underscores and
    /// dots); a token must look like a registration: a known hyphenated nationality prefix (<c>F-HZUF</c>, <c>YL-CSM</c>,
    /// <c>9H-ABC</c>), a US N-number (<c>N324DU</c>) or an unhyphenated Korean or Japanese one (<c>HL8315</c>). One such token: High. A registration-shaped token with an unknown
    /// prefix, or several candidates: Low. Nothing: None. The source is always <see cref="RegistrationSource.Derived"/>.
    /// </summary>
    internal static RegistrationCandidate ParseRegistration(string? liveryFolder)
    {
        if (string.IsNullOrWhiteSpace(liveryFolder))
        {
            return new RegistrationCandidate(null, RegistrationSource.None, ParserConfidence.None, "no folder");
        }

        var tokens = TokenSeparator().Split(liveryFolder.Trim().ToUpperInvariant()).Where(t => t.Length > 0).ToArray();
        var known = new List<string>();
        var shaped = new List<string>();
        foreach (var token in tokens)
        {
            if (UsNNumber().IsMatch(token) || UnhyphenatedAsia().IsMatch(token))
            {
                known.Add(token);
            }
            else if (HyphenRegistration().Match(token) is { Success: true } m)
            {
                (HyphenPrefixes.Contains(m.Groups["prefix"].Value) ? known : shaped).Add(token);
            }
        }

        return (known.Count, shaped.Count) switch
        {
            (1, 0) => new RegistrationCandidate(known[0], RegistrationSource.Derived, ParserConfidence.High, "one registration-shaped token with a known prefix"),
            (0, 1) => new RegistrationCandidate(shaped[0], RegistrationSource.Derived, ParserConfidence.Low, "registration-shaped token, prefix not in the known list"),
            (0, 0) => new RegistrationCandidate(null, RegistrationSource.None, ParserConfidence.None, "no registration-shaped token"),
            _ => new RegistrationCandidate(known.Concat(shaped).First(), RegistrationSource.Derived, ParserConfidence.Low, "several registration-shaped tokens"),
        };
    }

    /// <summary>Classifies an empty-tail probe result (§13). Comparisons ignore case, spaces and hyphens.</summary>
    internal static EmptyTailOutcome ClassifyEmptyTail(bool created, bool identityRead, string? observedAtcId, string? folderCandidate, string? userAtcId)
    {
        if (!created)
        {
            return EmptyTailOutcome.CreationRejected;
        }

        if (!identityRead)
        {
            return EmptyTailOutcome.Other;
        }

        if (string.IsNullOrWhiteSpace(observedAtcId))
        {
            return EmptyTailOutcome.Blank;
        }

        if (folderCandidate is not null && Key(observedAtcId) == Key(folderCandidate))
        {
            return EmptyTailOutcome.LiveryRegistration;
        }

        return userAtcId is not null && Key(observedAtcId) == Key(userAtcId)
            ? EmptyTailOutcome.InheritedStale
            : EmptyTailOutcome.GeneratedOrDefault;
    }

    /// <summary>
    /// The representative liveries to probe first (§9): Air France, Air Baltic, Delta when present, one row each,
    /// preferring the lighter no-cabin preset; then the other groups in name order, up to <paramref name="max"/>.
    /// </summary>
    internal static IReadOnlyList<(string Title, string Livery)> ChooseProbeTargets(IReadOnlyList<LiveryGroup> groups, int max, bool preferCabin = false)
    {
        string[] preferred = ["air france", "baltic", "delta"];
        var ordered = preferred
            .Select(p => groups.FirstOrDefault(g => g.LiveryName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Where(g => g is not null)
            .Select(g => g!)
            .Concat(groups)
            .Distinct()
            .Take(Math.Max(0, max));
        return ordered
            .Select(g => (g.Titles.FirstOrDefault(t => t.EndsWith("No Cabin", StringComparison.OrdinalIgnoreCase) != preferCabin) ?? g.Titles[0], g.LiveryName))
            .ToArray();
    }

    private static string Key(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    [GeneratedRegex(@"[\s_.]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSeparator();

    [GeneratedRegex(@"^N[1-9][0-9]{0,4}[A-HJ-NP-Z]{0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsNNumber();

    /// <summary>Korea (HL + 4 digits) and Japan (JA + 3–4 digits and up to 2 letters) write registrations without a hyphen.</summary>
    [GeneratedRegex(@"^(HL[0-9]{4}|JA[0-9]{2,4}[A-Z]{0,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex UnhyphenatedAsia();

    [GeneratedRegex(@"^(?<prefix>[A-Z0-9]{1,2})-[A-Z0-9]{2,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex HyphenRegistration();
}
