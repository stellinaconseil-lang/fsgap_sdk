namespace FSGAP.Abstractions.Failures;

/// <summary>
/// The system instance a failure applies to, e.g. engine 2, the APU, or hydraulic system "green".
/// </summary>
/// <remarks>
/// Indexes and ids use the same normalized keys as the telemetry model (<c>EngineTelemetry.Index</c>,
/// <c>FuelPumpTelemetry.Id</c>...), so a consumer can correlate failures with telemetry. Instances are created
/// through the static factories, which validate the designation.
/// </remarks>
public sealed record FailureTarget
{
    private FailureTarget(FailureTargetKind kind, int? index, string? id)
    {
        Kind = kind;
        Index = index;
        Id = id;
    }

    /// <summary>Kind of system targeted.</summary>
    public FailureTargetKind Kind { get; }

    /// <summary>1-based index for numbered systems (engines, inertial references); otherwise <see langword="null"/>.</summary>
    public int? Index { get; }

    /// <summary>Normalized id for named systems (pumps, buses, hydraulic systems); otherwise <see langword="null"/>.</summary>
    public string? Id { get; }

    /// <summary>The aircraft as a whole.</summary>
    public static FailureTarget Aircraft { get; } = new(FailureTargetKind.Aircraft, null, null);

    /// <summary>The auxiliary power unit.</summary>
    public static FailureTarget Apu { get; } = new(FailureTargetKind.Apu, null, null);

    /// <summary>An engine.</summary>
    /// <param name="index">1-based engine index.</param>
    public static FailureTarget Engine(int index) => Numbered(FailureTargetKind.Engine, index);

    /// <summary>An inertial reference unit.</summary>
    /// <param name="index">1-based unit index.</param>
    public static FailureTarget InertialReference(int index) => Numbered(FailureTargetKind.InertialReference, index);

    /// <summary>A fuel pump.</summary>
    /// <param name="id">Normalized pump id, as in <c>FuelPumpTelemetry.Id</c>.</param>
    public static FailureTarget FuelPump(string id) => Named(FailureTargetKind.FuelPump, id);

    /// <summary>A hydraulic system.</summary>
    /// <param name="id">Normalized system id, as in <c>HydraulicSystemTelemetry.Id</c>.</param>
    public static FailureTarget HydraulicSystem(string id) => Named(FailureTargetKind.HydraulicSystem, id);

    /// <summary>An electrical bus.</summary>
    /// <param name="id">Normalized bus id, as in <c>ElectricalBusTelemetry.Id</c>.</param>
    public static FailureTarget ElectricalBus(string id) => Named(FailureTargetKind.ElectricalBus, id);

    /// <inheritdoc />
    public override string ToString() => Index is { } index ? $"{Kind} {index}"
        : Id is { } id ? $"{Kind} {id}"
        : Kind.ToString();

    private static FailureTarget Numbered(FailureTargetKind kind, int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        return new FailureTarget(kind, index, null);
    }

    private static FailureTarget Named(FailureTargetKind kind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new FailureTarget(kind, null, id);
    }
}
