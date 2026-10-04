using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Cockpit;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Fenix.Cockpit;

/// <summary>
/// The Fenix cockpit observation catalog: the normalized <see cref="CockpitObservationKey"/> the Fenix provider
/// publishes, each bound INTERNALLY to the Fenix local variable it reads and the normalized value kind it produces.
/// </summary>
/// <remarks>
/// The L:Var names live here only, inside the provider package; they never appear in the public API, in
/// <c>FSGAP.Abstractions</c>, or in any consumer-facing type. Consumers see only the dotted keys. Integer keys are
/// multi-position selectors (ADIRS mode) or monotonic step counters (fire-test pushbuttons, which never return to
/// zero); boolean keys are on/off controls (switches, indicator lights). The semantic LABELLING of a value (for
/// example "NAV" or "PRESSED") is deliberately NOT done here — the consumer owns that.
/// </remarks>
internal static class FenixCockpitObservations
{
    private const string LocalUnit = "number";

    private static FenixCockpitControl Int(string key, string lvar) =>
        new(CockpitObservationKey.Parse(key), new SimulatorVariable(lvar, LocalUnit), CockpitObservationValueKind.Integer);

    private static FenixCockpitControl Bool(string key, string lvar) =>
        new(CockpitObservationKey.Parse(key), new SimulatorVariable(lvar, LocalUnit), CockpitObservationValueKind.Boolean);

    /// <summary>The Fenix cockpit controls, in catalog order (the order of a batched read).</summary>
    public static readonly IReadOnlyList<FenixCockpitControl> Controls =
    [
        // Fire / smoke tests — monotonic step counters (never return to 0), so Integer.
        Int("fire-test.engine-1", "L:S_OH_FIRE_ENG1_TEST"),
        Int("fire-test.engine-2", "L:S_OH_FIRE_ENG2_TEST"),
        Int("fire-test.apu", "L:S_OH_FIRE_APU_TEST"),
        Int("smoke-test.cargo", "L:S_OH_CARGO_SMOKE_TEST"),
        Int("fire.engine-1.agent-1.discharge", "L:S_OH_FIRE_ENG1_AGENT1"),
        Int("fire.engine-1.agent-2.discharge", "L:S_OH_FIRE_ENG1_AGENT2"),
        Int("fire.engine-2.agent-1.discharge", "L:S_OH_FIRE_ENG2_AGENT1"),
        Int("fire.apu.agent.discharge", "L:S_OH_FIRE_APU_AGENT"),
        Int("master-warning.captain.input", "L:S_MIP_MASTER_WARNING_CAPT"),

        // ADIRS IR mode selectors — Integer (multi-position).
        Int("adirs.ir-1.mode", "L:S_OH_NAV_IR1_MODE"),
        Int("adirs.ir-2.mode", "L:S_OH_NAV_IR2_MODE"),
        Int("adirs.ir-3.mode", "L:S_OH_NAV_IR3_MODE"),

        // Fuel pumps — on/off switches.
        Bool("fuel-pump.left-1", "L:S_OH_FUEL_LEFT_1"),
        Bool("fuel-pump.left-2", "L:S_OH_FUEL_LEFT_2"),
        Bool("fuel-pump.center-1", "L:S_OH_FUEL_CENTER_1"),
        Bool("fuel-pump.center-2", "L:S_OH_FUEL_CENTER_2"),
        Bool("fuel-pump.right-1", "L:S_OH_FUEL_RIGHT_1"),
        Bool("fuel-pump.right-2", "L:S_OH_FUEL_RIGHT_2"),

        // Fire indications / buttons / agent lights — on/off.
        Bool("fire.engine-1.indication", "L:I_ENG_FIRE_1"),
        Bool("fire.engine-2.indication", "L:I_ENG_FIRE_2"),
        Bool("fire.engine-1.button.input", "L:S_OH_FIRE_ENG1_BUTTON"),
        Bool("fire.engine-1.button.light", "L:I_OH_FIRE_ENG1_BUTTON"),
        Bool("fire.engine-2.button.input", "L:S_OH_FIRE_ENG2_BUTTON"),
        Bool("fire.engine-2.button.light", "L:I_OH_FIRE_ENG2_BUTTON"),
        Bool("fire.engine-1.agent-1.light-lower", "L:I_OH_FIRE_ENG1_AGENT1_L"),
        Bool("fire.engine-1.agent-1.light-upper", "L:I_OH_FIRE_ENG1_AGENT1_U"),
        Bool("fire.engine-1.agent-2.light-lower", "L:I_OH_FIRE_ENG1_AGENT2_L"),
        Bool("fire.engine-1.agent-2.light-upper", "L:I_OH_FIRE_ENG1_AGENT2_U"),
        Bool("fire.engine-2.agent-1.light-lower", "L:I_OH_FIRE_ENG2_AGENT1_L"),
        Bool("fire.engine-2.agent-1.light-upper", "L:I_OH_FIRE_ENG2_AGENT1_U"),
        Bool("fire.engine-2.agent-2.discharge", "L:S_OH_FIRE_ENG2_AGENT2"),
        Bool("fire.engine-2.agent-2.light-lower", "L:I_OH_FIRE_ENG2_AGENT2_L"),
        Bool("fire.engine-2.agent-2.light-upper", "L:I_OH_FIRE_ENG2_AGENT2_U"),
        Bool("fire.apu.button.input", "L:S_OH_FIRE_APU_BUTTON"),

        // Master warning indications.
        Bool("master-warning.captain.light", "L:I_MIP_MASTER_WARNING_CAPT"),
        Bool("master-warning.captain.light-l", "L:I_MIP_MASTER_WARNING_CAPT_L"),
        Bool("master-warning.first-officer.input", "L:S_MIP_MASTER_WARNING_FO"),
        Bool("master-warning.first-officer.light", "L:I_MIP_MASTER_WARNING_FO"),
        Bool("master-warning.first-officer.light-l", "L:I_MIP_MASTER_WARNING_FO_L"),
    ];

    /// <summary>The public normalized keys (the catalog).</summary>
    public static readonly IReadOnlyList<CockpitObservationKey> Keys = Controls.Select(c => c.Key).ToArray();

    /// <summary>The simulator variables to read, in catalog order, as one batch.</summary>
    public static readonly IReadOnlyList<SimulatorVariable> Variables = Controls.Select(c => c.Variable).ToArray();

    /// <summary>The capability Fenix sessions publish for cockpit observation.</summary>
    public static readonly CockpitObservationCapabilities Capabilities = new() { CanObserve = true, Keys = Keys };
}

/// <summary>One Fenix cockpit control: its public key, the local variable to read, and the normalized value kind.</summary>
internal sealed record FenixCockpitControl(CockpitObservationKey Key, SimulatorVariable Variable, CockpitObservationValueKind Kind);
