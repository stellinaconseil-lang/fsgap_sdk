using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Core.Telemetry;

/// <summary>
/// Telemetry provider for sessions that cannot read any telemetry yet: every snapshot is
/// <see cref="AircraftTelemetry.Unavailable"/>. Pair it with <see cref="Abstractions.Capabilities.TelemetryCapabilities.None"/>.
/// </summary>
public sealed class UnavailableTelemetryProvider : ITelemetryProvider
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the provider.</summary>
    /// <param name="timeProvider">Clock used for snapshot timestamps and stream pacing; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public UnavailableTelemetryProvider(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AircraftTelemetry.Unavailable(_timeProvider.GetUtcNow()));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AircraftTelemetry> StreamAsync(
        TelemetryStreamOptions? options = null,
        CancellationToken cancellationToken = default) =>
        PollingTelemetryStream.Create(GetSnapshotAsync, options, _timeProvider, cancellationToken);
}
