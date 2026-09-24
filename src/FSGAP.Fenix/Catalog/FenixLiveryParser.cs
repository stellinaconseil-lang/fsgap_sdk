using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Fenix.Detection;
using FSGAP.Fenix.Identity;

namespace FSGAP.Fenix.Catalog;

/// <summary>Outcome of parsing one livery folder: an installed aircraft, or the reason it is not one.</summary>
internal sealed record LiveryParseResult(InstalledAircraft? Aircraft, string? SkipReason)
{
    public static LiveryParseResult Skipped(string reason) => new(null, reason);
}

/// <summary>
/// Reads one Fenix livery folder into an <see cref="InstalledAircraft"/>. Read-only.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes exist on real installations:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Fenix community liveries: only a <c>livery.cfg</c> with <c>[SELECTION] required_tags</c>, <c>[GENERAL] name</c>
/// and <c>[FLTSIM] atc_id / atc_airline / icao_airline</c>.
/// </description></item>
/// <item><description>The classic <c>aircraft.cfg</c> with a <c>[FLTSIM.n]</c> section.</description></item>
/// </list>
/// <para>Field sources, first hit wins:</para>
/// <list type="bullet">
/// <item><description>Model: exact A319/A320/A321 tag, then the Fenix title rule on the aircraft.cfg title.</description></item>
/// <item><description>Engine and wingtip: exact tags, then exact title words.</description></item>
/// <item><description>
/// Registration: livery.cfg <c>atc_id</c>, then aircraft.cfg <c>atc_id</c>, then a <c>registration</c> /
/// <c>tailNumber</c> / <c>registrationNumber</c> key in a livery JSON. It is never guessed from a folder or display
/// name.
/// </description></item>
/// <item><description>Operator ICAO: <c>icao_airline</c>, three letters only.</description></item>
/// <item><description>Display name: <c>ui_variation</c>, then <c>[GENERAL] name</c>.</description></item>
/// </list>
/// <para>
/// Unknown stays null. The model is never taken from package or path names: <c>fnx-aircraft-319-321</c> would
/// otherwise yield A319 for an A321 livery.
/// </para>
/// </remarks>
internal static class FenixLiveryParser
{
    private static readonly string[] MetadataRegistrationKeys = ["registration", "tailNumber", "registrationNumber"];

    public static LiveryParseResult Parse(string liveryFolder, FenixPackage package)
    {
        var sources = new List<string>();
        var aircraftCfg = ReadCfg(Path.Combine(liveryFolder, "aircraft.cfg"), sources);
        var fltSim = aircraftCfg?.SectionsStartingWith("FLTSIM").FirstOrDefault();
        var liveryCfg = ReadCfg(Path.Combine(liveryFolder, "livery.cfg"), sources);
        if (fltSim is null && liveryCfg is null)
        {
            return LiveryParseResult.Skipped("no livery definition ([FLTSIM] or livery.cfg)");
        }

        var tags = FenixTokens.RequiredTags(liveryCfg?.Get("SELECTION", "required_tags"));
        if (FenixTokens.IsDisabled(tags))
        {
            return LiveryParseResult.Skipped("livery disabled by required_tags");
        }

        string? FromAircraftCfg(string key) => fltSim is null ? null : aircraftCfg!.Get(fltSim, key);
        string? FromLiveryCfg(string key) => liveryCfg?.Get("FLTSIM", key) ?? liveryCfg?.Get(string.Empty, key);

        var title = FromAircraftCfg("title");
        var titleWords = FenixTokens.TitleWords(title);
        var model = FenixTokens.ModelFromTags(tags) ?? FenixTokens.MatchModel(title);
        var engine = FenixTokens.Engine(tags) ?? FenixTokens.Engine(titleWords);
        var wingtip = FenixTokens.Wingtip(tags) ?? FenixTokens.Wingtip(titleWords);
        var registration = RegistrationText.Normalize(
            FromLiveryCfg("atc_id") ?? FromAircraftCfg("atc_id") ?? ReadMetadataRegistration(liveryFolder, sources));
        var displayName = FromAircraftCfg("ui_variation") ?? liveryCfg?.Get("GENERAL", "name");
        var operatorIcao = RegistrationText.OperatorIcao(FromAircraftCfg("icao_airline") ?? FromLiveryCfg("icao_airline"));

        var relativePath = Path.GetRelativePath(package.PackagePath, liveryFolder);
        return new LiveryParseResult(
            new InstalledAircraft
            {
                Id = StableId(package.PackageName, relativePath),
                Identity = FenixIdentity.Create(model, engine, wingtip, registration, displayName, operatorIcao),
                LiveryFolder = Path.GetFileName(liveryFolder),
                PackageName = package.PackageName,
                ModifiedAt = sources.Count == 0 ? null : sources.Max(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero)),
            },
            null);
    }

    /// <summary>Stable across rescans while the package and its relative livery path stay the same.</summary>
    internal static string StableId(string packageName, string relativeLiveryPath)
    {
        var input = $"{packageName}|{relativeLiveryPath.Replace('\\', '/')}".ToLowerInvariant();
        return "fenix:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32].ToLowerInvariant();
    }

    private static IniConfigFile? ReadCfg(string path, List<string> sources)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var cfg = IniConfigFile.Parse(File.ReadLines(path));
        sources.Add(path);
        return cfg;
    }

    private static string? ReadMetadataRegistration(string liveryFolder, List<string> sources)
    {
        var conventional = Path.Combine(liveryFolder, "liveryData.json");
        var candidates = File.Exists(conventional) ? [conventional] : Directory.GetFiles(liveryFolder, "*.json");
        foreach (var path in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var value = document.RootElement.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.String
                        && MetadataRegistrationKeys.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    .Value;
                if (value.ValueKind == JsonValueKind.String)
                {
                    sources.Add(path);
                    return value.GetString();
                }
            }
            catch (JsonException)
            {
                // A malformed optional metadata file only means one source fewer.
            }
        }

        return null;
    }
}
