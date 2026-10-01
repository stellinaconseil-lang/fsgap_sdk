using FSGAP.Abstractions.Simulator;

namespace FSGAP.Synaptic.Systems;

/// <summary>Where a control's normal and degraded values come from.</summary>
internal enum SynapticControlEvidence
{
    /// <summary>Written, observed in the cockpit and restored live (BLOCK 10C.3 / 11.1).</summary>
    LiveValidated,

    /// <summary>Documented control; its normal value was read live in the normal configuration (2026-10-01), never written.</summary>
    DocumentedLiveNormal,

    /// <summary>Documented control only; never written live.</summary>
    Documented,
}

/// <summary>
/// One writable A22X cockpit control: an exact documented variable, its degraded value, and its normal value (or
/// <see langword="null"/> when there is no single normal value: then anything but the degraded value is normal and the
/// value read before the write is the one restored).
/// </summary>
internal sealed record SynapticControl(string Name, SimulatorVariable Variable, double? Normal, double Degraded, SynapticControlEvidence Evidence, double Tolerance = 0.5)
{
    public bool IsDegraded(double value) => Math.Abs(value - Degraded) < Tolerance;

    public bool IsNormal(double value) => Normal is { } normal ? Math.Abs(value - normal) < Tolerance : !IsDegraded(value);

    /// <summary>The value written to restore: the qualified normal value, or the value found before the write.</summary>
    public double RestoreValue(double original) => Normal ?? original;
}

/// <summary>
/// The single registry of every A22X control FSGAP may write, for the controlled degradations and the failure recipes
/// alike (docs.synapticsim.com/pilots/simvars, github.com/synapticsim/docs @ca051d9). No other type holds a raw A22X
/// name that is written. Enum values are the documented ones (0 Off, 1 Auto, 2 On; booleans 1 = selected/off).
/// </summary>
/// <remarks>
/// Deliberately absent: circuit breakers (no documented mapping or reset), probe heat (a ground-test pulse), engine fire
/// pushbuttons and generator disconnects (latched / not reversible), RAT, passenger oxygen, emergency depressurization,
/// alternate gear (not reversible or unsafe), and any native simulator failure event.
/// </remarks>
internal static class SynapticControls
{
    private const string LocalUnit = "number";

    // Live-validated (BLOCK 10C.3 research, BLOCK 11.1 production path).
    internal static readonly SynapticControl LeftGeneratorOff = new("left generator switch", Local("L:A22X L Gen Off"), 0, 1, SynapticControlEvidence.LiveValidated);
    internal static readonly SynapticControl HydraulicPump3A = new("AC motor pump 3A selector", Local("L:A22X ACMP 3A"), 1, 0, SynapticControlEvidence.LiveValidated);
    internal static readonly SynapticControl LeftPackOff = new("left pack switch", Local("L:A22X L Pack Off"), 0, 1, SynapticControlEvidence.LiveValidated);
    internal static readonly SynapticControl Pfcc1Off = new("PFCC 1 power switch", Local("L:A22X PFCC 1 Off"), 0, 1, SynapticControlEvidence.LiveValidated);

    // Documented, normal value read live in the normal configuration (BLOCK 10C.3 baseline), never written.
    internal static readonly SynapticControl RightGeneratorOff = new("right generator switch", Local("L:A22X R Gen Off"), 0, 1, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl HydraulicPump3B = new("AC motor pump 3B selector", Local("L:A22X ACMP 3B"), 1, 0, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl HydraulicPump2B = new("AC motor pump 2B selector", Local("L:A22X ACMP 2B"), 1, 0, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl PowerTransferUnit = new("power transfer unit selector", Local("L:A22X PTU"), 1, 0, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl Hydraulic1ShutoffValve = new("hydraulic 1 shutoff valve switch", Local("L:A22X Hyd 1 SOV"), 0, 1, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl LeftBoostPump = new("left fuel boost pump selector", Local("L:A22X L Boost Pump"), 1, 0, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl RightBoostPump = new("right fuel boost pump selector", Local("L:A22X R Boost Pump"), 1, 0, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl LeftBleedOff = new("left engine bleed switch", Local("L:A22X L Bleed Off"), 0, 1, SynapticControlEvidence.DocumentedLiveNormal);
    internal static readonly SynapticControl AlternateBrake = new("alternate brake switch", Local("L:A22X Alternate Brake"), 0, 1, SynapticControlEvidence.DocumentedLiveNormal);

    // Documented only.
    internal static readonly SynapticControl ManualPressurization = new("manual pressurization switch", Local("L:A22X Man Press"), 0, 1, SynapticControlEvidence.Documented);
    internal static readonly SynapticControl TrimAirOff = new("trim air switch", Local("L:A22X Trim Air Off"), 0, 1, SynapticControlEvidence.Documented);
    internal static readonly SynapticControl LowerDisplayBrightness = new("lower display brightness", Local("L:A22X Lower Brightness"), null, 0, SynapticControlEvidence.Documented, Tolerance: 0.01);

    internal static readonly IReadOnlyList<SynapticControl> All =
    [
        LeftGeneratorOff, HydraulicPump3A, LeftPackOff, Pfcc1Off,
        RightGeneratorOff, HydraulicPump3B, HydraulicPump2B, PowerTransferUnit, Hydraulic1ShutoffValve, LeftBoostPump, RightBoostPump,
        LeftBleedOff, AlternateBrake, ManualPressurization, TrimAirOff, LowerDisplayBrightness,
    ];

    private static SimulatorVariable Local(string name) => new(name, LocalUnit);
}
