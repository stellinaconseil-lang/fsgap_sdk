// BLOCK 10C.3 — RESEARCH ONLY: the reviewed whitelist of Synaptic A220 controlled-degradation candidates and the safety
// model of the live probe (SynapticDegradationProbe.cs). Pure logic, linked into FSGAP.SimConnect.Tests and unit-tested.
// Every candidate is an exact, documented L:var (docs.synapticsim.com/pilots/simvars, github.com/synapticsim/docs @ca051d9)
// with a documented normal value, an explicit degraded value and an explicit restore value: set semantics only, never a
// toggle. The docs describe these switches' state; that an external write drives the system is what the probe measures.
// Not a failure provider; nothing here reaches FSGAP.Synaptic or FailureCapabilities.
using System.Globalization;

namespace FSGAP.SimConnect.Console;

/// <summary>Family of a candidate, for the report.</summary>
internal enum DegradationFamily
{
    Electrical,
    Hydraulic,
    Pneumatic,
    FlightControls,
}

/// <summary>One reviewed candidate: the variable, its documented normal value and the explicit applied and restore values.</summary>
internal sealed record DegradationCandidate(
    string Id,
    DegradationFamily Family,
    string DisplayName,
    string Variable,
    double NormalValue,
    double DegradedValue,
    IReadOnlyList<string> Indicators,
    string ExpectedConsequence)
{
    /// <summary>Restoration is the documented normal value, written explicitly.</summary>
    public double RestoreValue => NormalValue;
}

/// <summary>Where a candidate is in its one cycle.</summary>
internal enum DegradationStep
{
    Clean,
    Baseline,
    Applied,
    Restored,
    Verified,
}

/// <summary>Live conditions sampled just before a write.</summary>
internal readonly record struct DegradationConditions(
    bool AircraftQualified,
    bool OnGround,
    double GroundSpeedKnots,
    bool ParkingBrakeSet,
    double Engine1N2Percent,
    double Engine2N2Percent);

/// <summary>An accepted request to write one value once.</summary>
internal sealed record DegradationWrite(DegradationCandidate Candidate, string Action, double Value);

/// <summary>
/// CLEAN → BASELINE → APPLIED → RESTORED → VERIFIED (→ CLEAN), one candidate at a time, one cycle per candidate per run.
/// A write is allowed only for a whitelisted candidate, only from the expected step, only when the variable currently reads
/// the value the step expects (normal before apply, degraded or normal before restore), on the ground, stationary, parking
/// brake set, both engines running. A restore whose readback is not the normal value halts the run for good.
/// </summary>
internal sealed class SynapticDegradationSession
{
    internal const double MaxGroundSpeedKnots = 1.0;
    internal const double RunningN2Percent = 50.0;

    internal static readonly IReadOnlyList<DegradationCandidate> Candidates =
    [
        new("ELEC_L_GEN_OFF", DegradationFamily.Electrical, "Left engine generator switch OFF", "L:A22X L Gen Off", 0, 1,
            ["L:A22X L Gen Off Lamp", "L:A22X L Gen Fail Lamp", "L:A22X APU Gen Off Lamp", "L:A22X Caution PBA"],
            "left IDG off line: L GEN OFF lamp, EICAS caution, AC bus 1 transferred to another source; engine 1 keeps running"),
        // Hyd 1 SOV was withdrawn live (2026-10-01): documented "selected on", but the normal configuration reads 0, so the
        // meaning of 1 is unresolved. ACMP 3A has a documented enum (0 Off, 1 Auto, 2 On) and reads Auto in normal flight.
        new("HYD_ACMP_3A_OFF", DegradationFamily.Hydraulic, "AC motor pump 3A switch OFF (3B stays AUTO)", "L:A22X ACMP 3A", 1, 0,
            ["L:A22X ACMP 3B", "L:A22X Caution PBA"],
            "one of the two system 3 pumps off: HYD synoptic pump 3A off, EICAS status/advisory; ACMP 3B (AUTO) keeps system 3 pressurized"),
        new("PNEU_L_PACK_OFF", DegradationFamily.Pneumatic, "Left air-conditioning pack switch OFF", "L:A22X L Pack Off", 0, 1,
            ["L:A22X L Pack Off Lamp", "L:A22X L Pack Fail Lamp", "L:A22X Caution PBA"],
            "left pack off: PACK OFF lamp, ECS synoptic pack L closed, EICAS status/advisory; right pack keeps the cabin supplied"),
        new("FCS_PFCC_1_OFF", DegradationFamily.FlightControls, "PFCC 1 power switch OFF", "L:A22X PFCC 1 Off", 0, 1,
            ["L:A22X PFCC 1 Off Lamp", "L:A22X Caution PBA"],
            "PFCC 1 unpowered: PFCC 1 OFF lamp, flight-control status/EICAS message; PFCC 2/3 keep normal mode"),
    ];

