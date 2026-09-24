using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

#pragma warning disable CS0649 // Fields are written by SimConnect.NET when it unmarshals the response.

/// <summary>
/// The generic MSFS SimVars FSGAP reads, split into three groups by how fast each value actually changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>One struct is one native data definition.</b> SimConnect.NET builds a definition from the struct's
/// attributes and returns every field in a single response, so a group of seventeen variables costs one native
/// read rather than seventeen. This keeps the batched pattern of the audited applications (one struct per
/// cadence).
/// </para>
/// <para>
/// <b>Names and units come from the audit</b> (docs/audits/fenix-fsgap-mapping.md), which recorded what
/// FSHANGAR and FLIPPP read against a real simulator. Three variables are not in the audited registry, because
/// neither application read them: <c>PLANE HEADING DEGREES MAGNETIC</c>, <c>GEAR LEFT POSITION</c> and
/// <c>GEAR RIGHT POSITION</c>. They are standard documented SimVars, the last two siblings of the audited
/// <c>GEAR CENTER POSITION</c>. They were validated live in BLOCK 5 (see docs/generic-telemetry.md).
/// </para>
/// <para>
/// <b>One invalid variable fails its whole group</b> (SimConnect answers the definition as a unit), which is why the
/// groups are independent and why every variable here has been read successfully against MSFS 2024.
/// </para>
/// <para>
/// <b>Numeric fields are <see cref="double"/>, including the booleans.</b> SimConnect delivers boolean SimVars as
/// numbers, and asking for a CLR <c>bool</c> adds a marshalling assumption for no benefit. The mapper turns
/// non-zero into <see langword="true"/> in one documented place.
/// </para>
/// </remarks>
internal struct FastGroupVars
{
    // -- position ---------------------------------------------------------------------------------------------
    //
    // ADR 0005: 1 Hz. The audited applications read position every ten seconds, a concession to their upload
    // budget rather than a property of the data. A SDK that resamples at 10 s would destroy information its
    // consumers cannot recover; an application that wants 10 s uploads can downsample what it is given.
    [SimConnect("PLANE LATITUDE", "Degrees")]
    public double LatitudeDegrees;

    [SimConnect("PLANE LONGITUDE", "Degrees")]
    public double LongitudeDegrees;

    [SimConnect("PLANE ALTITUDE", "Feet")]
    public double AltitudeFeet;

    [SimConnect("PLANE ALT ABOVE GROUND", "Feet")]
    public double HeightAboveGroundFeet;

    // -- speeds -----------------------------------------------------------------------------------------------
    [SimConnect("AIRSPEED INDICATED", "Knots")]
    public double IndicatedAirspeedKnots;

    [SimConnect("GROUND VELOCITY", "Knots")]
    public double GroundSpeedKnots;

    /// <summary>Feet per SECOND, as the audited applications requested it. The mapper converts; see
    /// <c>TelemetryConversions</c> for why the unit is not simply changed here.</summary>
    [SimConnect("VERTICAL SPEED", "Feet per second")]
    public double VerticalSpeedFeetPerSecond;

    /// <summary>Feet per second at the last touchdown. Distinct from vertical speed: this one does not change
    /// until the aircraft touches down again.</summary>
    [SimConnect("PLANE TOUCHDOWN NORMAL VELOCITY", "Feet per second")]
    public double TouchdownNormalVelocityFeetPerSecond;

    // -- attitude ---------------------------------------------------------------------------------------------
    [SimConnect("PLANE HEADING DEGREES MAGNETIC", "Degrees")]
    public double HeadingMagneticDegrees;

    /// <summary>MSFS reports pitch positive NOSE DOWN; the FSGAP contract is positive nose up. The mapper
    /// negates it, and that is the only place the sign is touched.</summary>
    [SimConnect("PLANE PITCH DEGREES", "Degrees")]
    public double PitchDegrees;

    /// <summary>MSFS reports bank positive LEFT wing down; the FSGAP contract is positive right wing down.</summary>
    [SimConnect("PLANE BANK DEGREES", "Degrees")]
    public double BankDegrees;

    [SimConnect("G FORCE", "GForce")]
    public double GLoad;

    // -- state ------------------------------------------------------------------------------------------------
    [SimConnect("SIM ON GROUND", "Bool")]
    public double OnGround;

    // -- warnings -------------------------------------------------------------------------------------------
    //
    // In this group rather than a slower one because FLIPPP scores exceedances: a five-second sample can miss an
    // overspeed entirely, and a warning that was missed is indistinguishable from one that never happened.
    [SimConnect("OVERSPEED WARNING", "Bool")]
    public double OverspeedWarning;

