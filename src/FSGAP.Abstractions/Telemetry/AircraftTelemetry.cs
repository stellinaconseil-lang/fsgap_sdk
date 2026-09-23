using FSGAP.Abstractions.Internal;

namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// A normalized, vendor-neutral, immutable snapshot of the aircraft state at a point in time.
/// </summary>
/// <remarks>
/// <para>
/// Scalar readings use <see cref="TelemetryValue{T}"/> so that "unavailable" and "unknown" are never confused
/// with <c>false</c> or <c>0</c>, and so that each reading carries the time it was observed.
/// </para>
/// <para>
/// Snapshots are immutable: sections are records with init-only properties, and every collection is copied when
/// assigned, so neither the provider nor a consumer can change a snapshot another consumer holds. Derive a new
/// snapshot with a <c>with</c> expression instead of mutating one.
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
    private readonly IReadOnlyList<EngineTelemetry> _engines = [];
    private readonly IReadOnlyList<InertialReferenceTelemetry> _inertialReferences = [];
    private readonly IReadOnlyList<FuelPumpTelemetry> _fuelPumps = [];
    private readonly IReadOnlyList<ElectricalBusTelemetry> _electricalBuses = [];
    private readonly IReadOnlyList<HydraulicSystemTelemetry> _hydraulicSystems = [];
    private readonly IReadOnlyList<FireZoneTelemetry> _fireZones = [];

    /// <summary>Time at which the snapshot was taken.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Position, attitude and speeds.</summary>
    public FlightStateTelemetry Flight { get; init; } = new();

    /// <summary>Flight-envelope warnings.</summary>
    public WarningsTelemetry Warnings { get; init; } = new();

    /// <summary>Engines, ordered by <see cref="EngineTelemetry.Index"/>. The assigned collection is copied.</summary>
    public IReadOnlyList<EngineTelemetry> Engines
    {
        get => _engines;
        init => _engines = ReadOnlyCopy.Of(value, nameof(Engines));
    }

    /// <summary>Auxiliary power unit.</summary>
    public ApuTelemetry Apu { get; init; } = new();

    /// <summary>
    /// Inertial reference units (IRS, ADIRU, AHRS...), ordered by <see cref="InertialReferenceTelemetry.Index"/>.
    /// The assigned collection is copied.
    /// </summary>
    public IReadOnlyList<InertialReferenceTelemetry> InertialReferences
    {
        get => _inertialReferences;
        init => _inertialReferences = ReadOnlyCopy.Of(value, nameof(InertialReferences));
    }

    /// <summary>Fuel pumps. The assigned collection is copied.</summary>
    public IReadOnlyList<FuelPumpTelemetry> FuelPumps
    {
        get => _fuelPumps;
        init => _fuelPumps = ReadOnlyCopy.Of(value, nameof(FuelPumps));
    }

    /// <summary>Electrical buses. The assigned collection is copied.</summary>
    public IReadOnlyList<ElectricalBusTelemetry> ElectricalBuses
    {
        get => _electricalBuses;
        init => _electricalBuses = ReadOnlyCopy.Of(value, nameof(ElectricalBuses));
    }

    /// <summary>Hydraulic systems. The assigned collection is copied.</summary>
    public IReadOnlyList<HydraulicSystemTelemetry> HydraulicSystems
    {
        get => _hydraulicSystems;
        init => _hydraulicSystems = ReadOnlyCopy.Of(value, nameof(HydraulicSystems));
    }

    /// <summary>
    /// Fire detection zones other than engines and APU (cargo, lavatory, wheel well...). The assigned collection
    /// is copied.
    /// </summary>
    public IReadOnlyList<FireZoneTelemetry> FireZones
    {
        get => _fireZones;
        init => _fireZones = ReadOnlyCopy.Of(value, nameof(FireZones));
    }

    /// <summary>Landing gear handle and units.</summary>
    public LandingGearTelemetry LandingGear { get; init; } = new();

    /// <summary>Flap handle, flap surfaces and speed brake.</summary>
    public FlightControlsTelemetry FlightControls { get; init; } = new();

    /// <summary>Creates a snapshot in which every value is unavailable and every collection is empty.</summary>
    public static AircraftTelemetry Unavailable(DateTimeOffset timestamp) => new() { Timestamp = timestamp };
}
