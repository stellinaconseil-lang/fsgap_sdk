namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of the auxiliary power unit. All values stay unavailable on aircraft without an APU.</summary>
public sealed record ApuTelemetry
{
    /// <summary>Whether the APU is up to speed and ready to supply power or bleed air ("APU AVAIL").</summary>
    public TelemetryValue<bool> Available { get; init; }

    /// <summary>Whether the APU is running, including while starting or cooling down.</summary>
    public TelemetryValue<bool> Running { get; init; }

    /// <summary>Whether the APU master switch is on.</summary>
    public TelemetryValue<bool> MasterSwitchOn { get; init; }

    /// <summary>Whether APU bleed air is selected on.</summary>
    public TelemetryValue<bool> BleedOn { get; init; }

    /// <summary>Whether the APU fire detection reports a fire.</summary>
    public TelemetryValue<bool> FireDetected { get; init; }

    /// <summary>
    /// Whether the APU fire handle (fire pushbutton) is pulled / released. A crew action on the fire panel, not a
    /// fire indication. Covered by <c>TelemetryCapabilities.Fire</c>, not <c>TelemetryCapabilities.Apu</c>.
    /// </summary>
    public TelemetryValue<bool> FireHandlePulled { get; init; }
}
