namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// A simulator variable to read, by its simulator name and the unit to read it in (for MSFS: a SimVar such as
/// <c>HYDRAULIC PRESSURE:1</c> in <c>Psi</c>, or a local variable in <c>number</c>).
/// </summary>
/// <remarks>
/// A technical identifier for aircraft providers, used with <see cref="ISimulatorVariableReader"/>. It never appears
/// in normalized telemetry: providers translate what they read into <c>AircraftTelemetry</c>.
/// </remarks>
public sealed record SimulatorVariable
{
    /// <summary>Creates a variable reference.</summary>
    /// <param name="name">Simulator variable name.</param>
    /// <param name="unit">Unit to read it in.</param>
    /// <exception cref="ArgumentException">A value is null, empty or whitespace.</exception>
    public SimulatorVariable(string name, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        Name = name;
        Unit = unit;
    }

    /// <summary>Simulator variable name.</summary>
    public string Name { get; }

    /// <summary>Unit the value is read in.</summary>
    public string Unit { get; }
}
