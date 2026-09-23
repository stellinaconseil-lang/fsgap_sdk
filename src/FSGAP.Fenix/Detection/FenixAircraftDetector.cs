using System.Text.RegularExpressions;
using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Fenix.Detection;

/// <summary>
/// Recognizes a Fenix A319/A320/A321 from an <see cref="AircraftDescriptor"/>.
/// </summary>
/// <remarks>
/// Placeholder heuristic for BLOCK 0: the aircraft must mention "Fenix" in its title or package path, and its
/// model is read from the ICAO type, or else from an "A319"/"A320"/"A321" token in the title or model. BLOCK 1
/// replaces it with the detection rules already proven in FSHANGAR, once they have been audited.
/// </remarks>
internal static partial class FenixAircraftDetector
{
    private const string FenixMarker = "fenix";

    public static FenixModel? Detect(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);

        if (!Mentions(aircraft.Title, FenixMarker) && !Mentions(aircraft.PackagePath, FenixMarker))
        {
            return null;
        }

        return ParseModel(aircraft.IcaoType) ?? ParseModel(aircraft.Title) ?? ParseModel(aircraft.Model);
    }

    private static bool Mentions(string? text, string marker) =>
        text?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true;

    private static FenixModel? ParseModel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ModelToken().Match(text);
        return match.Success ? Enum.Parse<FenixModel>("A" + match.Groups["number"].Value) : null;
    }

    // "A319", "A320", "A321" not followed by another digit; may be glued to a preceding word ("FenixA320").
    [GeneratedRegex(@"A(?<number>319|320|321)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelToken();
}
