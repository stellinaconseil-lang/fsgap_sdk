using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Simulator;

/// <summary>General state of the simulation, independent of the aircraft.</summary>
public sealed record SimulatorState
{
    /// <summary>State before anything is known: pause unavailable, no crash.</summary>
    public static SimulatorState Initial { get; } = new();

    /// <summary>Whether the simulation is paused (any pause mode).</summary>
    public TelemetryValue<bool> Paused { get; init; }

    /// <summary>
    /// Number of crashes reported by the simulator since the connection was started. It only increases, so an
    /// observer that skipped intermediate states still detects that a crash happened.
    /// </summary>
    public int CrashCount { get; init; }

    /// <summary>When the most recent crash was reported, or <see langword="null"/> if none.</summary>
    public DateTimeOffset? LastCrashAt { get; init; }
}
