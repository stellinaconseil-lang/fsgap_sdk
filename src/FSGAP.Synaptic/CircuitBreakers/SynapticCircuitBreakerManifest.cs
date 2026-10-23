using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Synaptic.Systems;

namespace FSGAP.Synaptic.CircuitBreakers;

/// <summary>Live qualification of one step of a breaker recipe.</summary>
internal enum SynapticBreakerQualification
{
    /// <summary>Proven in the Shadow lab on Synaptic A220 v1.0.11 (see <c>docs/qualification/synaptic-a220-circuit-breakers</c>).</summary>
    LabValidated,

    /// <summary>Not proven live yet.</summary>
    Unqualified,
}

/// <summary>
/// One cockpit circuit breaker of the Synaptic A220: its stable id, failure key, the documented cell that is its position
/// (1 = tripped / out, 0 = in) and the qualification of its mapping, reset (cell = 0) and direct trip (cell = 1).
/// </summary>
internal sealed record SynapticCircuitBreaker(
    string Id,
    FailureKey Key,
    string DisplayName,
    string System,
    SynapticControl Control,
    SynapticBreakerQualification Mapping,
    SynapticBreakerQualification Reset,
    SynapticBreakerQualification Trip,
    bool MasterCaution);

/// <summary>
/// The 55 Synaptic A220 circuit breakers, loaded from the manifest embedded in this assembly
/// (<c>CircuitBreakers/Resources/synaptic-a220-circuit-breakers.json</c>), itself generated from the lab evidence by
/// <c>tools/synaptic-cb-manifest/generate.py</c>; never typed by hand.
/// </summary>
/// <remarks>
/// Loading fails fast (<see cref="InvalidOperationException"/>) on any inconsistency: not exactly 55 breakers, a duplicate
/// id, key, cell or display name, a cell outside <c>L:A22X Circuit Breaker &lt;L|R&gt; &lt;A-I&gt;&lt;1-5&gt;</c>, a key
/// outside <c>electrical.circuit-breaker.*</c>, or an unknown qualification. Tests load it, so a bad manifest never ships.
/// </remarks>
internal sealed partial class SynapticCircuitBreakerManifest
{
    internal const int BreakerCount = 55;
    internal const string KeyPrefix = "electrical.circuit-breaker.";
    private const string Resource = "FSGAP.Synaptic.CircuitBreakers.synaptic-a220-circuit-breakers.json";
    private const string LocalUnit = "number";

    private static readonly Lazy<SynapticCircuitBreakerManifest> Loaded = new(LoadEmbedded);

    private readonly FrozenDictionary<string, SynapticCircuitBreaker> _byId;

    private SynapticCircuitBreakerManifest(string synapticVersion, IReadOnlyList<SynapticCircuitBreaker> breakers)
    {
        SynapticVersion = synapticVersion;
        Breakers = breakers;
        _byId = breakers.ToFrozenDictionary(b => b.Id, StringComparer.Ordinal);
    }

    /// <summary>The manifest embedded in this assembly.</summary>
    internal static SynapticCircuitBreakerManifest Embedded => Loaded.Value;

    /// <summary>The Synaptic A220 version the manifest was qualified on.</summary>
    internal string SynapticVersion { get; }

    /// <summary>The 55 breakers, in manifest order.</summary>
    internal IReadOnlyList<SynapticCircuitBreaker> Breakers { get; }

    internal SynapticCircuitBreaker? FindById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Parses and validates a manifest document.</summary>
    internal static SynapticCircuitBreakerManifest Parse(string json)
    {
        var document = JsonSerializer.Deserialize<ManifestDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("The circuit-breaker manifest is empty.");
        if (document.Schema != "fsgap.synaptic.circuit-breakers/1")
        {
            throw new InvalidOperationException($"Unexpected circuit-breaker manifest schema '{document.Schema}'.");
        }

        if (string.IsNullOrWhiteSpace(document.SynapticVersion))
        {
            throw new InvalidOperationException("The circuit-breaker manifest has no Synaptic version.");
        }

        var entries = document.Breakers ?? [];
        if (entries.Count != BreakerCount)
        {
            throw new InvalidOperationException($"The circuit-breaker manifest lists {entries.Count} breakers, expected {BreakerCount}.");
        }

        var breakers = entries.Select(ToBreaker).ToArray();
        RequireUnique(breakers, b => b.Id, "id");
        RequireUnique(breakers, b => b.Key.Value, "failure key");
        RequireUnique(breakers, b => b.Control.Variable.Name, "cell");
        RequireUnique(breakers, b => b.DisplayName, "display name");
        return new SynapticCircuitBreakerManifest(document.SynapticVersion.Trim(), breakers);
    }