    private readonly Dictionary<string, DegradationStep> _steps = Candidates.ToDictionary(c => c.Id, _ => DegradationStep.Clean, StringComparer.Ordinal);

    public string? HaltReason { get; private set; }

    public bool Halted => HaltReason is not null;

    /// <summary>The one candidate past CLEAN and not yet VERIFIED, if any.</summary>
    public string? OpenId => _steps.FirstOrDefault(s => s.Value is DegradationStep.Baseline or DegradationStep.Applied or DegradationStep.Restored).Key;

    /// <summary>The one candidate whose degraded value was written and not yet restored, if any.</summary>
    public string? ActiveId => _steps.FirstOrDefault(s => s.Value == DegradationStep.Applied).Key;

    public static DegradationCandidate? Find(string id) =>
        Candidates.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public DegradationStep StepOf(string id) => _steps[id];

    /// <summary>Opens the baseline of a candidate: the variable must read its documented normal value.</summary>
    public string? BeginBaseline(string id, double currentValue)
    {
        if (Find(id) is not { } c)
        {
            return UnknownId(id);
        }

        return Halted ? $"halted: {HaltReason}"
            : _steps[c.Id] != DegradationStep.Clean ? $"{c.Id} is {_steps[c.Id]}: one cycle per candidate per run"
            : OpenId is { } open ? $"{open} is still open ({_steps[open]}): finish its cycle first"
            : !Same(currentValue, c.NormalValue) ? Mismatch(c, "normal", c.NormalValue, currentValue)
            : Set(c, DegradationStep.Baseline);
    }

    /// <summary>Plans the degraded write.</summary>
    public (DegradationWrite? Write, string? Refusal) PlanApply(string id, double currentValue, DegradationConditions conditions)
    {
        if (Find(id) is not { } c)
        {
            return (null, UnknownId(id));
        }

        var refusal = Halted ? $"halted: {HaltReason}"
            : _steps[c.Id] != DegradationStep.Baseline ? $"{c.Id} is {_steps[c.Id]}: apply only after its baseline"
            : !Same(currentValue, c.NormalValue) ? Mismatch(c, "normal", c.NormalValue, currentValue)
            : GateRefusal(conditions);
        return refusal is null ? (new DegradationWrite(c, "apply", c.DegradedValue), null) : (null, refusal);
    }

    /// <summary>Plans the restore write; never gated on motion, only on the qualified aircraft.</summary>
    public (DegradationWrite? Write, string? Refusal) PlanRestore(string id, DegradationConditions conditions)
    {
        if (Find(id) is not { } c)
        {
            return (null, UnknownId(id));
        }

        var refusal = Halted ? $"halted: {HaltReason}"
            : _steps[c.Id] != DegradationStep.Applied ? $"{c.Id} is {_steps[c.Id]}: only an applied candidate can be restored"
            : !conditions.AircraftQualified ? "the loaded aircraft is not the qualified Synaptic A220"
            : null;
        return refusal is null ? (new DegradationWrite(c, "restore", c.RestoreValue), null) : (null, refusal);
    }

    /// <summary>Records a write that completed; the step advances whatever the readback says (the value was written).</summary>
    /// <exception cref="InvalidOperationException">The write does not match the current step.</exception>
    public void RecordWritten(DegradationWrite write)
    {
        var expected = write.Action == "apply" ? DegradationStep.Baseline : DegradationStep.Applied;
        if (Halted || _steps[write.Candidate.Id] != expected)
        {
            throw new InvalidOperationException($"{write.Action} {write.Candidate.Id} does not match step {_steps[write.Candidate.Id]}.");
        }

        _steps[write.Candidate.Id] = write.Action == "apply" ? DegradationStep.Applied : DegradationStep.Restored;
    }

