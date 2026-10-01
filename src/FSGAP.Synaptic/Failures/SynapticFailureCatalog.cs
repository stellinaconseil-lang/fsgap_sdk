using FSGAP.Abstractions.Failures;
using FSGAP.Synaptic.Degradations;
using FSGAP.Synaptic.Systems;

namespace FSGAP.Synaptic.Failures;

/// <summary>
/// Engineering provenance of a recipe, kept for traceability and for the later qualification report. It never gates
/// execution: <see cref="Validated"/>, <see cref="Assumed"/> and <see cref="Approximation"/> recipes all execute;
/// <see cref="Unmapped"/> means no documented Synaptic control exists, so the key is listed but not executable.
/// </summary>
internal enum SynapticFailureQualification
{
    /// <summary>The control was written, observed in the cockpit and restored live.</summary>
    Validated,

    /// <summary>A documented control matching the failure; not yet exercised live.</summary>
    Assumed,

    /// <summary>The closest coherent documented degradation; the operational effect only approximates the failure.</summary>
    Approximation,

    /// <summary>No documented Synaptic control: listed for catalog parity, <c>Operations = None</c>.</summary>
    Unmapped,
}

/// <summary>How a recipe acts on the aircraft.</summary>
internal enum SynapticFailureMechanism
{
    /// <summary>The single control of one of the public controlled degradations (shared registry control and board).</summary>
    ControlledDegradation,

    /// <summary>One other registry control.</summary>
    LocalVariableState,

    /// <summary>Several registry controls, applied and restored together.</summary>
    CompositeSystemState,

    /// <summary>No mechanism (unmapped key).</summary>
    None,
}

/// <summary>
/// One normalized failure on the Synaptic A220: the same definition as the Fenix key (key, display name, category,
/// target), the registry controls it forces, and its provenance. Raw A22X names stay in <see cref="SynapticControls"/>.
/// </summary>
internal sealed record SynapticFailureRecipe(FailureDefinition Definition, SynapticFailureQualification Qualification, string Effect, IReadOnlyList<SynapticControl> Controls)
{
    public FailureKey Key => Definition.Key;

    public bool Executable => Controls.Count > 0;

    public SynapticFailureMechanism Mechanism =>
        Controls.Count == 0 ? SynapticFailureMechanism.None
        : Controls.Count > 1 ? SynapticFailureMechanism.CompositeSystemState
        : SynapticDegradationControls.Controls.Any(d => ReferenceEquals(d.Control, Controls[0])) ? SynapticFailureMechanism.ControlledDegradation
        : SynapticFailureMechanism.LocalVariableState;
}

/// <summary>
/// The Synaptic A220 failure catalog: exactly the 40 normalized keys of the Fenix catalog (same display names, categories
/// and targets), each with one recipe. Generated from the Fenix mapping resource; the parity is enforced by tests.
/// </summary>
/// <remarks>
/// Executable recipes force documented cockpit controls (see <see cref="SynapticControls"/>): the failure is the aircraft
/// put into a degraded configuration, not a diagnosed hardware fault. No native simulator failure event is used. Keys with
/// no documented control are listed with <c>Operations = None</c> (a trigger answers <c>NotSupported</c>), so consumers see
/// the same key set on every aircraft and which ones act.
/// </remarks>
internal static class SynapticFailureCatalog
{
    private const FailureOperations Both = FailureOperations.Trigger | FailureOperations.Clear;

