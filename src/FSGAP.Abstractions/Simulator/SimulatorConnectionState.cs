namespace FSGAP.Abstractions.Simulator;

/// <summary>State of the connection between FSGAP and the simulator.</summary>
public enum SimulatorConnectionState
{
    /// <summary>Not started, or stopped by the host. No attempt is being made.</summary>
    Disconnected = 0,

    /// <summary>First connection attempt in progress.</summary>
    Connecting,

    /// <summary>The simulator is not running or not reachable. Attempts are retried periodically.</summary>
    WaitingForSimulator,

    /// <summary>Connected; data can flow.</summary>
    Connected,

    /// <summary>An established connection was lost. Attempts are retried periodically.</summary>
    Reconnecting,

    /// <summary>
    /// Terminal error (for example the native simulator library is missing, or the configuration is invalid). No
    /// further attempt is made until the host stops and starts the connection again.
    /// </summary>
    Faulted,
}
