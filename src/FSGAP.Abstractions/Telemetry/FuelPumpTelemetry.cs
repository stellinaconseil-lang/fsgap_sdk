namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one fuel pump.</summary>
/// <remarks>
/// <para>
/// <see cref="Mode"/> (since 0.10) is the canonical representation of the pump control: <see cref="FuelPumpMode.Off"/>,
/// <see cref="FuelPumpMode.Auto"/> or <see cref="FuelPumpMode.On"/>.
/// </para>
/// <para>
/// <see cref="IsOn"/> is kept as the simple binary view, and is known only when the source genuinely supplies a binary
/// ON/OFF state. A provider never derives it from <see cref="FuelPumpMode.Auto"/>: an automatic pump is neither "on"
/// nor "off" from the cockpit's point of view, so with <c>Mode = Auto</c> the value of <see cref="IsOn"/> is
/// <see cref="ValueState.Unavailable"/>. Consumers that only understand <see cref="IsOn"/> therefore never receive a
/// guessed value.
/// </para>
/// </remarks>
public sealed record FuelPumpTelemetry
{
    /// <summary>
    /// Stable, provider-assigned normalized key of the pump (e.g. <c>left-1</c>, <c>center-left</c>).
    /// Never a vendor variable name.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name (e.g. "Left tank pump 1").</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether the pump is switched on: the binary control position, known only when the source supplies a binary
    /// ON/OFF state. <see cref="ValueState.Unavailable"/> when the control is in <see cref="FuelPumpMode.Auto"/>.
    /// </summary>
    public TelemetryValue<bool> IsOn { get; init; }

    /// <summary>
    /// Selected mode of the pump control (off, automatic, on). The canonical, richer form of <see cref="IsOn"/>; a
    /// control position, not an operating state.
    /// </summary>
    public TelemetryValue<FuelPumpMode> Mode { get; init; }

    /// <summary>Whether the pump reports a fault (e.g. low pressure).</summary>
    public TelemetryValue<bool> Fault { get; init; }
}
