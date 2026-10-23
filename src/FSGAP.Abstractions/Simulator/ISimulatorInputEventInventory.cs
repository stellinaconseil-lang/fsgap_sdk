namespace FSGAP.Abstractions.Simulator;

/// <summary>One input event (<c>B:</c>) the loaded aircraft declares: its name and the hash the simulator gave it.</summary>
/// <param name="Name">The input event name, as the aircraft declares it.</param>
/// <param name="Hash">The simulator's hash for this event in the loaded build.</param>
public sealed record SimulatorInputEvent(string Name, ulong Hash);

/// <summary>
/// Read-only inventory of the input events (<c>B:</c>) the loaded aircraft declares, on the host's existing simulator
/// connection. It only lists them: no input event is ever set, subscribed or mapped through it, and listing creates
/// nothing in the simulator (unlike reading an unknown local variable).
/// </summary>
public interface ISimulatorInputEventInventory
{
    /// <summary>Lists the input events of the aircraft loaded now.</summary>
    /// <exception cref="InvalidOperationException">The simulator is not connected.</exception>
    /// <exception cref="TimeoutException">The simulator did not answer within the time limit.</exception>
    Task<IReadOnlyList<SimulatorInputEvent>> EnumerateInputEventsAsync(CancellationToken cancellationToken = default);
}
