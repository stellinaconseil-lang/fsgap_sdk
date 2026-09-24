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

    /// <summary>Applies a cockpit group read: replaces IRs, pumps and fire panel, keeps hydraulics.</summary>
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

    /// <summary>Applies a hydraulics group read: replaces the hydraulic systems, keeps the rest.</summary>
    /// <exception cref="ArgumentException"><paramref name="raw"/> does not have one value per hydraulics variable.</exception>
    internal static FenixSystemState ApplyHydraulics(FenixSystemState current, IReadOnlyList<double> raw, DateTimeOffset observedAt)
    {
        RequireCount(raw, FenixVariables.Hydraulics.Count);
        return current with
        {
            HydraulicSystems =
            [
                Circuit("green", "Green", raw[(int)FenixVariables.HydraulicsIndex.GreenPressure], observedAt),
                Circuit("blue", "Blue", raw[(int)FenixVariables.HydraulicsIndex.BluePressure], observedAt),

                // Present so consumers see the circuit exists, but with no value: the only generic source (index 3)
                // is confirmed wrong on Fenix, and no Fenix variable for it was ever validated.
                new HydraulicSystemTelemetry { Id = "yellow", Name = "Yellow" },
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

    private static HydraulicSystemTelemetry Circuit(string id, string name, double psi, DateTimeOffset observedAt) =>
        new()
        {
            Id = id,
            Name = name,
            PressurePsi = double.IsFinite(psi) ? TelemetryValue<double>.Known(psi, observedAt) : TelemetryValue<double>.Unknown,
        };

    private static void RequireCount(IReadOnlyList<double> raw, int expected)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count != expected)
        {
            throw new ArgumentException($"Expected {expected} raw values, got {raw.Count}.", nameof(raw));
        }
    }
}
