namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// Writes one local (<c>L:</c>) cockpit variable of the user aircraft on the simulator connection that already exists.
/// Provider plumbing, not a consumer feature: an aircraft provider uses it to command the few vendor controls it has
/// qualified, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Bounded by contract: only local variables (name starting with <c>L:</c>), only finite numeric values, one variable per
/// call, never a simulation variable and never an event. The connection's owner carries the write out on its single
/// connection; it never learns what the variable means.
/// </para>
/// <para>
/// A completed call means the simulator accepted the request, not that the aircraft took the value: a caller that needs
/// the outcome reads the variable back (<see cref="ISimulatorVariableReader"/>). Thread-safe.
/// </para>
/// </remarks>
public interface ISimulatorVariableWriter
{
    /// <summary>Writes <paramref name="value"/> to the local variable <paramref name="variable"/> of the user aircraft.</summary>
    /// <param name="variable">A local variable: its name starts with <c>L:</c>.</param>
    /// <param name="value">The value, finite.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">Not a local variable, or the value is not finite.</exception>
    /// <exception cref="InvalidOperationException">The simulator is not connected; nothing was written.</exception>
    /// <exception cref="TimeoutException">The simulator did not take the request in time; it may or may not have been applied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task WriteAsync(SimulatorVariable variable, double value, CancellationToken cancellationToken = default);
}