    [SimConnect("FLAP SPEED EXCEEDED", "Bool")]
    public double FlapSpeedExceeded;

    [SimConnect("GEAR SPEED EXCEEDED", "Bool")]
    public double GearSpeedExceeded;

    [SimConnect("STALL WARNING", "Bool")]
    public double StallWarning;
}

/// <summary>
/// Airframe configuration: gear, flaps and speed brake. Changes over seconds, not milliseconds.
/// </summary>
/// <remarks>
/// Read faster than the audited applications did (they used five seconds), because configuration at touchdown is
/// exactly what a landing analysis needs and five seconds can straddle a gear or flap transition.
/// </remarks>
internal struct NormalGroupVars
{
    /// <summary>The lever, not the gear. A handle down with the legs still travelling is a real and different
    /// state, which is why the contract carries both.</summary>
    [SimConnect("GEAR HANDLE POSITION", "Percent")]
    public double GearHandlePercent;

    [SimConnect("GEAR CENTER POSITION", "Percent")]
    public double GearCenterPercent;

    [SimConnect("GEAR LEFT POSITION", "Percent")]
    public double GearLeftPercent;

    [SimConnect("GEAR RIGHT POSITION", "Percent")]
    public double GearRightPercent;

    /// <summary>The lever. BLOCK 2 removed the ambiguous single "flaps percent" for this reason.</summary>
    [SimConnect("FLAPS HANDLE PERCENT", "Percent")]
    public double FlapsHandlePercent;

    /// <summary>The surface. Verified on Fenix by the audit, unlike the leading-edge equivalents.</summary>
    [SimConnect("TRAILING EDGE FLAPS LEFT PERCENT", "Percent")]
    public double FlapsLeftPercent;

    [SimConnect("TRAILING EDGE FLAPS RIGHT PERCENT", "Percent")]
    public double FlapsRightPercent;

    /// <summary>The audit records the scale as wrong on Fenix. It is read generically anyway and masked for
    /// Fenix by that provider's policy — FSGAP.SimConnect does not know what a Fenix is.</summary>
    [SimConnect("SPOILERS LEFT POSITION", "Percent")]
    public double SpoilersLeftPercent;

    [SimConnect("SPOILERS RIGHT POSITION", "Percent")]
    public double SpoilersRightPercent;
}

/// <summary>
/// Engine state. Slower because these are thermal and rotational quantities that do not step instantaneously,
/// and because the audited applications proved five seconds sufficient for them.
/// </summary>
/// <remarks>
/// Engines one and two only, and that is an implementation limit rather than a contract one: the contract's
/// <c>Engines</c> is a collection with no fixed size, and every consumer audited in BLOCK 1 flies A32x. Adding a
/// third and fourth engine means adding fields here and widening the loop in the mapper; nothing above it changes.
/// </remarks>
internal struct SlowGroupVars
{
    [SimConnect("GENERAL ENG COMBUSTION:1", "Bool")]
    public double Engine1Combustion;

    [SimConnect("TURB ENG N1:1", "Percent")]
    public double Engine1N1Percent;

    [SimConnect("TURB ENG N2:1", "Percent")]
    public double Engine1N2Percent;

    [SimConnect("GENERAL ENG EXHAUST GAS TEMPERATURE:1", "Celsius")]
    public double Engine1EgtCelsius;

    /// <summary>Pounds per hour. The FSGAP contract is kilograms per hour; the mapper converts.</summary>
    [SimConnect("ENG FUEL FLOW PPH:1", "Pounds per hour")]
    public double Engine1FuelFlowPoundsPerHour;

    [SimConnect("GENERAL ENG COMBUSTION:2", "Bool")]
    public double Engine2Combustion;

    [SimConnect("TURB ENG N1:2", "Percent")]
    public double Engine2N1Percent;

    [SimConnect("TURB ENG N2:2", "Percent")]
    public double Engine2N2Percent;

    [SimConnect("GENERAL ENG EXHAUST GAS TEMPERATURE:2", "Celsius")]
    public double Engine2EgtCelsius;

    [SimConnect("ENG FUEL FLOW PPH:2", "Pounds per hour")]
    public double Engine2FuelFlowPoundsPerHour;
}

#pragma warning restore CS0649

/// <summary>Which logical group a native read belongs to. Used for logging and failure isolation.</summary>
internal enum TelemetryGroup
{
    /// <summary>Position, attitude, speeds and warnings. 1 Hz.</summary>
    Fast,

    /// <summary>Gear, flaps and speed brake.</summary>
    Normal,

    /// <summary>Engines.</summary>
    Slow,
}
