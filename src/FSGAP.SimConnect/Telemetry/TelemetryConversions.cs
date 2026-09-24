namespace FSGAP.SimConnect.Telemetry;

/// <summary>
/// Every unit conversion between a raw SimVar reading and the FSGAP contract, in one place.
/// </summary>
/// <remarks>
/// <para>
/// One place because the audited applications had these constants written out at each call site, and a constant
/// repeated is a constant that eventually disagrees with itself. A wrong conversion here is not a cosmetic bug:
/// a fuel flow off by 2.2 or a vertical speed off by 60 produces numbers that look entirely plausible and are
/// simply false, which is the hardest kind of error to notice downstream.
/// </para>
/// <para>
/// The conversions are deliberately not folded into the SimVar unit strings. Asking SimConnect for
/// "Feet per minute" would work, but it would also move the arithmetic somewhere no test can reach and leave the
/// audit's recorded behaviour unverifiable. Requesting the audited unit and converting here keeps both the
/// evidence and the arithmetic visible.
/// </para>
/// </remarks>
internal static class TelemetryConversions
{
    /// <summary>Seconds in a minute. Vertical speed is requested in feet per second; the contract is per minute.</summary>
    internal const double SecondsPerMinute = 60.0;

    /// <summary>
    /// Kilograms in one pound, to the full internationally defined value (1 lb = 0.45359237 kg exactly).
    /// </summary>
    internal const double KilogramsPerPound = 0.45359237;

    /// <summary>Feet per second to feet per minute.</summary>
    internal static double FeetPerSecondToFeetPerMinute(double feetPerSecond) => feetPerSecond * SecondsPerMinute;

    /// <summary>Pounds per hour to kilograms per hour.</summary>
    internal static double PoundsPerHourToKilogramsPerHour(double poundsPerHour) => poundsPerHour * KilogramsPerPound;

    /// <summary>
    /// A SimConnect boolean, which arrives as a number. Anything other than exactly zero is true, which is how
    /// the audited applications read them.
    /// </summary>
    internal static bool ToBoolean(double raw) => raw != 0.0;

    /// <summary>
    /// MSFS pitch is positive nose DOWN; <c>FlightStateTelemetry.PitchDegrees</c> is positive nose up.
    /// </summary>
    /// <remarks>
    /// The audit flagged this sign explicitly as needing verification, and it is the kind of error that survives
    /// review because a pitch of -3 in the cruise looks as reasonable as +3. Both attitude conversions subtract from
    /// +0.0 rather than negate, so a level attitude is +0.0 and never the -0.0 that unary minus produces (found in
    /// live validation, where it printed as "-0.0").
    /// </remarks>
    internal static double SimPitchToNoseUpDegrees(double simPitchDegrees) => 0.0 - simPitchDegrees;

    /// <summary>
    /// MSFS bank is positive LEFT wing down; <c>FlightStateTelemetry.BankDegrees</c> is positive right wing down.
    /// </summary>
    internal static double SimBankToRightWingDownDegrees(double simBankDegrees) => 0.0 - simBankDegrees;

    /// <summary>
    /// Touchdown normal velocity, feet per second, to the contract's feet per minute (negative when descending),
    /// or <see langword="null"/> when the simulator holds no touchdown value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The raw SimVar's sign convention is not confirmed against the official documentation, so, exactly like the
    /// audited applications (FLIPPP <c>FlightAnalyzer</c>, FSHANGAR <c>CrashDetector</c>), it is treated as a
    /// magnitude: <c>-|v| × 60</c>. This does not depend on the unconfirmed sign.
    /// </para>
    /// <para>
    /// Exactly zero means no touchdown has been recorded (a real touchdown at exactly 0.0 ft/s does not happen), and
    /// becomes Unknown rather than a misleading "0 fpm landing". This is the simulator's own value mapped, not a
    /// touchdown detector: deciding when a landing happened stays application logic.
    /// </para>
    /// </remarks>
    internal static double? TouchdownToFeetPerMinute(double feetPerSecond) =>
        feetPerSecond == 0.0 ? null : -Math.Abs(FeetPerSecondToFeetPerMinute(feetPerSecond));

    /// <summary>
    /// <c>GEAR HANDLE POSITION</c> (percent, 0 = up, 100 = down) at or above which the handle counts as down. The
    /// handle is a two-position lever, so the midpoint only matters for a transient reading.
    /// </summary>
    internal const double GearHandleDownThresholdPercent = 50.0;

    /// <summary>Whether the gear handle percentage means "handle down".</summary>
    internal static bool IsGearHandleDown(double handlePercent) => handlePercent >= GearHandleDownThresholdPercent;
}