    /// <summary>The write threw: whether it landed is unknown.</summary>
    public void RecordUncertain(DegradationWrite write, string reason) => Halt($"{write.Action} {write.Candidate.Id} uncertain: {reason}");

    /// <summary>Checks a restore readback: anything but the normal value halts the run (restoration not demonstrated).</summary>
    public string? CheckRestoreReadback(DegradationCandidate candidate, double readback)
    {
        if (Same(readback, candidate.NormalValue))
        {
            return null;
        }

        Halt(string.Create(CultureInfo.InvariantCulture, $"{candidate.Id} restore readback {readback} != normal {candidate.NormalValue}: reload the aircraft"));
        return HaltReason;
    }

    /// <summary>Closes the cycle after the pilot and the logs show recovery; the variable must read its normal value.</summary>
    public string? Verify(string id, double currentValue)
    {
        if (Find(id) is not { } c)
        {
            return UnknownId(id);
        }

        return _steps[c.Id] != DegradationStep.Restored ? $"{c.Id} is {_steps[c.Id]}: only a restored candidate can be verified"
            : !Same(currentValue, c.NormalValue) ? Mismatch(c, "normal", c.NormalValue, currentValue)
            : Set(c, DegradationStep.Verified);
    }

    /// <summary>Abandons a baseline that was not applied (no write happened).</summary>
    public string? CancelBaseline(string id)
    {
        if (Find(id) is not { } c)
        {
            return UnknownId(id);
        }

        if (_steps[c.Id] != DegradationStep.Baseline)
        {
            return $"{c.Id} is {_steps[c.Id]}: only a baseline can be cancelled";
        }

        _steps[c.Id] = DegradationStep.Clean;
        return null;
    }

    public void ConnectionLost()
    {
        if (ActiveId is { } active)
        {
            Halt($"connection lost while {active} was applied");
        }
    }

    public void AircraftReplaced(string description)
    {
        if (ActiveId is { } active)
        {
            Halt($"aircraft replaced/reloaded ({description}) while {active} was applied");
        }
    }

    public void Halt(string reason) => HaltReason ??= reason;

    internal static readonly IReadOnlyList<string> Results =
        ["USABLE_TRUE_FAILURE", "USABLE_CONTROLLED_DEGRADATION", "CONTROL_ONLY", "NO_EFFECT", "NOT_REVERSIBLE", "INDETERMINATE"];

    public static string? ValidateResult(string id, string result) =>
        Find(id) is null ? UnknownId(id) : !Results.Contains(result) ? $"result must be one of {string.Join(", ", Results)}" : null;

    public IReadOnlyDictionary<string, string> Snapshot() => Candidates.ToDictionary(c => c.Id, c => _steps[c.Id].ToString(), StringComparer.Ordinal);

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.5;

    private static string UnknownId(string id) => $"unknown candidate '{id}' (whitelist: {string.Join(", ", Candidates.Select(c => c.Id))})";

    private static string Mismatch(DegradationCandidate c, string what, double expected, double actual) =>
        string.Create(CultureInfo.InvariantCulture, $"{c.Variable} reads {actual}, expected {what} {expected}: the documented state is not the live state");

    private static string? GateRefusal(DegradationConditions c) =>
        !c.AircraftQualified ? "the loaded aircraft is not the qualified Synaptic A220"
        : !c.OnGround ? "ground only: SIM ON GROUND is false"
        : c.GroundSpeedKnots >= MaxGroundSpeedKnots ? string.Create(CultureInfo.InvariantCulture, $"ground speed {c.GroundSpeedKnots:0.0} kt: stop first")
        : !c.ParkingBrakeSet ? "set the parking brake first"
        : c.Engine1N2Percent < RunningN2Percent || c.Engine2N2Percent < RunningN2Percent ? string.Create(CultureInfo.InvariantCulture, $"both engines must be running (N2 {c.Engine1N2Percent:0.0}/{c.Engine2N2Percent:0.0} %)")
        : null;

    private string? Set(DegradationCandidate c, DegradationStep step)
    {
        _steps[c.Id] = step;
        return null;
    }
}
