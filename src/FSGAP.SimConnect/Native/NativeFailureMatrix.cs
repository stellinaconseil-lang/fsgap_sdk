using System.Globalization;
using System.Text.Json;

namespace FSGAP.SimConnect.Native;

// BLOCK 10C.2 — RESEARCH ONLY, diagnostic. The safety model of the native failure qualification campaign driven by the
// live sample. MSFS has no authoritative readback of these toggles, so the state below is only what this harness knows it
// sent: never a simulator readback, never a FailureProvider, never public. Pure logic, no I/O, so it is unit-tested.

/// <summary>Conditions a diagnostic event may be injected under.</summary>
internal enum NativeFailureGate
{
    /// <summary>On the ground, stationary (&lt; 1 kt), parking brake set.</summary>
    GroundParked,

    /// <summary><see cref="GroundParked"/> and both engines running (generic N2 above <see cref="NativeFailureMatrix.RunningN2Percent"/>).</summary>
    GroundParkedEnginesRunning,

    /// <summary>Airborne, at least <see cref="NativeFailureMatrix.MinimumAirDataHeightFeet"/> above the ground.</summary>
    Airborne,
}

/// <summary>What the harness knows it sent for one event in this run. Local knowledge only.</summary>
internal enum NativeFailureDiagnosticState
{
    NotTouched,
    Injected,
    Restored,
}

internal enum NativeFailureAction
{
    Inject,
    Restore,
}

/// <summary>One row of the matrix.</summary>
internal sealed record NativeFailureDefinition(string Key, string EventName, NativeFailureGate Gate, IReadOnlyList<string> RequiresVerified)
{
    public bool IsAirData => Gate == NativeFailureGate.Airborne;
}

/// <summary>Live conditions sampled just before a send.</summary>
internal readonly record struct NativeFailureConditions(
    bool AircraftQualified,
    bool OnGround,
    double GroundSpeedKnots,
    bool ParkingBrakeSet,
    double HeightAboveGroundFeet,
    double Engine1N2Percent,
    double Engine2N2Percent);

/// <summary>An accepted request to send one event once.</summary>
internal sealed record NativeFailurePlan(NativeFailureDefinition Definition, NativeFailureAction Action);

/// <summary>
/// The campaign state machine: nine allowed events, at most two sends each (inject, restore), never two failures at once,
/// a restore must be verified before anything else is injected, the total brake test only after left and right, air-data
/// tests only after a clean aircraft reload that follows the ground tests, and any uncertainty halts the run for good.
/// </summary>
internal sealed class NativeFailureMatrix
{
    internal const double MaxInjectGroundSpeedKnots = 1.0;
    internal const double RunningN2Percent = 50.0;
    internal const double MinimumAirDataHeightFeet = 1000.0;

    internal static readonly IReadOnlyList<NativeFailureDefinition> Definitions =
    [
        new("LB", NativeFailureProbeEvents.ToggleLeftBrakeFailure, NativeFailureGate.GroundParked, []),
        new("RB", NativeFailureProbeEvents.ToggleRightBrakeFailure, NativeFailureGate.GroundParked, []),
        new("TB", NativeFailureProbeEvents.ToggleTotalBrakeFailure, NativeFailureGate.GroundParked, ["LB", "RB"]),
        new("E1", NativeFailureProbeEvents.ToggleEngine1Failure, NativeFailureGate.GroundParkedEnginesRunning, []),
        new("E2", NativeFailureProbeEvents.ToggleEngine2Failure, NativeFailureGate.GroundParkedEnginesRunning, []),
        new("HYD", NativeFailureProbeEvents.ToggleHydraulicFailure, NativeFailureGate.GroundParked, []),
        new("ELEC", NativeFailureProbeEvents.ToggleElectricalFailure, NativeFailureGate.GroundParked, []),
        new("PITOT", NativeFailureProbeEvents.TogglePitotBlockage, NativeFailureGate.Airborne, []),
        new("STATIC", NativeFailureProbeEvents.ToggleStaticPortBlockage, NativeFailureGate.Airborne, []),
    ];

    internal static readonly IReadOnlyList<string> Verdicts = ["NATIVE_FAILURE_WORKS", "NATIVE_FAILURE_IGNORED", "FAILURE_RESULT_INDETERMINATE"];

    internal static readonly IReadOnlyList<string> Confidences = ["PROVEN_LIVE", "PROVEN_LIVE_PREVIOUS_RUN", "EXTRAPOLATED"];

