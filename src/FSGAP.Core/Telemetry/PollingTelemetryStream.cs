using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Core.Telemetry;

/// <summary>
/// Builds a telemetry stream by reading a snapshot at a fixed interval. Lets providers that can only read
/// snapshots implement <see cref="ITelemetryProvider.StreamAsync"/> in one line.
/// </summary>
public static class PollingTelemetryStream
{
    /// <summary>
    /// Yields a snapshot immediately, then one every <see cref="TelemetryStreamOptions.Interval"/>, until
    /// <paramref name="cancellationToken"/> is cancelled (the enumeration then throws
    /// <see cref="OperationCanceledException"/>).
    /// </summary>
    /// <param name="readSnapshot">Reads one snapshot.</param>
    /// <param name="options">Stream options, or <see langword="null"/> for <see cref="TelemetryStreamOptions.Default"/>.</param>
    /// <param name="timeProvider">Clock used to wait between snapshots; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Ends the stream.</param>
    public static IAsyncEnumerable<AircraftTelemetry> Create(
        Func<CancellationToken, Task<AircraftTelemetry>> readSnapshot,
        TelemetryStreamOptions? options = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readSnapshot);
        var interval = (options ?? TelemetryStreamOptions.Default).Interval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(options));

        return PollAsync(readSnapshot, interval, timeProvider ?? TimeProvider.System, cancellationToken);
    }

    private static async IAsyncEnumerable<AircraftTelemetry> PollAsync(
        Func<CancellationToken, Task<AircraftTelemetry>> readSnapshot,
        TimeSpan interval,
        TimeProvider timeProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await readSnapshot(cancellationToken).ConfigureAwait(false);
            await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
