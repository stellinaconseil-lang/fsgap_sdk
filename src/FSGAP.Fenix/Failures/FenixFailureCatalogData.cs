using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using FSGAP.Abstractions.Failures;

namespace FSGAP.Fenix.Failures;

/// <summary>One entry of the Fenix EFB's own failure list, as captured from the EFB. Internal: never exposed.</summary>
/// <param name="Id">Fenix failure id (e.g. <c>F_...</c>).</param>
/// <param name="Title">Fenix title; the EFB requires it verbatim in every command.</param>
/// <param name="Ata">ATA chapter number.</param>
/// <param name="AtaTitle">ATA chapter title as Fenix names it.</param>
/// <param name="Group">Fenix sub-group within the chapter.</param>
internal sealed record FenixRawFailure(string Id, string Title, int Ata, string AtaTitle, string Group);

/// <summary>A normalized failure: the public definition and the Fenix entry behind it.</summary>
internal sealed record FenixMappedFailure(FailureDefinition Definition, FenixRawFailure Raw)
{
    /// <summary>The single target the failure applies to.</summary>
    public FailureTarget Target => Definition.SupportedTargets[0];
}

/// <summary>
/// The Fenix failure catalog: the raw EFB list (384 entries) and the normalized subset, loaded from the two JSON
/// resources embedded in this assembly and validated once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw catalog</b> (<c>Resources/fenix-failure-catalog.json</c>): copied verbatim from the audited FSHANGAR
/// client, which captured it from the EFB's own <c>GET /fenix/failures/manual</c>. It provides the title each command
/// needs and the ATA chapter used to classify an active failure that has no key.
/// </para>
/// <para>
/// <b>Normalized subset</b> (<c>Resources/fenix-failure-mapping.json</c>): only the failures some consumer actually
/// uses, with a hand-written key, display name and target. Everything else stays internal on purpose: a key
/// generated from a Fenix title would be a fragile taxonomy that consumers would then depend on.
/// </para>
/// <para>
/// Loading fails fast (<see cref="InvalidOperationException"/>) on any inconsistency: duplicate id or key, a mapped
/// id missing from the raw catalog, an invalid key or target. Tests load it, so a bad resource never ships.
/// </para>
/// </remarks>
internal sealed partial class FenixFailureCatalogData
{
    private const string RawResource = "FSGAP.Fenix.Failures.fenix-failure-catalog.json";
    private const string MappingResource = "FSGAP.Fenix.Failures.fenix-failure-mapping.json";

    private static readonly Lazy<FenixFailureCatalogData> Embedded = new(LoadEmbedded);

    private readonly FrozenDictionary<string, FenixRawFailure> _rawById;
    private readonly FrozenDictionary<string, FenixMappedFailure> _mappedByRawId;
    private readonly FrozenDictionary<FailureKey, FenixMappedFailure> _mappedByKey;

    internal FenixFailureCatalogData(IReadOnlyList<FenixRawFailure> raw, IReadOnlyList<FenixMappedFailure> mapped)
    {
        Raw = raw;
        Mapped = mapped;
        _rawById = raw.ToFrozenDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        _mappedByRawId = mapped.ToFrozenDictionary(m => m.Raw.Id, StringComparer.OrdinalIgnoreCase);
        _mappedByKey = mapped.ToFrozenDictionary(m => m.Definition.Key);
        Catalog = new FailureCatalog(mapped.Select(m => m.Definition));
    }

    /// <summary>The catalog embedded in this assembly.</summary>
    internal static FenixFailureCatalogData Default => Embedded.Value;

    /// <summary>Every EFB failure, in catalog order.</summary>
    internal IReadOnlyList<FenixRawFailure> Raw { get; }

    /// <summary>The normalized failures.</summary>
    internal IReadOnlyList<FenixMappedFailure> Mapped { get; }

    /// <summary>The public catalog: normalized definitions only.</summary>
    internal FailureCatalog Catalog { get; }

    internal bool TryGetByKey(FailureKey key, out FenixMappedFailure mapped) => _mappedByKey.TryGetValue(key, out mapped!);

    internal bool TryGetMapped(string rawId, out FenixMappedFailure mapped) => _mappedByRawId.TryGetValue(rawId, out mapped!);

    internal bool TryGetRaw(string rawId, out FenixRawFailure raw) => _rawById.TryGetValue(rawId, out raw!);