    private readonly Dictionary<string, NativeFailureDiagnosticState> _states = Definitions.ToDictionary(d => d.Key, _ => NativeFailureDiagnosticState.NotTouched, StringComparer.Ordinal);
    private readonly HashSet<string> _verified = new(StringComparer.Ordinal);
    private bool _cleanReloadAfterGroundTests;

    /// <summary>Why the run stopped for good; <see langword="null"/> while it may continue.</summary>
    public string? HaltReason { get; private set; }

    public bool Halted => HaltReason is not null;

    /// <summary>The one failure this harness injected and has not restored, if any.</summary>
    public string? ActiveKey => _states.FirstOrDefault(s => s.Value == NativeFailureDiagnosticState.Injected).Key;

    public bool CleanReloadAfterGroundTests => _cleanReloadAfterGroundTests;

    public static NativeFailureDefinition? Find(string key) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    public NativeFailureDiagnosticState StateOf(string key) => _states[key];

    public bool IsVerified(string key) => _verified.Contains(key);

    /// <summary>Checks an inject; returns the plan, or a refusal.</summary>
    public (NativeFailurePlan? Plan, string? Refusal) PlanInject(string key, NativeFailureConditions conditions)
    {
        if (Find(key) is not { } definition)
        {
            return (null, $"unknown failure key '{key}' (allowed: {string.Join(", ", Definitions.Select(d => d.Key))})");
        }

        var refusal =
            Halted ? $"matrix halted: {HaltReason}"
            : _states[definition.Key] != NativeFailureDiagnosticState.NotTouched ? $"{definition.Key} was already {_states[definition.Key]} in this run (max two sends: inject, restore)"
            : ActiveKey is { } active ? $"{active} is still injected: restore and verify it first"
            : _states.FirstOrDefault(s => s.Value == NativeFailureDiagnosticState.Restored && !_verified.Contains(s.Key)).Key is { } unverified ? $"{unverified} restored but recovery not verified"
            : definition.RequiresVerified.FirstOrDefault(r => !_verified.Contains(r)) is { } missing ? $"{definition.Key} requires {missing} restored and verified first"
            : definition.IsAirData && GroundTestsTouched && !_cleanReloadAfterGroundTests ? "air-data tests require a clean aircraft reload after the ground tests"
            : GateRefusal(definition.Gate, conditions);
        return refusal is null ? (new NativeFailurePlan(definition, NativeFailureAction.Inject), null) : (null, refusal);
    }

    /// <summary>Checks a restore; a restore is never gated on speed, height or ground state.</summary>
    public (NativeFailurePlan? Plan, string? Refusal) PlanRestore(string key, NativeFailureConditions conditions)
    {
        if (Find(key) is not { } definition)
        {
            return (null, $"unknown failure key '{key}'");
        }

        var refusal =
            Halted ? $"matrix halted: {HaltReason}"
            : _states[definition.Key] != NativeFailureDiagnosticState.Injected ? $"{definition.Key} is {_states[definition.Key]}: only an injected failure can be restored"
            : !conditions.AircraftQualified ? "the loaded aircraft is not the aircraft qualified for this campaign"
            : null;
        return refusal is null ? (new NativeFailurePlan(definition, NativeFailureAction.Restore), null) : (null, refusal);
    }

    /// <summary>Records a send the simulator accepted. The plan must still be valid for the current state.</summary>
    /// <exception cref="InvalidOperationException">The plan no longer matches the state (never sent twice).</exception>
    public void RecordSent(NativeFailurePlan plan)
    {
        var key = plan.Definition.Key;
        var expected = plan.Action == NativeFailureAction.Inject ? NativeFailureDiagnosticState.NotTouched : NativeFailureDiagnosticState.Injected;
        if (Halted || _states[key] != expected)
        {
            throw new InvalidOperationException($"{plan.Action} {key} does not match state {_states[key]}{(Halted ? " (halted)" : string.Empty)}.");
        }

        _states[key] = plan.Action == NativeFailureAction.Inject ? NativeFailureDiagnosticState.Injected : NativeFailureDiagnosticState.Restored;
    }

    /// <summary>The send failed or its outcome is unknown: the toggle count is uncertain, the run stops.</summary>
    public void RecordUncertain(NativeFailurePlan plan, string reason) =>
        Halt($"{plan.Action} {plan.Definition.Key} uncertain: {reason}");

