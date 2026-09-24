using FSGAP.Abstractions.Simulator;

namespace FSGAP.Fenix.Variables;

/// <summary>
/// The Fenix variables FSGAP reads, and the only place their technical names exist in FSGAP.
/// </summary>
/// <remarks>
/// <para>
/// Every name here was proven live against MSFS 2024 and a real Fenix A320 by the audited applications (FSHANGAR
/// <c>FenixCockpitControlRegistry</c>, 2026-09-12 and 2026-09-13; hydraulic pressures and reservoirs and the BAT1
/// voltage cross-checked against the Fenix ECAM on 2026-08-31). None is guessed. The full inventory of the 39 legacy cockpit variables, with the
/// reason each one is or is not read, is in docs/fenix-system-telemetry.md.
/// </para>
/// <para>
/// Two groups, each read as <b>one</b> batched request through
/// <see cref="ISimulatorVariableReader"/> (the simulator's single connection):
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="Cockpit"/>, every second: switch and light states. The legacy code polled at 10 Hz only to catch
/// brief FIRE TEST presses on counters, which FSGAP does not expose; one second is well inside the time a selector,
/// a pump switch, a fire handle or a lit fire warning stays in a state.
/// </description></item>
/// <item><description>
/// <see cref="Systems"/>, every five seconds: hydraulic pressures and reservoirs and the BAT1 voltage, as the audited
/// applications read them.
/// </description></item>
/// </list>
/// <para>Read-only: nothing here is ever written.</para>
/// </remarks>
internal static class FenixVariables
{
    /// <summary>Unit of every Fenix local variable: they carry none of their own.</summary>
    private const string LocalUnit = "number";

    /// <summary>Cockpit group cadence.</summary>
    internal static readonly TimeSpan CockpitInterval = TimeSpan.FromSeconds(1);

    /// <summary>Systems group cadence.</summary>
    internal static readonly TimeSpan SystemsInterval = TimeSpan.FromSeconds(5);

    /// <summary>Position of each variable in <see cref="Cockpit"/>.</summary>
    internal enum CockpitIndex
    {
        /// <summary>ADIRS IR1 mode selector: 0 OFF, 1 NAV, 2 ATT.</summary>
        Ir1Mode,

        /// <summary>ADIRS IR2 mode selector.</summary>
        Ir2Mode,

        /// <summary>ADIRS IR3 mode selector.</summary>
        Ir3Mode,

        /// <summary>Left tank pump 1 switch: 0 OFF, 1 ON.</summary>
        FuelLeft1,

        /// <summary>Left tank pump 2 switch.</summary>
        FuelLeft2,

        /// <summary>Centre tank pump 1 switch.</summary>
        FuelCenter1,

        /// <summary>Centre tank pump 2 switch.</summary>
        FuelCenter2,

        /// <summary>Right tank pump 1 switch.</summary>
        FuelRight1,

        /// <summary>Right tank pump 2 switch.</summary>
        FuelRight2,

        /// <summary>ENG1 fire handle: 0 STOWED, 1 PULLED (a stable latch, proven live).</summary>
        Eng1FireHandle,

        /// <summary>ENG2 fire handle.</summary>
        Eng2FireHandle,

        /// <summary>APU fire handle.</summary>
        ApuFireHandle,

        /// <summary>ENG1 fire pushbutton light: 0/1. Also lit during a FIRE TEST.</summary>
        Eng1FireLight,

        /// <summary>ENG2 fire pushbutton light.</summary>
        Eng2FireLight,
    }

    /// <summary>Position of each variable in <see cref="Systems"/>.</summary>
    internal enum SystemsIndex
    {
        /// <summary>Green circuit pressure (index 1 of the stock SimVar, ECAM-verified on Fenix).</summary>
        GreenPressure,

        /// <summary>Blue circuit pressure (index 2, ECAM-verified on Fenix).</summary>
        BluePressure,

        /// <summary>Green reservoir quantity, percent (index 1, same circuit mapping as the pressure).</summary>
        GreenReservoir,

        /// <summary>Blue reservoir quantity, percent (index 2).</summary>
        BlueReservoir,

        /// <summary>BAT1 voltage (index 1, ECAM-verified on Fenix).</summary>
        Battery1Voltage,
    }

    /// <summary>Cockpit switch and light states, in <see cref="CockpitIndex"/> order.</summary>
    internal static IReadOnlyList<SimulatorVariable> Cockpit { get; } =
    [
        Local("L:S_OH_NAV_IR1_MODE"),
        Local("L:S_OH_NAV_IR2_MODE"),
        Local("L:S_OH_NAV_IR3_MODE"),
        Local("L:S_OH_FUEL_LEFT_1"),
        Local("L:S_OH_FUEL_LEFT_2"),
        Local("L:S_OH_FUEL_CENTER_1"),
        Local("L:S_OH_FUEL_CENTER_2"),
        Local("L:S_OH_FUEL_RIGHT_1"),
        Local("L:S_OH_FUEL_RIGHT_2"),
        Local("L:S_OH_FIRE_ENG1_BUTTON"),
        Local("L:S_OH_FIRE_ENG2_BUTTON"),
        Local("L:S_OH_FIRE_APU_BUTTON"),
        Local("L:I_OH_FIRE_ENG1_BUTTON"),
        Local("L:I_OH_FIRE_ENG2_BUTTON"),
    ];

    /// <summary>
    /// Hydraulic and battery readings, in <see cref="SystemsIndex"/> order. Stock SimVars, but their index meaning is
    /// Fenix knowledge, which is why they are read here and not by the generic groups.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Hydraulics: index 1 is green and 2 is blue. Index 3 does <b>not</b> map to the Fenix yellow circuit (it reads
    /// 0 psi while the ECAM shows 3000), so it is not read at all, for pressure or reservoir.
    /// </description></item>
    /// <item><description>
    /// Batteries: index 1 is BAT1 (matches the ECAM ELEC page). Index 2 does <b>not</b> read BAT2 on Fenix, so it is
    /// not read.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static IReadOnlyList<SimulatorVariable> Systems { get; } =
    [
        new("HYDRAULIC PRESSURE:1", "Psi"),
        new("HYDRAULIC PRESSURE:2", "Psi"),
        new("HYDRAULIC RESERVOIR PERCENT:1", "Percent"),
        new("HYDRAULIC RESERVOIR PERCENT:2", "Percent"),
        new("ELECTRICAL BATTERY VOLTAGE:1", "Volts"),
    ];

    private static SimulatorVariable Local(string name) => new(name, LocalUnit);
}
