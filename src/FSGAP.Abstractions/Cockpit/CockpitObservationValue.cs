namespace FSGAP.Abstractions.Cockpit;

/// <summary>The normalized type of a cockpit observation value.</summary>
public enum CockpitObservationValueKind
{
    /// <summary>An on/off control (for example a fire-handle light).</summary>
    Boolean,

    /// <summary>
    /// A discrete integer: a multi-position selector state (ADIRS mode) or a monotonic step counter (a fire-test
    /// pushbutton, which never returns to zero). Never a raw floating-point value.
    /// </summary>
    Integer,
}

/// <summary>Whether a cockpit observation currently has a value.</summary>
public enum CockpitObservationState
{
    /// <summary>The provider does not support this key. The default, so a zero-initialized value is never "known false".</summary>
    Unavailable,

    /// <summary>No value yet, or not read for the loaded aircraft (for example another aircraft is loaded).</summary>
    Unknown,

    /// <summary>A value was read.</summary>
    Known,
}

/// <summary>
/// One cockpit observation's current value: its normalized kind, whether it is known, and the value. Raw vendor
/// floating-point noise never leaks out — a boolean control is a <see cref="bool"/>, a selector/counter is an
/// integer. <c>default</c> is an unavailable boolean.
/// </summary>
public readonly record struct CockpitObservationValue
{
    private readonly long _value;

    private CockpitObservationValue(CockpitObservationValueKind kind, CockpitObservationState state, long value)
    {
        Kind = kind;
        State = state;
        _value = value;
    }

    /// <summary>The normalized value kind.</summary>
    public CockpitObservationValueKind Kind { get; }

    /// <summary>Whether the value is known, unknown or unavailable.</summary>
    public CockpitObservationState State { get; }

    /// <summary>True only when a value was actually read.</summary>
    public bool IsKnown => State == CockpitObservationState.Known;

    /// <summary>A known boolean observation.</summary>
    public static CockpitObservationValue Boolean(bool value) =>
        new(CockpitObservationValueKind.Boolean, CockpitObservationState.Known, value ? 1 : 0);

    /// <summary>A known integer observation (selector state or step counter).</summary>
    public static CockpitObservationValue Integer(long value) =>
        new(CockpitObservationValueKind.Integer, CockpitObservationState.Known, value);

    /// <summary>A known value with no read yet, for a key the provider supports.</summary>
    public static CockpitObservationValue Unknown(CockpitObservationValueKind kind) =>
        new(kind, CockpitObservationState.Unknown, 0);

    /// <summary>A key the provider does not support.</summary>
    public static CockpitObservationValue Unavailable(CockpitObservationValueKind kind) =>
        new(kind, CockpitObservationState.Unavailable, 0);

    /// <summary>Gets the boolean value when known and of kind <see cref="CockpitObservationValueKind.Boolean"/>.</summary>
    public bool TryGetBoolean(out bool value)
    {
        if (IsKnown && Kind == CockpitObservationValueKind.Boolean)
        {
            value = _value != 0;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>Gets the integer value when known and of kind <see cref="CockpitObservationValueKind.Integer"/>.</summary>
    public bool TryGetInteger(out long value)
    {
        if (IsKnown && Kind == CockpitObservationValueKind.Integer)
        {
            value = _value;
            return true;
        }

        value = 0;
        return false;
    }
}