    /// <summary>The pilot and the logs show the aircraft back to baseline after a restore.</summary>
    public string? Verify(string key)
    {
        if (Find(key) is not { } definition)
        {
            return $"unknown failure key '{key}'";
        }

        if (_states[definition.Key] != NativeFailureDiagnosticState.Restored)
        {
            return $"{definition.Key} is {_states[definition.Key]}: only a restored failure can be verified";
        }

        _verified.Add(definition.Key);
        return null;
    }

    /// <summary>The pilot reloaded a clean aircraft. Refused while a failure is injected (that needs a halt, not a reload).</summary>
    public string? RecordCleanReload()
    {
        if (ActiveKey is { } active)
        {
            return $"{active} is still injected: restore it, or halt the matrix";
        }

        if (GroundTestsTouched)
        {
            _cleanReloadAfterGroundTests = true;
        }

        return null;
    }

    /// <summary>The native connection dropped. With a failure injected, its state is no longer known.</summary>
    public void ConnectionLost()
    {
        if (ActiveKey is { } active)
        {
            Halt($"connection lost while {active} was injected");
        }
    }

    /// <summary>A different aircraft, or a fresh load, was detected.</summary>
    public void AircraftReplaced(string description)
    {
        if (ActiveKey is { } active)
        {
            Halt($"aircraft replaced/reloaded ({description}) while {active} was injected");
        }
    }

    public void Halt(string reason) => HaltReason ??= reason;

    /// <summary>Validates a verdict line for the journal.</summary>
    public static string? ValidateVerdict(string key, string verdict, string confidence) =>
        Find(key) is null ? $"unknown failure key '{key}'"
        : !Verdicts.Contains(verdict) ? $"verdict must be one of {string.Join(", ", Verdicts)}"
        : !Confidences.Contains(confidence) ? $"confidence must be one of {string.Join(", ", Confidences)}"
        : null;

    public IReadOnlyDictionary<string, string> Snapshot() =>
        Definitions.ToDictionary(d => d.Key, d => _states[d.Key] + (_verified.Contains(d.Key) ? "+VERIFIED" : string.Empty), StringComparer.Ordinal);

    private bool GroundTestsTouched => Definitions.Any(d => !d.IsAirData && _states[d.Key] != NativeFailureDiagnosticState.NotTouched);

    private static string? GateRefusal(NativeFailureGate gate, NativeFailureConditions c)
    {
        if (!c.AircraftQualified)
        {
            return "the loaded aircraft is not the aircraft qualified for this campaign";
        }

        if (gate == NativeFailureGate.Airborne)
        {
            return c.OnGround ? "air-data tests are airborne only"
                : c.HeightAboveGroundFeet < MinimumAirDataHeightFeet ? string.Create(CultureInfo.InvariantCulture, $"height {c.HeightAboveGroundFeet:0} ft AGL < {MinimumAirDataHeightFeet} ft")
                : null;
        }

        return !c.OnGround ? "ground tests only: SIM ON GROUND is false"
            : c.GroundSpeedKnots >= MaxInjectGroundSpeedKnots ? string.Create(CultureInfo.InvariantCulture, $"ground speed {c.GroundSpeedKnots:0.0} kt: stop the aircraft first")
            : !c.ParkingBrakeSet ? "set the parking brake first"
            : gate == NativeFailureGate.GroundParkedEnginesRunning && (c.Engine1N2Percent < RunningN2Percent || c.Engine2N2Percent < RunningN2Percent)
                ? string.Create(CultureInfo.InvariantCulture, $"both engines must be running (N2 {c.Engine1N2Percent:0.0} / {c.Engine2N2Percent:0.0} %)")
            : null;
    }
}

/// <summary>
/// Line-delimited JSON journal of the campaign: one object per line, a strictly increasing sequence number, flushed per line,
/// safe to call from the sampling and the command loops.
/// </summary>
internal sealed class NativeFailureJournal(TextWriter writer, Func<DateTimeOffset>? clock = null)
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private long _sequence;

    public long Count => Interlocked.Read(ref _sequence);

    public void Write(string kind, object? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate)
        {
            var line = JsonSerializer.Serialize(new { seq = ++_sequence, time = _clock(), kind, payload });
            writer.WriteLine(line);
            writer.Flush();
        }
    }
}
