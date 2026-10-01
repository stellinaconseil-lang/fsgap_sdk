using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Synaptic.Degradations;

/// <summary>One qualified control: the public descriptor and its private A22X mapping (variable, normal and degraded values).</summary>
internal sealed record SynapticDegradationControl(DegradationDescriptor Descriptor, SimulatorVariable Variable, double Normal, double Degraded)
{
    public DegradationKey Key => Descriptor.Key;

    /// <summary>Restoration writes the qualified normal value.</summary>
    public double Restore => Normal;
}

/// <summary>
/// The controlled degradations qualified live on the Synaptic A220 (BLOCK 10C.3, 2026-10-01, ground only, one at a time):
/// four documented A22X cockpit controls written with explicit set values and read back. Nothing else is ever written.
/// </summary>
/// <remarks>
/// Deliberately absent: <c>Hyd 1 SOV</c> (value semantics unresolved: documented "selected on", normal reads 0), circuit
/// breakers (no documented mapping or reset), probe heat (a ground-test pulse, not a switch), engine fire pushbuttons
/// (latched until the aircraft is reloaded), native simulator failures, and the symmetric counterparts not yet qualified.
/// </remarks>
internal static class SynapticDegradationControls
{
    private const string LocalUnit = "number";

    private const DegradationOperations All = DegradationOperations.Apply | DegradationOperations.Restore | DegradationOperations.ReadState;

    internal static readonly IReadOnlyList<SynapticDegradationControl> Controls =
    [
        new(
            new DegradationDescriptor
            {
                Key = DegradationKey.Parse("electrical.generator.1.forced-off"),
                DisplayName = "Generator 1 forced off",
                Description = "FSGAP sets the left engine generator switch to OFF. The generator goes off line (OFF light, "
                    + "EICAS message, electrical network reconfigured); the generator and the engine remain healthy. "
                    + "Not a generator failure.",
                Category = DegradationCategory.Electrical,
                Operations = All,
            },
            new SimulatorVariable("L:A22X L Gen Off", LocalUnit),
            Normal: 0,
            Degraded: 1),
        new(
            new DegradationDescriptor
            {
                Key = DegradationKey.Parse("hydraulic.system-3.electric-pump-a.forced-off"),
                DisplayName = "Hydraulic system 3 electric pump A forced off",
                Description = "FSGAP sets the AC motor pump 3A selector from AUTO to OFF. Pump 3A stops and pump 3B starts "
                    + "automatically (EICAS HYD 3 LO PRESS, a caution that clears once 3B carries the system). Loss of one "
                    + "hydraulic source, not a failure of hydraulic system 3.",
                Category = DegradationCategory.Hydraulic,
                Operations = All,
            },
            new SimulatorVariable("L:A22X ACMP 3A", LocalUnit),
            Normal: 1,
            Degraded: 0),
        new(
            new DegradationDescriptor
            {
                Key = DegradationKey.Parse("air-conditioning.pack.1.forced-off"),
                DisplayName = "Pack 1 forced off",
                Description = "FSGAP sets the left air-conditioning pack switch to OFF. The left pack stops and its valve "
                    + "closes (OFF light, EICAS L PACK OFF); the right pack keeps supplying the cabin. Not a pack failure.",
                Category = DegradationCategory.AirConditioning,
                Operations = All,
            },
            new SimulatorVariable("L:A22X L Pack Off", LocalUnit),
            Normal: 0,
            Degraded: 1),
        new(
            new DegradationDescriptor
            {
                Key = DegradationKey.Parse("flight-controls.pfcc.1.forced-off"),
                DisplayName = "Primary flight control computer 1 forced off",
                Description = "FSGAP sets the PFCC 1 power switch to OFF. PFCC 1 is unpowered (OFF light, EICAS PFCC1 OFF, "
                    + "flight-control synoptic); the other computers keep controlling the aircraft. Not a PFCC internal fault.",
                Category = DegradationCategory.FlightControls,
                Operations = All,
            },
            new SimulatorVariable("L:A22X PFCC 1 Off", LocalUnit),
            Normal: 0,
            Degraded: 1),
    ];

    internal static readonly DegradationCatalog Catalog = new(Controls.Select(c => c.Descriptor));

    internal static SynapticDegradationControl? Find(DegradationKey key) => Controls.FirstOrDefault(c => c.Key == key);
}
