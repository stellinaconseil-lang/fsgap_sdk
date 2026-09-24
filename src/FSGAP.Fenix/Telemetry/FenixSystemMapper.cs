using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Variables;

namespace FSGAP.Fenix.Telemetry;

/// <summary>
/// Turns raw Fenix readings into normalized sections. Pure functions: raw values and a receive time in, immutable
/// sections out, so every interpretation is testable without a simulator.
/// </summary>
/// <remarks>
/// <para>
/// A raw value outside the proven encoding (for example 3 on a three-position selector, or 0.5 on a switch) is
/// reported as <see cref="ValueState.Unknown"/>: the variable answered, but not with anything whose meaning was ever
/// observed. It is never rounded or guessed.
/// </para>
/// <para>
/// Only what the variables actually say is produced. IR alignment and fault, pump fault (low pressure), engine and
/// APU fire detection, and the hydraulic "pressurized" state have no proven source and stay
/// <see cref="ValueState.Unavailable"/>.
/// </para>
/// </remarks>
internal static class FenixSystemMapper
{
    private static readonly (string Id, string Name, FenixVariables.CockpitIndex Raw)[] Pumps =
    [
        ("left-1", "Left tank pump 1", FenixVariables.CockpitIndex.FuelLeft1),
        ("left-2", "Left tank pump 2", FenixVariables.CockpitIndex.FuelLeft2),
        ("center-1", "Centre tank pump 1", FenixVariables.CockpitIndex.FuelCenter1),
        ("center-2", "Centre tank pump 2", FenixVariables.CockpitIndex.FuelCenter2),
        ("right-1", "Right tank pump 1", FenixVariables.CockpitIndex.FuelRight1),
        ("right-2", "Right tank pump 2", FenixVariables.CockpitIndex.FuelRight2),
    ];

    /// <summary>Applies a cockpit group read: replaces IRs, pumps and fire panel, keeps hydraulics and batteries.</summary>
    /// <exception cref="ArgumentException"><paramref name="raw"/> does not have one value per cockpit variable.</exception>
    internal static FenixSystemState ApplyCockpit(FenixSystemState current, IReadOnlyList<double> raw, DateTimeOffset observedAt)
    {
        RequireCount(raw, FenixVariables.Cockpit.Count);
        double At(FenixVariables.CockpitIndex index) => raw[(int)index];

        return current with
        {
            InertialReferences =
            [
                Ir(1, At(FenixVariables.CockpitIndex.Ir1Mode), observedAt),
                Ir(2, At(FenixVariables.CockpitIndex.Ir2Mode), observedAt),
                Ir(3, At(FenixVariables.CockpitIndex.Ir3Mode), observedAt),
            ],
            FuelPumps = Pumps
                .Select(p => new FuelPumpTelemetry { Id = p.Id, Name = p.Name, IsOn = Discrete(At(p.Raw), observedAt) })
                .ToArray(),
            EngineFirePanels =
            [
                new EngineFirePanel(
                    1,
                    Discrete(At(FenixVariables.CockpitIndex.Eng1FireHandle), observedAt),
                    Discrete(At(FenixVariables.CockpitIndex.Eng1FireLight), observedAt)),
                new EngineFirePanel(
                    2,
                    Discrete(At(FenixVariables.CockpitIndex.Eng2FireHandle), observedAt),
                    Discrete(At(FenixVariables.CockpitIndex.Eng2FireLight), observedAt)),
            ],
            ApuFireHandlePulled = Discrete(At(FenixVariables.CockpitIndex.ApuFireHandle), observedAt),
        };
    }

    /// <summary>Applies a systems group read: replaces the hydraulic systems and batteries, keeps the rest.</summary>
    /// <exception cref="ArgumentException"><paramref name="raw"/> does not have one value per systems variable.</exception>
    internal static FenixSystemState ApplySystems(FenixSystemState current, IReadOnlyList<double> raw, DateTimeOffset observedAt)
    {
        RequireCount(raw, FenixVariables.Systems.Count);
        double At(FenixVariables.SystemsIndex index) => raw[(int)index];

        return current with
        {
            HydraulicSystems =
            [
                Circuit(
                    "green",
                    "Green",
                    At(FenixVariables.SystemsIndex.GreenPressure),
                    At(FenixVariables.SystemsIndex.GreenReservoir),
                    observedAt),
                Circuit(
                    "blue",
                    "Blue",
                    At(FenixVariables.SystemsIndex.BluePressure),
                    At(FenixVariables.SystemsIndex.BlueReservoir),
                    observedAt),

                // Present so consumers see the circuit exists, but with no value: the only generic source (index 3)
                // is confirmed wrong on Fenix, and no Fenix variable for it was ever validated.
                new HydraulicSystemTelemetry { Id = "yellow", Name = "Yellow" },
            ],
            Batteries =
            [
                new BatteryTelemetry
                {
                    Id = "bat-1",
                    Name = "Battery 1",
                    VoltageVolts = Finite(At(FenixVariables.SystemsIndex.Battery1Voltage), observedAt),
                },

                // Same reasoning as the yellow circuit: BAT2 exists, but its only generic source (index 2) is
                // confirmed wrong on Fenix.
                new BatteryTelemetry { Id = "bat-2", Name = "Battery 2" },
            ],
        };
    }

    /// <summary>ADIRS selector: 0 OFF, 1 NAV, 2 ATT (proven live on all three, full cycle). Anything else: Unknown.</summary>
    internal static TelemetryValue<InertialReferenceMode> IrMode(double raw, DateTimeOffset observedAt) => raw switch
    {
        0.0 => TelemetryValue<InertialReferenceMode>.Known(InertialReferenceMode.Off, observedAt),
        1.0 => TelemetryValue<InertialReferenceMode>.Known(InertialReferenceMode.Navigation, observedAt),
        2.0 => TelemetryValue<InertialReferenceMode>.Known(InertialReferenceMode.Attitude, observedAt),
        _ => TelemetryValue<InertialReferenceMode>.Unknown,
    };

    /// <summary>A two-state switch or light: exactly 0 or 1. Anything else: Unknown.</summary>
    internal static TelemetryValue<bool> Discrete(double raw, DateTimeOffset observedAt) => raw switch
    {
        0.0 => TelemetryValue<bool>.Known(false, observedAt),
        1.0 => TelemetryValue<bool>.Known(true, observedAt),
        _ => TelemetryValue<bool>.Unknown,
    };

    private static InertialReferenceTelemetry Ir(int index, double raw, DateTimeOffset observedAt) =>
        new() { Index = index, Mode = IrMode(raw, observedAt) };

    private static HydraulicSystemTelemetry Circuit(string id, string name, double psi, double reservoirPercent, DateTimeOffset observedAt) =>
        new()
        {
            Id = id,
            Name = name,
            PressurePsi = Finite(psi, observedAt),
            ReservoirPercent = Finite(reservoirPercent, observedAt),
        };

    /// <summary>A measured quantity: Known when finite, Unknown otherwise (the variable answered with no number).</summary>
    private static TelemetryValue<double> Finite(double raw, DateTimeOffset observedAt) =>
        double.IsFinite(raw) ? TelemetryValue<double>.Known(raw, observedAt) : TelemetryValue<double>.Unknown;

    private static void RequireCount(IReadOnlyList<double> raw, int expected)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count != expected)
        {
            throw new ArgumentException($"Expected {expected} raw values, got {raw.Count}.", nameof(raw));
        }
    }
}
