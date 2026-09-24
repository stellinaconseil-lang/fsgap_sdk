using System.Text.RegularExpressions;

namespace FSGAP.Fenix.Detection;

/// <summary>
/// The Fenix naming rules, confirmed on real installations and in live MSFS 2024 sessions.
/// </summary>
/// <remarks>
/// <para>
/// A Fenix aircraft title reads <c>Fenix&lt;model&gt; &lt;engine&gt; &lt;wingtip&gt;[ &lt;cabin&gt;]</c>, for example
/// <c>FenixA321 IAE WF SC</c>. These are exactly the titles of the Fenix preset variants
/// (<c>presets\fnx\FNX_321_IAE_WF_SC</c>). A livery declares the same facts in
/// <c>livery.cfg [SELECTION] required_tags</c>, for example <c>"A321,IAE,WF,Cabin_A321_SingleClass"</c>.
/// </para>
/// <para>Token meanings, confirmed by the audited applications and the installed presets:</para>
/// <list type="bullet">
/// <item><description><c>CFM</c> / <c>IAE</c>: engine family.</description></item>
/// <item><description><c>SL</c>: sharklets. <c>WF</c>: wingtip fence.</description></item>
/// <item><description>
/// <c>SD</c>/<c>HD</c> (A319) and <c>SC</c>/<c>TC</c> (A321): cabin layout. These are recognized but not exposed,
/// because no consumer needs them yet.
/// </description></item>
/// <item><description>The tag <c>Disabled</c> marks a livery MSFS never offers for selection.</description></item>
/// </list>
/// <para>
/// Tokens are matched exactly (whole title words, whole tags), never as substrings. If a source names two engines
/// or two wingtips, the result is unknown rather than a guess.
/// </para>
/// </remarks>
internal static partial class FenixTokens
{
    private const string DisabledTag = "Disabled";

    /// <summary>
    /// Recognizes a Fenix model in free text: "Fenix", up to 4 non-digits (none in <c>FenixA320</c>, a space in
    /// <c>Fenix A320</c>), then 319, 320 or 321 not followed by another digit.
    /// </summary>
    public static FenixModel? MatchModel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ModelPattern().Match(text);
        return match.Success ? ToModel(match.Groups["model"].Value) : null;
    }

    /// <summary>Whole words of a title (letters and digits).</summary>
    public static IReadOnlyList<string> TitleWords(string? title) =>
        string.IsNullOrWhiteSpace(title) ? [] : WordSeparator().Split(title).Where(w => w.Length > 0).ToArray();

    /// <summary>The comma-separated tags of <c>required_tags</c>.</summary>
    public static IReadOnlyList<string> RequiredTags(string? requiredTags) =>
        string.IsNullOrWhiteSpace(requiredTags)
            ? []
            : requiredTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The model named by an exact <c>A319</c>/<c>A320</c>/<c>A321</c> tag; null if none or several.</summary>
    public static FenixModel? ModelFromTags(IReadOnlyList<string> tags) =>
        Single(tags.Select(t => t.ToUpperInvariant() switch
        {
            "A319" => FenixModel.A319,
            "A320" => FenixModel.A320,
            "A321" => FenixModel.A321,
            _ => (FenixModel?)null,
        }));

    /// <summary>The engine named by an exact token; null if none or contradictory.</summary>
    public static FenixEngine? Engine(IReadOnlyList<string> tokens) =>
        Single(tokens.Select(t => t.ToUpperInvariant() switch
        {
            "CFM" => FenixEngine.Cfm,
            "IAE" => FenixEngine.Iae,
            _ => (FenixEngine?)null,
        }));

    /// <summary>The wingtip named by an exact token; null if none or contradictory.</summary>
    public static FenixWingtip? Wingtip(IReadOnlyList<string> tokens) =>
        Single(tokens.Select(t => t.ToUpperInvariant() switch
        {
            "SL" or "SHARKLET" or "SHARKLETS" => FenixWingtip.Sharklets,
            "WF" or "WTF" or "WINGTIP_FENCE" => FenixWingtip.WingtipFence,
            _ => (FenixWingtip?)null,
        }));

    /// <summary>Whether the tags mark a livery MSFS never offers for selection.</summary>
    public static bool IsDisabled(IReadOnlyList<string> tags) =>
        tags.Any(t => string.Equals(t, DisabledTag, StringComparison.OrdinalIgnoreCase));

    private static T? Single<T>(IEnumerable<T?> values)
        where T : struct
    {
        var distinct = values.Where(v => v.HasValue).Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static FenixModel? ToModel(string digits) => digits switch
    {
        "319" => FenixModel.A319,
        "320" => FenixModel.A320,
        "321" => FenixModel.A321,
        _ => null,
    };

    [GeneratedRegex(@"Fenix\D{0,4}(?<model>319|320|321)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();

    [GeneratedRegex(@"[^A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordSeparator();
}
