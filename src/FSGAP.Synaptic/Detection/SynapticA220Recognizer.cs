using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Synaptic.Detection;

/// <summary>
/// Decides whether the aircraft the simulator reports is the Synaptic Simulations A220-300, from the generic descriptor
/// alone.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the one qualified live in BLOCK 10A-LIVE (three liveries, both presets). All three conditions are
/// required; none of them alone is specific:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>TITLE</c>, trimmed, is exactly one of the two Synaptic presets: <c>A220-300</c> or <c>A220-300 - No Cabin</c>;
/// </description></item>
/// <item><description><c>ATC MODEL</c> (descriptor <c>Model</c>) is exactly <c>A220-300</c>;</description></item>
/// <item><description><c>ATC TYPE</c> (descriptor <c>Manufacturer</c>) is exactly <c>223</c>.</description></item>
/// </list>
/// <para>
/// No descriptor field names the developer: neither "Synaptic" nor "iniBuilds" is required, because neither appears.
/// Titles that merely contain "A220" (<c>Asobo PassiveAircraft A220-300</c>, AI model libraries) never match, and neither
/// does any aircraft whose ATC strings are missing: without them there is no second, independent piece of evidence, so
/// the provider does not guess.
/// </para>
/// </remarks>
internal static class SynapticA220Recognizer
{
    /// <summary>The two preset titles of the Synaptic A220-300 (cabin and no-cabin).</summary>
    internal static readonly IReadOnlyList<string> PresetTitles = ["A220-300", "A220-300 - No Cabin"];

    /// <summary>The stock <c>atc_model</c> of both presets.</summary>
    internal const string AtcModel = "A220-300";

    /// <summary>The stock <c>atc_type</c> of both presets (a simulator string, not an ICAO designator).</summary>
    internal const string AtcType = "223";

    /// <summary>Whether <paramref name="aircraft"/> is the Synaptic A220-300.</summary>
    public static bool IsSynapticA220(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        return IsPresetTitle(aircraft.Title)
            && Equal(aircraft.Model, AtcModel)
            && Equal(aircraft.Manufacturer, AtcType);
    }

    /// <summary>Whether an aircraft title (from the descriptor or from the livery enumeration) is a Synaptic A220 preset.</summary>
    public static bool IsPresetTitle(string? title) => title is not null && PresetTitles.Any(t => Equal(title, t));

    private static bool Equal(string? value, string expected) =>
        value is not null && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
