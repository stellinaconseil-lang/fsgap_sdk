namespace FSGAP.Abstractions.Configuration;

/// <summary>Telemetry behaviour, common to every provider.</summary>
public sealed record TelemetryOptions
{
    /// <summary>
    /// Maximum age of a known value in a snapshot. Older values are reported as <c>Unknown</c>. It must exceed
    /// the slowest sampling interval a provider uses. The default of 15 s tolerates two missed 5 s polls.
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Checks the values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate() => ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StaleAfter, TimeSpan.Zero, nameof(StaleAfter));
}
