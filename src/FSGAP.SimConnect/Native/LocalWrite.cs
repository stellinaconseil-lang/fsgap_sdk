using FSGAP.Abstractions.Simulator;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// The transport-side bounds of <see cref="ISimulatorVariableWriter"/>: one local (<c>L:</c>) variable of the user aircraft,
/// a finite value, a time limit. Simulation variables and events are never written through it.
/// </summary>
internal static class LocalWrite
{
    /// <summary>How long a write may take before it is reported as <see cref="TimeoutException"/>.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <exception cref="ArgumentException">Not a local variable, or the value is not finite.</exception>
    internal static void Validate(SimulatorVariable variable, double value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (!variable.Name.StartsWith("L:", StringComparison.Ordinal) || variable.Name.Length < 3)
        {
            throw new ArgumentException($"'{variable.Name}' is not a local (L:) variable; only local variables can be written.", nameof(variable));
        }

        if (!double.IsFinite(value))
        {
            throw new ArgumentException("The value must be finite.", nameof(value));
        }
    }
}
