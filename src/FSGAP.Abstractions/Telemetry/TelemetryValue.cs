namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// A telemetry reading that distinguishes a known value from an unknown or unavailable one, and records when it
/// was observed.
/// </summary>
/// <remarks>
/// <para>
/// <c>default(TelemetryValue&lt;T&gt;)</c> is <see cref="ValueState.Unavailable"/>: a provider that does not set a
/// property therefore reports "cannot supply", never <c>false</c> or <c>0</c>.
/// </para>
/// <para>
/// A known value always carries <see cref="ObservedAt"/>, the time it was read from the source. A known value
/// older than the freshness limit is turned into <see cref="ValueState.Unknown"/> by
/// <see cref="ExpireIfOlderThan"/>. The resulting value keeps its <see cref="ObservedAt"/>, so its age remains
/// visible while its content is no longer usable.
/// </para>
/// </remarks>
/// <typeparam name="T">Value type of the reading (e.g. <see cref="bool"/>, <see cref="double"/>, an enum).</typeparam>
public readonly record struct TelemetryValue<T>
    where T : struct
{
    private readonly T _value;

    private TelemetryValue(ValueState state, T value, DateTimeOffset? observedAt)
    {
        State = state;
        _value = value;
        ObservedAt = observedAt;
    }

    /// <summary>Whether the value is known, unknown or unavailable.</summary>
    public ValueState State { get; }

    /// <summary>
    /// When the value was last observed at its source: set for known values, and for unknown values that became
    /// stale; <see langword="null"/> otherwise.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary><see langword="true"/> when <see cref="State"/> is <see cref="ValueState.Known"/>.</summary>
    public bool IsKnown => State == ValueState.Known;

    /// <summary>The known value.</summary>
    /// <exception cref="InvalidOperationException">The value is not <see cref="ValueState.Known"/>.</exception>
    public T Value => IsKnown
        ? _value
        : throw new InvalidOperationException($"Telemetry value is {State}; check {nameof(IsKnown)} or use {nameof(TryGetValue)}.");

    /// <summary>A value the provider cannot supply.</summary>
    public static TelemetryValue<T> Unavailable => default;

    /// <summary>A value the provider supports but has never been able to read.</summary>
    public static TelemetryValue<T> Unknown => new(ValueState.Unknown, default, null);

    /// <summary>Creates a known value.</summary>
    /// <param name="value">The reading.</param>
    /// <param name="observedAt">When the reading was taken at its source.</param>
    public static TelemetryValue<T> Known(T value, DateTimeOffset observedAt) => new(ValueState.Known, value, observedAt);

    /// <summary>Gets the value when it is known.</summary>
    /// <returns><see langword="true"/> when the value is known.</returns>
    public bool TryGetValue(out T value)
    {
        value = _value;
        return IsKnown;
    }

    /// <summary>Returns the known value, or <paramref name="fallback"/> when unknown or unavailable.</summary>
    public T GetValueOrDefault(T fallback) => IsKnown ? _value : fallback;

    /// <summary>Age of the observation at <paramref name="now"/>, or <see langword="null"/> when never observed.</summary>
    public TimeSpan? GetAge(DateTimeOffset now) => now - ObservedAt;

    /// <summary>
    /// Returns this value, unless it is known and was observed more than <paramref name="maxAge"/> before
    /// <paramref name="now"/>. In that case it returns an <see cref="ValueState.Unknown"/> value that keeps
    /// <see cref="ObservedAt"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is negative.</exception>
    public TelemetryValue<T> ExpireIfOlderThan(DateTimeOffset now, TimeSpan maxAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);
        return IsKnown && now - ObservedAt > maxAge
            ? new TelemetryValue<T>(ValueState.Unknown, default, ObservedAt)
            : this;
    }

    /// <inheritdoc />
    public override string ToString() => IsKnown ? $"{_value}" : State.ToString();
}
