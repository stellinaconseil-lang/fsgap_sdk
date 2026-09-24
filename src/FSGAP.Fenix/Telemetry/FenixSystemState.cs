using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Fenix.Telemetry;

/// <summary>
/// What the Fenix-specific reads currently say, as normalized sections ready to be laid over the generic snapshot.
/// Immutable: each group read produces a new instance that replaces only that group's sections.
/// </summary>
internal sealed record FenixSystemState
{
    /// <summary>Nothing read yet (or discarded after an aircraft change).</summary>
    internal static FenixSystemState Empty { get; } = new();

    /// <summary>ADIRS IR1..IR3, from the cockpit group.</summary>
    internal IReadOnlyList<InertialReferenceTelemetry> InertialReferences { get; init; } = [];

    /// <summary>The six tank pump switches, from the cockpit group.</summary>
    internal IReadOnlyList<FuelPumpTelemetry> FuelPumps { get; init; } = [];

    /// <summary>Engine fire handles and fire lights, by engine index, from the cockpit group.</summary>
    internal IReadOnlyList<EngineFirePanel> EngineFirePanels { get; init; } = [];

    /// <summary>APU fire handle, from the cockpit group.</summary>
    internal TelemetryValue<bool> ApuFireHandlePulled { get; init; }

    /// <summary>Green, blue and yellow circuits, from the hydraulics group.</summary>
    internal IReadOnlyList<HydraulicSystemTelemetry> HydraulicSystems { get; init; } = [];
}

/// <summary>Fire panel state for one engine.</summary>
/// <param name="Index">1-based engine index, as in <see cref="EngineTelemetry.Index"/>.</param>
/// <param name="HandlePulled">Fire handle position.</param>
/// <param name="WarningLit">Fire pushbutton light: fire <i>or</i> test.</param>
internal sealed record EngineFirePanel(int Index, TelemetryValue<bool> HandlePulled, TelemetryValue<bool> WarningLit);
