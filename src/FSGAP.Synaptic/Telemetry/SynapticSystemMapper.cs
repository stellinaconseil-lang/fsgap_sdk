using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Synaptic.Telemetry;

/// <summary>
/// Turns raw Synaptic readings into normalized values. Pure functions. A raw value outside the documented and observed
/// encoding is <see cref="ValueState.Unknown"/>, never rounded or guessed.
/// </summary>
internal static class SynapticSystemMapper
{
    /// <summary>Applies one overlay group read.</summary>
    /// <exception cref="ArgumentException"><paramref name="raw"/> does not have one value per variable.</exception>
    internal static SynapticSystemState Apply(IReadOnlyList<double> raw, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count != SynapticVariables.Systems.Count)
        {
            throw new ArgumentException($"Expected {SynapticVariables.Systems.Count} values, got {raw.Count}.", nameof(raw));
        }

        double At(SynapticVariables.Index index) => raw[(int)index];

        return new SynapticSystemState
        {
            FuelPumps =
            [
                Pump("left", "Left boost pump", At(SynapticVariables.Index.LeftBoostPump), observedAt),
                Pump("right", "Right boost pump", At(SynapticVariables.Index.RightBoostPump), observedAt),
            ],
            ApuMasterSwitchOn = ApuSwitchOn(At(SynapticVariables.Index.ApuSwitch), observedAt),
            ApuBleedSelectedOn = BleedSelectedOn(At(SynapticVariables.Index.ApuBleedOff), observedAt),
            // Latched logical state, not the pushbutton position: BLOCK 10B.3 saw 0 → 1 on press and 1 kept after the
            // momentary pushbutton was released, until the aircraft was reloaded ("engine fire control activated").
            EngineFirePushbuttons =
            [
                (1, Discrete(At(SynapticVariables.Index.LeftEngineFire), observedAt)),
                (2, Discrete(At(SynapticVariables.Index.RightEngineFire), observedAt)),
            ],
        };
    }

    /// <summary>
    /// A boost pump switch: 0 Off (<c>IsOn</c> false), 1 Auto (<c>IsOn</c> Unavailable: an automatic pump is neither on
    /// nor off), 2 On (<c>IsOn</c> true). Anything else: both Unknown. <c>Fault</c> has no source and stays Unavailable.
    /// </summary>
    internal static FuelPumpTelemetry Pump(string id, string name, double raw, DateTimeOffset observedAt)
    {
        var pump = new FuelPumpTelemetry { Id = id, Name = name };
        return raw switch
        {
            0.0 => pump with { Mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.Off, observedAt), IsOn = TelemetryValue<bool>.Known(false, observedAt) },
            1.0 => pump with { Mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.Auto, observedAt), IsOn = TelemetryValue<bool>.Unavailable },
            2.0 => pump with { Mode = TelemetryValue<FuelPumpMode>.Known(FuelPumpMode.On, observedAt), IsOn = TelemetryValue<bool>.Known(true, observedAt) },
            _ => pump with { Mode = TelemetryValue<FuelPumpMode>.Unknown, IsOn = TelemetryValue<bool>.Unknown },
        };
    }

    /// <summary>
    /// APU switch, reduced to "selected on or not": 0 is OFF; 1 and 2 are both non-off positions. BLOCK 10B.3 saw 1
    /// with the selector at RUN (after a brief START) and 2 after a held START, kept while the APU runs even once the
    /// spring-loaded START has returned to RUN; which of 1/2 means what is not relied on, so no selector mode is
    /// exposed. Anything else is Unknown.
    /// </summary>
    internal static TelemetryValue<bool> ApuSwitchOn(double raw, DateTimeOffset observedAt) => raw switch
    {
        0.0 => TelemetryValue<bool>.Known(false, observedAt),
        1.0 or 2.0 => TelemetryValue<bool>.Known(true, observedAt),
        _ => TelemetryValue<bool>.Unknown,
    };

    /// <summary>
    /// APU bleed switch "selected off" (documented Bool) inverted into "selected on": 0 → true, 1 → false. A control
    /// position; it says nothing about air actually flowing.
    /// </summary>
    internal static TelemetryValue<bool> BleedSelectedOn(double raw, DateTimeOffset observedAt) => raw switch
    {
        0.0 => TelemetryValue<bool>.Known(true, observedAt),
        1.0 => TelemetryValue<bool>.Known(false, observedAt),
        _ => TelemetryValue<bool>.Unknown,
    };

    /// <summary>A documented two-state pushbutton: exactly 0 or 1. Anything else: Unknown.</summary>
    internal static TelemetryValue<bool> Discrete(double raw, DateTimeOffset observedAt) => raw switch
    {
        0.0 => TelemetryValue<bool>.Known(false, observedAt),
        1.0 => TelemetryValue<bool>.Known(true, observedAt),
        _ => TelemetryValue<bool>.Unknown,
    };
}
