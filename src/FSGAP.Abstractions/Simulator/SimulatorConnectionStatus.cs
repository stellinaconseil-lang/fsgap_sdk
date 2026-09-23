namespace FSGAP.Abstractions.Simulator;

/// <summary>Current connection state, when it was entered and why.</summary>
public sealed record SimulatorConnectionStatus
{
    /// <summary>The initial status: <see cref="SimulatorConnectionState.Disconnected"/> since the epoch.</summary>
    public static SimulatorConnectionStatus Initial { get; } = new() { State = SimulatorConnectionState.Disconnected, Since = DateTimeOffset.UnixEpoch };

    /// <summary>Connection state.</summary>
    public required SimulatorConnectionState State { get; init; }

    /// <summary>When <see cref="State"/> was entered.</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>
    /// Human-readable detail for diagnostics (for example the last connection error). Not meant to be parsed.
    /// </summary>
    public string? Detail { get; init; }
}
