namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// Reads named simulator variables on the simulator connection that already exists. Read-only: there is no write.
/// </summary>
/// <remarks>
/// <para>
/// This is how an aircraft provider reads the variables only it knows about (for example a vendor's local cockpit
/// variables) without referencing the simulator library and without opening a connection of its own. The
/// connection's owner decides how the read is carried out; it never learns what the variables mean.
/// </para>
/// <para>
/// Implementations read the whole list as <b>one batched request</b> and return the values in the same order.
/// Reading the same list repeatedly is expected (a provider polls it), so implementations may cache whatever they
/// prepare per distinct list. Thread-safe.
/// </para>
/// </remarks>
public interface ISimulatorVariableReader
{
    /// <summary>Reads every variable of <paramref name="variables"/> in one request.</summary>
    /// <param name="variables">Variables to read; at least one, no duplicates.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One value per variable, in the order of <paramref name="variables"/>.</returns>
    /// <exception cref="ArgumentException">The list is empty or contains a duplicate.</exception>
    /// <exception cref="InvalidOperationException">The simulator is not connected.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default);
}