    private static SynapticCircuitBreakerManifest LoadEmbedded()
    {
        using var stream = typeof(SynapticCircuitBreakerManifest).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded resource '{Resource}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static SynapticCircuitBreaker ToBreaker(ManifestBreaker entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) || !IdPattern().IsMatch(entry.Id))
        {
            throw new InvalidOperationException($"Invalid circuit-breaker id '{entry.Id}'.");
        }

        if (entry.Cell is null || !CellPattern().IsMatch(entry.Cell))
        {
            throw new InvalidOperationException($"{entry.Id}: invalid breaker cell '{entry.Cell}'.");
        }

        if (!FailureKey.TryParse(entry.FailureKey, out var key) || !key.Value.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{entry.Id}: invalid failure key '{entry.FailureKey}'.");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName) || entry.DisplayName.Contains("A22X", StringComparison.Ordinal) || entry.DisplayName.Contains("L:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{entry.Id}: invalid display name '{entry.DisplayName}'.");
        }

        // A breaker cell reads 1 when the breaker is out (tripped) and 0 when it is in: normal 0, degraded 1.
        var control = new SynapticControl($"circuit breaker {entry.Id}", new SimulatorVariable(entry.Cell, LocalUnit), 0, 1, SynapticControlEvidence.LabValidated);
        return new SynapticCircuitBreaker(
            entry.Id,
            key,
            entry.DisplayName,
            entry.System ?? string.Empty,
            control,
            Qualification(entry.Id, entry.Mapping),
            Qualification(entry.Id, entry.Reset),
            Qualification(entry.Id, entry.Trip),
            entry.MasterCaution);
    }

    private static SynapticBreakerQualification Qualification(string id, string? value) =>
        Enum.TryParse<SynapticBreakerQualification>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"{id}: unknown qualification '{value}'.");

    private static void RequireUnique(IEnumerable<SynapticCircuitBreaker> breakers, Func<SynapticCircuitBreaker, string> field, string name)
    {
        var duplicate = breakers.GroupBy(field, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate circuit-breaker {name} '{duplicate.Key}'.");
        }
    }

    [GeneratedRegex(@"^CB_[A-Z0-9]+(?:_[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^L:A22X Circuit Breaker [LR] [A-I][1-5]$", RegexOptions.CultureInvariant)]
    private static partial Regex CellPattern();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false, ReadCommentHandling = JsonCommentHandling.Disallow };

    private sealed record ManifestDocument(
        [property: JsonPropertyName("schema")] string? Schema,
        [property: JsonPropertyName("synapticVersion")] string? SynapticVersion,
        [property: JsonPropertyName("breakers")] IReadOnlyList<ManifestBreaker>? Breakers);

    private sealed record ManifestBreaker(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("failureKey")] string? FailureKey,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("cell")] string? Cell,
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("mapping")] string? Mapping,
        [property: JsonPropertyName("reset")] string? Reset,
        [property: JsonPropertyName("trip")] string? Trip,
        [property: JsonPropertyName("masterCaution")] bool MasterCaution);
}

/// <summary>
/// The breakers a session may execute, decided once from <see cref="SynapticCircuitBreakerOptions"/>: none unless the
/// application reports the manifest's Synaptic version, then those whose direct trip is qualified (manifest or options).
/// </summary>
internal sealed class SynapticCircuitBreakerActivation
{
    private readonly FrozenSet<string> _enabled;

    private SynapticCircuitBreakerActivation(FrozenSet<string> enabled, string reason)
    {
        _enabled = enabled;
        Reason = reason;
    }

    /// <summary>No breaker executable (the default: no options given).</summary>
    internal static SynapticCircuitBreakerActivation Disabled { get; } =
        new(FrozenSet<string>.Empty, "circuit-breaker failures are disabled (no Synaptic A220 version reported by the application)");

    /// <summary>Why breakers that are not enabled are not executable.</summary>
    internal string Reason { get; }

    /// <summary>The executable breaker ids.</summary>
    internal IReadOnlySet<string> Enabled => _enabled;

    internal static SynapticCircuitBreakerActivation From(SynapticCircuitBreakerOptions? options, SynapticCircuitBreakerManifest manifest)
    {
        if (options is null)
        {
            return Disabled;
        }

        options.Validate();
        var version = Normalize(options.AircraftVersion);
        if (version is null)
        {
            return Disabled;
        }

        if (!string.Equals(version, Normalize(manifest.SynapticVersion), StringComparison.Ordinal))
        {
            return new(FrozenSet<string>.Empty, $"the circuit-breaker cells were qualified on Synaptic A220 {manifest.SynapticVersion}, not on the reported {version}");
        }

        var enabled = manifest.Breakers
            .Where(b => b.Trip == SynapticBreakerQualification.LabValidated || options.QualifiedTrips.Contains(b.Id, StringComparer.Ordinal))
            .Select(b => b.Id)
            .ToFrozenSet(StringComparer.Ordinal);
        return new(enabled, "its direct trip (cell = 1) is not qualified live yet");
    }

    internal bool IsEnabled(SynapticCircuitBreaker breaker) => _enabled.Contains(breaker.Id);

    private static string? Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V') ? trimmed[1..] : trimmed;
    }
}
