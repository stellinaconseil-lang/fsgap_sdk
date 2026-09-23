namespace FSGAP.Abstractions.Configuration;

/// <summary>
/// Host-level configuration of FSGAP, supplied by the consuming application. Vendor-specific settings (for example a
/// provider's endpoints or polling rates) belong to that provider's own options, never here.
/// </summary>
public sealed record FsgapOptions
{
    /// <summary>
    /// Name identifying the application to the simulator and in diagnostics (for example <c>FSHangar</c> or
    /// <c>FLIPPP</c>).
    /// </summary>
    public required string ApplicationName { get; init; }

    /// <summary>
    /// Absolute directory where FSGAP components keep their local data (caches such as the installed-aircraft
    /// catalog). Each application supplies its own, so two applications never share or corrupt each other's files.
    /// </summary>
    public required string DataDirectory { get; init; }

    /// <summary>Connection behaviour.</summary>
    public SimulatorConnectionOptions Connection { get; init; } = new();

    /// <summary>Telemetry behaviour.</summary>
    public TelemetryOptions Telemetry { get; init; } = new();

    /// <summary>Checks the whole configuration.</summary>
    /// <exception cref="ArgumentException">A value is missing or invalid.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApplicationName, nameof(ApplicationName));
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory, nameof(DataDirectory));
        if (!Path.IsPathFullyQualified(DataDirectory))
        {
            throw new ArgumentException("DataDirectory must be an absolute path.", nameof(DataDirectory));
        }

        ArgumentNullException.ThrowIfNull(Connection, nameof(Connection));
        ArgumentNullException.ThrowIfNull(Telemetry, nameof(Telemetry));
        Connection.Validate();
        Telemetry.Validate();
    }
}
