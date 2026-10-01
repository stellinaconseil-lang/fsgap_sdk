using FSGAP.Abstractions.Configuration;

namespace FSGAP;

/// <summary>Configuration of an <see cref="FsgapRuntime"/>.</summary>
public sealed record FsgapRuntimeOptions
{
    /// <summary>SDK settings: application name, data directory, connection and telemetry options.</summary>
    public required FsgapOptions Sdk { get; init; }

    /// <summary>
    /// Whether sessions may act on the aircraft: trigger and clear failures, apply and restore controlled degradations.
    /// <see langword="true"/> by default. When <see langword="false"/>, sessions are read-only (identity, telemetry) and
    /// their failure and degradation capabilities are empty.
    /// </summary>
    public bool EnableAircraftCommands { get; init; } = true;

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sdk, nameof(Sdk));
        Sdk.Validate();
    }
}
