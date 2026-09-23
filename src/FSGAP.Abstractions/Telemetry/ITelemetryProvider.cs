namespace FSGAP.Abstractions.Telemetry;

/// <summary>Reads normalized telemetry for the attached aircraft.</summary>
/// <remarks>
/// Implementations must be safe to call concurrently: consumers may take snapshots while one or more streams
/// are being enumerated.
/// </remarks>
public interface ITelemetryProvider
{
    /// <summary>Reads the current telemetry snapshot.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams telemetry snapshots until <paramref name="cancellationToken"/> is cancelled. Each enumeration is an
    /// independent subscription.
    /// </summary>
    /// <param name="options">Stream options, or <see langword="null"/> for <see cref="TelemetryStreamOptions.Default"/>.</param>
    /// <param name="cancellationToken">Ends the stream.</param>
    IAsyncEnumerable<AircraftTelemetry> StreamAsync(
        TelemetryStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}