    internal static readonly IReadOnlyList<SynapticFailureRecipe> Recipes =
    [
        F("air-conditioning.cpc.1", "Cabin pressure controller 1", FailureCategory.AirConditioning, FailureTarget.Aircraft, SynapticFailureQualification.Approximation, "automatic pressurization replaced by manual mode (cabin pressure controller out of the loop)", SynapticControls.ManualPressurization),
        F("air-conditioning.pack.1.overheat", "Pack 1 overheat", FailureCategory.AirConditioning, FailureTarget.Aircraft, SynapticFailureQualification.Approximation, "left pack shut down, as after an overheat (live-validated control)", SynapticControls.LeftPackOff),
        F("air-conditioning.pack.1.regulator-fault", "Pack 1 regulator fault", FailureCategory.AirConditioning, FailureTarget.Aircraft, SynapticFailureQualification.Approximation, "trim air off: zone temperature regulation lost", SynapticControls.TrimAirOff),
        F("electrical.static-inverter", "Static inverter", FailureCategory.Electrical, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("electrical.generator.1", "Generator 1 (IDG 1 drive)", FailureCategory.Electrical, FailureTarget.Aircraft, SynapticFailureQualification.Validated, "left generator off line (live-validated control)", SynapticControls.LeftGeneratorOff),
        F("electrical.generator.2", "Generator 2 (IDG 2 drive)", FailureCategory.Electrical, FailureTarget.Aircraft, SynapticFailureQualification.Assumed, "right generator off line", SynapticControls.RightGeneratorOff),
        F("electrical.bus.ac-1", "AC bus 1", FailureCategory.Electrical, FailureTarget.ElectricalBus("ac-1"), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("electrical.bus.ac-ess", "AC essential bus", FailureCategory.Electrical, FailureTarget.ElectricalBus("ac-ess"), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("electrical.bus.dc-1", "DC bus 1", FailureCategory.Electrical, FailureTarget.ElectricalBus("dc-1"), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("electrical.bus.dc-2", "DC bus 2", FailureCategory.Electrical, FailureTarget.ElectricalBus("dc-2"), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("electrical.bus.dc-bat", "DC battery bus", FailureCategory.Electrical, FailureTarget.ElectricalBus("dc-bat"), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("fire.lavatory.smoke", "Lavatory smoke", FailureCategory.Fire, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("fire.engine.1.loop-a", "Engine 1 fire detection loop A", FailureCategory.Fire, FailureTarget.Engine(1), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("fire.fdu.1", "Fire detection unit 1", FailureCategory.Fire, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("fuel.fqi.channel-2", "Fuel quantity indication channel 2", FailureCategory.Fuel, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("fuel.pump.left-1", "Left tank fuel pump 1", FailureCategory.Fuel, FailureTarget.FuelPump("left-1"), SynapticFailureQualification.Assumed, "left boost pump selector off", SynapticControls.LeftBoostPump),
        F("fuel.pump.right-1", "Right tank fuel pump 1", FailureCategory.Fuel, FailureTarget.FuelPump("right-1"), SynapticFailureQualification.Assumed, "right boost pump selector off", SynapticControls.RightBoostPump),
        F("hydraulic.blue.electric-pump", "Blue electric hydraulic pump", FailureCategory.Hydraulic, FailureTarget.HydraulicSystem("blue"), SynapticFailureQualification.Validated, "system 3 pump 3A off, 3B takes over (A320 blue ~ A220 system 3; live-validated control)", SynapticControls.HydraulicPump3A),
        F("hydraulic.yellow.electric-pump", "Yellow electric hydraulic pump", FailureCategory.Hydraulic, FailureTarget.HydraulicSystem("yellow"), SynapticFailureQualification.Assumed, "system 2 AC motor pump 2B off (A320 yellow ~ A220 system 2)", SynapticControls.HydraulicPump2B),
        F("hydraulic.blue.low-level", "Blue hydraulic reservoir low level", FailureCategory.Hydraulic, FailureTarget.HydraulicSystem("blue"), SynapticFailureQualification.Approximation, "both system 3 pumps off: system 3 unpressurized", SynapticControls.HydraulicPump3A, SynapticControls.HydraulicPump3B),
        F("hydraulic.green.low-level", "Green hydraulic reservoir low level", FailureCategory.Hydraulic, FailureTarget.HydraulicSystem("green"), SynapticFailureQualification.Approximation, "system 1 shutoff valve switch to its non-normal position (A320 green ~ A220 system 1)", SynapticControls.Hydraulic1ShutoffValve),
        F("hydraulic.blue.leak", "Blue hydraulic leak", FailureCategory.Hydraulic, FailureTarget.HydraulicSystem("blue"), SynapticFailureQualification.Approximation, "both system 3 pumps off: system 3 unpressurized", SynapticControls.HydraulicPump3A, SynapticControls.HydraulicPump3B),
        F("hydraulic.green.leak", "Green hydraulic leak", FailureCategory.Hydraulic, FailureTarget.HydraulicSystem("green"), SynapticFailureQualification.Approximation, "system 1 shutoff valve switch to its non-normal position and PTU off", SynapticControls.Hydraulic1ShutoffValve, SynapticControls.PowerTransferUnit),
        F("ice-rain.aoa-heat.standby", "Standby AOA probe heat", FailureCategory.IceAndRain, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("ice-rain.pitot-heat.fo", "First officer pitot heat", FailureCategory.IceAndRain, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("indicating.display.ecam-lower", "Lower ECAM display unit", FailureCategory.Instruments, FailureTarget.Aircraft, SynapticFailureQualification.Approximation, "lower display brightness to zero (display dark)", SynapticControls.LowerDisplayBrightness),
        F("landing-gear.brake.wheel-1", "Wheel 1 brake fault", FailureCategory.LandingGear, FailureTarget.Aircraft, SynapticFailureQualification.Approximation, "alternate brake mode selected: normal brake control degraded (no side-specific brake isolation is documented)", SynapticControls.AlternateBrake),
        F("landing-gear.tyre-pressure.main-1", "Main tyre 1 low pressure", FailureCategory.LandingGear, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("landing-gear.tyre-pressure.right-1", "Right tyre 1 low pressure", FailureCategory.LandingGear, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("navigation.fmgc.1", "FMGC 1", FailureCategory.Navigation, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("navigation.mcdu.1.recoverable-fault", "MCDU 1 recoverable fault", FailureCategory.Navigation, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("navigation.adf.1", "ADF 1", FailureCategory.Navigation, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("navigation.gps.1", "GPS 1", FailureCategory.Navigation, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("navigation.ils.1.localizer", "ILS 1 localizer", FailureCategory.Navigation, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("pneumatic.bleed-valve.1", "Engine 1 bleed valve", FailureCategory.Pneumatic, FailureTarget.Aircraft, SynapticFailureQualification.Assumed, "left engine bleed off", SynapticControls.LeftBleedOff),
        F("doors.entry.forward-left", "Forward left entry door", FailureCategory.Doors, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("doors.entry.aft-left", "Aft left entry door", FailureCategory.Doors, FailureTarget.Aircraft, SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("engine.1.surge", "Engine 1 surge", FailureCategory.Engine, FailureTarget.Engine(1), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("engine.1.vibration.n1", "Engine 1 high N1 vibration", FailureCategory.Engine, FailureTarget.Engine(1), SynapticFailureQualification.Unmapped, "no documented Synaptic control"),
        F("engine.1.eiu", "Engine 1 interface unit (EIU 1)", FailureCategory.Engine, FailureTarget.Engine(1), SynapticFailureQualification.Approximation, "engine 1 services to the aircraft cut: left generator and left bleed off", SynapticControls.LeftGeneratorOff, SynapticControls.LeftBleedOff),    ];

    internal static readonly FailureCatalog Catalog = new(Recipes.Select(r => r.Definition));

    internal static SynapticFailureRecipe? Find(FailureKey key) => Recipes.FirstOrDefault(r => r.Key == key);

    private static SynapticFailureRecipe F(
        string key,
        string displayName,
        FailureCategory category,
        FailureTarget target,
        SynapticFailureQualification qualification,
        string effect,
        params SynapticControl[] controls) =>
        new(
            new FailureDefinition
            {
                Key = FailureKey.Parse(key),
                DisplayName = displayName,
                Category = category,
                SupportedTargets = [target],
                Operations = controls.Length > 0 ? Both : FailureOperations.None,
            },
            qualification,
            effect,
            controls);
}
