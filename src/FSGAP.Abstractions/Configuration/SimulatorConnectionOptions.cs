namespace FSGAP.Abstractions.Configuration;

/// <summary>Connection behaviour, common to every simulator transport.</summary>
public sealed record SimulatorConnectionOptions
{
    /// <summary>Delay between two connection attempts while the simulator is absent or the connection was lost.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Checks the values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate() => ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RetryDelay, TimeSpan.Zero, nameof(RetryDelay));
}