    /// <summary>Category of a Fenix ATA chapter.</summary>
    internal static FailureCategory CategoryOf(int ata) => ata switch
    {
        21 => FailureCategory.AirConditioning,
        22 => FailureCategory.Autoflight,
        23 => FailureCategory.Communications,
        24 => FailureCategory.Electrical,
        26 => FailureCategory.Fire,
        27 => FailureCategory.FlightControls,
        28 => FailureCategory.Fuel,
        29 => FailureCategory.Hydraulic,
        30 => FailureCategory.IceAndRain,
        31 => FailureCategory.Instruments,
        32 => FailureCategory.LandingGear,
        34 => FailureCategory.Navigation,
        35 => FailureCategory.Oxygen,
        36 => FailureCategory.Pneumatic,
        49 => FailureCategory.Apu,
        52 => FailureCategory.Doors,
        70 or 80 => FailureCategory.Engine,
        _ => FailureCategory.Other,
    };

    /// <summary>Parses the two resources and validates them together.</summary>
    /// <exception cref="InvalidOperationException">The resources are inconsistent.</exception>
    internal static FenixFailureCatalogData Parse(Stream rawJson, Stream mappingJson)
    {
        var raw = ParseRaw(rawJson);
        var rawById = new Dictionary<string, FenixRawFailure>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw)
        {
            if (!rawById.TryAdd(entry.Id, entry))
            {
                throw Invalid($"duplicate Fenix id {entry.Id} in the raw catalog");
            }
        }

        var mapped = new List<FenixMappedFailure>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<FailureKey>();
        using var mapping = JsonDocument.Parse(mappingJson);
        foreach (var item in mapping.RootElement.GetProperty("failures").EnumerateArray())
        {
            var fenixId = RequiredString(item, "fenixId");
            if (!rawById.TryGetValue(fenixId, out var rawEntry))
            {
                throw Invalid($"mapped Fenix id {fenixId} is not in the raw catalog");
            }

            if (!seenIds.Add(fenixId))
            {
                throw Invalid($"Fenix id {fenixId} is mapped twice");
            }

            var keyText = RequiredString(item, "key");
            if (!FailureKey.TryParse(keyText, out var key))
            {
                throw Invalid($"'{keyText}' is not a valid failure key");
            }

            if (!seenKeys.Add(key))
            {
                throw Invalid($"key {key} is used twice");
            }

            var definition = new FailureDefinition
            {
                Key = key,
                DisplayName = RequiredString(item, "displayName"),
                Category = CategoryOf(rawEntry.Ata),
                SupportedTargets = [ParseTarget(item.GetProperty("target"))],

                // The EFB toggles any manual failure both ways, so every normalized failure supports both.
                Operations = FailureOperations.Trigger | FailureOperations.Clear,
            };
            mapped.Add(new FenixMappedFailure(definition, rawEntry));
        }

        return new FenixFailureCatalogData(raw, mapped);
    }

    private static FenixFailureCatalogData LoadEmbedded()
    {
        var assembly = typeof(FenixFailureCatalogData).Assembly;
        using var raw = assembly.GetManifestResourceStream(RawResource) ?? throw Invalid($"resource {RawResource} is missing");
        using var mapping = assembly.GetManifestResourceStream(MappingResource) ?? throw Invalid($"resource {MappingResource} is missing");
        return Parse(raw, mapping);
    }

    private static List<FenixRawFailure> ParseRaw(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = new List<FenixRawFailure>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = RequiredString(item, "id");
            var category = RequiredString(item, "category");
            var match = CategoryPattern().Match(category);
            if (!match.Success)
            {
                throw Invalid($"category '{category}' of {id} is not 'ATA <n> - <title> / <group>'");
            }

            entries.Add(new FenixRawFailure(
                id,
                RequiredString(item, "title"),
                int.Parse(match.Groups["ata"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["title"].Value.Trim(),
                match.Groups["group"].Value.Trim()));
        }

        return entries;
    }

    private static FailureTarget ParseTarget(JsonElement target)
    {
        var kind = RequiredString(target, "kind");
        return kind switch
        {
            "aircraft" => FailureTarget.Aircraft,
            "apu" => FailureTarget.Apu,
            "engine" => FailureTarget.Engine(target.GetProperty("index").GetInt32()),
            "inertialReference" => FailureTarget.InertialReference(target.GetProperty("index").GetInt32()),
            "fuelPump" => FailureTarget.FuelPump(RequiredString(target, "id")),
            "hydraulicSystem" => FailureTarget.HydraulicSystem(RequiredString(target, "id")),
            "electricalBus" => FailureTarget.ElectricalBus(RequiredString(target, "id")),
            _ => throw Invalid($"unknown target kind '{kind}'"),
        };
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw Invalid($"missing or empty '{property}'");

    private static InvalidOperationException Invalid(string reason) => new($"The Fenix failure catalog is invalid: {reason}.");

    [GeneratedRegex(@"^ATA (?<ata>\d+) - (?<title>.+?) / (?<group>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryPattern();
}
