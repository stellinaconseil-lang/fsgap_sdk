namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// A telemetry reading that distinguishes a known value from an unknown or unavailable one.
/// </summary>
/// <remarks>
/// <c>default(TelemetryValue&lt;T&gt;)</c> is <see cref="ValueState.Unavailable"/>: a provider that does not
/// set a property therefore reports "cannot supply", never <c>false</c> or <c>0</c>. Providers can assign a raw
/// value directly thanks to the implicit conversion from <typeparamref name="T"/>.
/// </remarks>
/// <typeparam name="T">Value type of the reading (e.g. <see cref="bool"/>, <see cref="double"/>, an enum).</typeparam>
public readonly record struct TelemetryValue<T>
    where T : struct
{
    private readonly T _value;

    private TelemetryValue(ValueState state, T value)
    {
        State = state;
        _value = value;
    }

    /// <summary>Whether the value is known, unknown or unavailable.</summary>
    public ValueState State { get; }

    /// <summary><see langword="true"/> when <see cref="State"/> is <see cref="ValueState.Known"/>.</summary>
    public bool IsKnown => State == ValueState.Known;

    /// <summary>The known value.</summary>
    /// <exception cref="InvalidOperationException">The value is not <see cref="ValueState.Known"/>.</exception>
    public T Value => IsKnown
        ? _value
        : throw new InvalidOperationException($"Telemetry value is {State}; check {nameof(IsKnown)} or use {nameof(TryGetValue)}.");

    /// <summary>A value the provider cannot supply.</summary>
    public static TelemetryValue<T> Unavailable => default;

    /// <summary>A value the provider supports but cannot currently read.</summary>
    public static TelemetryValue<T> Unknown => new(ValueState.Unknown, default);

    /// <summary>Creates a known value.</summary>
    public static TelemetryValue<T> Known(T value) => new(ValueState.Known, value);

    /// <summary>Converts a raw value into a known telemetry value.</summary>
    public static implicit operator TelemetryValue<T>(T value) => Known(value);

    /// <summary>Gets the value when it is known.</summary>
    /// <returns><see langword="true"/> when the value is known.</returns>
    public bool TryGetValue(out T value)
    {
        value = _value;
        return IsKnown;
    }

    /// <summary>Returns the known value, or <paramref name="fallback"/> when unknown or unavailable.</summary>
    public T GetValueOrDefault(T fallback) => IsKnown ? _value : fallback;

    /// <inheritdoc />
    public override string ToString() => IsKnown ? $"{_value}" : State.ToString();
}
