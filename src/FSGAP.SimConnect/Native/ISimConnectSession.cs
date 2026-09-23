namespace FSGAP.SimConnect.Native;

/// <summary>System events the transport subscribes to on every connection.</summary>
internal enum SimulatorSystemEvent
{
    /// <summary>SimConnect <c>Crashed</c>.</summary>
    Crashed,

    /// <summary>SimConnect <c>Pause_EX1</c>.</summary>
    Pause,
}

/// <summary>Raw aircraft identity strings as read from the simulator, before normalization.</summary>
internal readonly record struct RawAircraftIdentity(string? Title, string? AtcId, string? LiveryFolder, string? LiveryName);

/// <summary>
/// One open native connection to the simulator. This is the seam between the transport logic, which is fully
/// testable, and SimConnect.NET. An instance is owned by exactly one transport run and disposed exactly once.
/// </summary>
/// <remarks>
/// Events are raised on the native message-processing thread. Handlers must only record the information and
/// return; they must never block.
/// </remarks>
internal interface ISimConnectSession : IAsyncDisposable
{
    /// <summary>The connection was lost (simulator closed or connection dropped).</summary>
    event Action? Disconnected;

    /// <summary>The simulator reported a crash.</summary>
    event Action? Crashed;

    /// <summary>The simulator's pause state changed; the argument is <see langword="true"/> when paused.</summary>
    event Action<bool>? PauseChanged;

    /// <summary>Whether the native connection is still open, according to the library.</summary>
    bool IsConnected { get; }

    /// <summary>Subscribes to a system event.</summary>
    Task SubscribeAsync(SimulatorSystemEvent systemEvent, CancellationToken cancellationToken);

    /// <summary>Reads the identity of the user aircraft.</summary>
    Task<RawAircraftIdentity> ReadAircraftIdentityAsync(CancellationToken cancellationToken);
}

/// <summary>Opens native connections.</summary>
internal interface ISimConnectSessionFactory
{
    /// <summary>Opens a connection to the simulator.</summary>
    /// <exception cref="SimulatorUnavailableException">The simulator is not running or not reachable (retryable).</exception>
    Task<ISimConnectSession> ConnectAsync(string applicationName, CancellationToken cancellationToken);
}

/// <summary>The simulator is not running or refused the connection. Part of the normal lifecycle, retried.</summary>
internal sealed class SimulatorUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
