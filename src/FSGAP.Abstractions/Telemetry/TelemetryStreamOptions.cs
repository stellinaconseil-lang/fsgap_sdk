namespace FSGAP.Abstractions.Telemetry;

/// <summary>Options for <see cref="ITelemetryProvider.StreamAsync"/>.</summary>
public sealed record TelemetryStreamOptions
{
    /// <summary>Default options: one snapshot per second.</summary>
    public static TelemetryStreamOptions Default { get; } = new();

    /// <summary>
    /// Desired interval between two snapshots. Providers never emit faster than this; push-based providers may
    /// emit slower when the simulator publishes less often.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);
}
