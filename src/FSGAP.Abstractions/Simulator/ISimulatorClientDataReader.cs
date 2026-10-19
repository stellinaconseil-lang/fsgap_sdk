namespace FSGAP.Abstractions.Simulator;

/// <summary>Optional read-only ClientData subscription on the host's existing simulator connection.</summary>
public interface ISimulatorClientDataReader
{
    /// <summary>
    /// Observes an untagged block on each producer SET (not only byte changes). Completes on connection loss,
    /// cancellation or protocol error. The caller must resubscribe after reconnect and discard the old generation.
    /// No producer heartbeat is guaranteed; an initial block may be cached by the simulator.
    /// The callback must be nonblocking; the memory is owned by this notification and may be retained.
    /// </summary>
    Task ObserveClientDataAsync(string name, int length, Action<ReadOnlyMemory<byte>> observed, CancellationToken cancellationToken = default);
}
