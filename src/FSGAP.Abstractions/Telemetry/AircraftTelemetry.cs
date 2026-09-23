namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// A normalized, vendor-neutral snapshot of the aircraft state at a point in time.
/// </summary>
/// <remarks>
/// <para>
/// Scalar readings use <see cref="TelemetryValue{T}"/> so that "unavailable" and "unknown" are never confused
/// with <c>false</c> or <c>0</c>.
/// </para>
/// <para>
/// Systems that exist in varying numbers (engines, inertial references, pumps, buses...) are exposed as
/// collections. An empty collection means the provider reports no such system; whether that is because the
/// aircraft has none or because the provider cannot read them is answered by
/// <see cref="Capabilities.TelemetryCapabilities"/>, not by the snapshot.
/// </para>
/// </remarks>
public sealed record AircraftTelemetry
{
    /// <summary>Time at which the snapshot was taken.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Position, attitude and speeds.</summary>
    public FlightStateTelemetry Flight { get; init; } = new();

    /// <summary>Engines, ordered by <see cref="EngineTelemetry.Index"/>.</summary>
    public IReadOnlyList<EngineTelemetry> Engines { get; init; } = [];

    /// <summary>Auxiliary power unit.</summary>
    public ApuTelemetry Apu { get; init; } = new();

    /// <summary>Inertial reference units (IRS, ADIRU, AHRS...), ordered by <see cref="InertialReferenceTelemetry.Index"/>.</summary>
    public IReadOnlyList<InertialReferenceTelemetry> InertialReferences { get; init; } = [];

    /// <summary>Fuel pumps.</summary>
    public IReadOnlyList<FuelPumpTelemetry> FuelPumps { get; init; } = [];

    /// <summary>Electrical buses.</summary>
    public IReadOnlyList<ElectricalBusTelemetry> ElectricalBuses { get; init; } = [];

    /// <summary>Hydraulic systems.</summary>
    public IReadOnlyList<HydraulicSystemTelemetry> HydraulicSystems { get; init; } = [];

    /// <summary>Fire detection zones other than engines and APU (cargo, lavatory, wheel well...).</summary>
    public IReadOnlyList<FireZoneTelemetry> FireZones { get; init; } = [];

    /// <summary>Flight control surfaces.</summary>
    public FlightControlsTelemetry FlightControls { get; init; } = new();

    /// <summary>Creates a snapshot in which every value is unavailable and every collection is empty.</summary>
    public static AircraftTelemetry Unavailable(DateTimeOffset timestamp) => new() { Timestamp = timestamp };
}
