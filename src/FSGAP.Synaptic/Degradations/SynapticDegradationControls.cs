using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Simulator;
using FSGAP.Synaptic.Systems;

namespace FSGAP.Synaptic.Degradations;

/// <summary>One public controlled degradation: its descriptor and the registry control it forces (see <see cref="SynapticControls"/>).</summary>
internal sealed record SynapticDegradationControl(DegradationDescriptor Descriptor, SynapticControl Control)
{
    public DegradationKey Key => Descriptor.Key;

    public SimulatorVariable Variable => Control.Variable;

    public double Normal => Control.Normal!.Value;

    public double Degraded => Control.Degraded;

    /// <summary>Restoration writes the qualified normal value.</summary>
    public double Restore => Normal;
}

/// <summary>
/// The controlled degradations qualified live on the Synaptic A220 (BLOCK 10C.3, 2026-10-01, ground only, one at a time):
/// four registry controls (<see cref="SynapticControls"/>) written with explicit set values and read back. The public
/// degradation catalog stays limited to these four; the failure recipes may use other registry controls.
/// </summary>
internal static class SynapticDegradationControls
{
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
            SynapticControls.LeftGeneratorOff),
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
            SynapticControls.HydraulicPump3A),
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
            SynapticControls.LeftPackOff),
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
            SynapticControls.Pfcc1Off),
    ];

    internal static readonly DegradationCatalog Catalog = new(Controls.Select(c => c.Descriptor));

    internal static SynapticDegradationControl? Find(DegradationKey key) => Controls.FirstOrDefault(c => c.Key == key);
}
