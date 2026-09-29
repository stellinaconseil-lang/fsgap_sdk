using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Synaptic.Telemetry;

/// <summary>What the Synaptic overlay currently says, as normalized values. Immutable.</summary>
internal sealed record SynapticSystemState
{
    /// <summary>Nothing read yet (or discarded after an aircraft change).</summary>
    internal static SynapticSystemState Empty { get; } = new();

    /// <summary>Left and right fuel boost pumps.</summary>
    internal IReadOnlyList<FuelPumpTelemetry> FuelPumps { get; init; } = [];

    /// <summary>APU switch in a non-off position.</summary>
    internal TelemetryValue<bool> ApuMasterSwitchOn { get; init; }

    /// <summary>APU bleed switch selected on (a control position, not bleed flow).</summary>
    internal TelemetryValue<bool> ApuBleedSelectedOn { get; init; }

    /// <summary>Engine fire pushbuttons, engines 1 (left) and 2 (right).</summary>
    internal IReadOnlyList<(int Index, TelemetryValue<bool> Pressed)> EngineFirePushbuttons { get; init; } = [];
}
