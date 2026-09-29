namespace FSGAP.Abstractions.Telemetry;

/// <summary>Selected mode of a fuel pump control, as the cockpit sets it.</summary>
/// <remarks>
/// A control position, not an operating state: <see cref="Auto"/> means the aircraft decides whether the pump runs,
/// so it says nothing about the pump actually running. There is deliberately no "unknown" member: a mode that cannot
/// be read is a <see cref="TelemetryValue{T}"/> in the <see cref="ValueState.Unknown"/> or
/// <see cref="ValueState.Unavailable"/> state, never an enum value.
/// </remarks>
public enum FuelPumpMode
{
    /// <summary>Switched off.</summary>
    Off = 0,

    /// <summary>Automatic: the aircraft switches the pump on and off as required.</summary>
    Auto = 1,

    /// <summary>Switched on.</summary>
    On = 2,
}
