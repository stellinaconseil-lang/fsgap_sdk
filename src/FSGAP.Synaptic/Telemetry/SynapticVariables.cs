using FSGAP.Abstractions.Simulator;

namespace FSGAP.Synaptic.Telemetry;

/// <summary>
/// The Synaptic A220 variables FSGAP reads, and the only place their names exist in FSGAP. Read-only.
/// </summary>
/// <remarks>
/// <para>
/// All six are officially documented (docs.synapticsim.com/pilots/simvars) and were read live in BLOCK 10A-LIVE. They form
/// one group read every two seconds through <see cref="ISimulatorVariableReader"/> on the simulator's single connection:
/// selectors and pushbuttons hold a position for seconds, so a faster cadence would add load without adding information.
/// </para>
/// <para>
/// Deliberately not read: master caution/warning (no vendor-neutral field exists yet), the documented flap lever and APU
/// selector mode (both contradicted by live behaviour), and the hundreds of lighting, circuit-breaker and aural variables.
/// </para>
/// </remarks>
internal static class SynapticVariables
{
    /// <summary>Unit of every Synaptic local variable: they carry none of their own.</summary>
    private const string LocalUnit = "number";

    /// <summary>Group cadence.</summary>
    internal static readonly TimeSpan SystemsInterval = TimeSpan.FromSeconds(2);

    /// <summary>Position of each variable in <see cref="Systems"/>.</summary>
    internal enum Index
    {
        /// <summary>Left fuel boost pump switch: 0 Off, 1 Auto, 2 On (documented; 1 and 2 seen live).</summary>
        LeftBoostPump,

        /// <summary>Right fuel boost pump switch.</summary>
        RightBoostPump,

        /// <summary>APU switch: 0 when off, 2 while selected and running (live); the documented 1 was never seen.</summary>
        ApuSwitch,

        /// <summary>APU bleed switch selected off: 0 no (selected on), 1 yes (documented).</summary>
        ApuBleedOff,

        /// <summary>Left engine fire pushbutton pressed: 0/1 (documented).</summary>
        LeftEngineFire,

        /// <summary>Right engine fire pushbutton pressed.</summary>
        RightEngineFire,
    }

    /// <summary>The overlay group, in <see cref="Index"/> order.</summary>
    internal static IReadOnlyList<SimulatorVariable> Systems { get; } =
    [
        Local("L:A22X L Boost Pump"),
        Local("L:A22X R Boost Pump"),
        Local("L:A22X APU Switch"),
        Local("L:A22X APU Bleed Off"),
        Local("L:A22X L Eng Fire"),
        Local("L:A22X R Eng Fire"),
    ];

    private static SimulatorVariable Local(string name) => new(name, LocalUnit);
}
