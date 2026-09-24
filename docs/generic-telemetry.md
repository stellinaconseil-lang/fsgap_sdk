# Generic flight telemetry (FSGAP.SimConnect, since 0.5.0; completed in 0.9.0)

`SimConnectSimulator.Telemetry` is an `ITelemetryProvider` for whatever aircraft is loaded in MSFS. It reads stock
SimVars only. It never reads LVARs, never calls a vendor API, and does not know what aircraft it is reading.
Aircraft integrations compose on top of it (see [Fenix policy](#fenix-policy)).

0.9.0 adds the generic fields FSHANGAR uploads and FSGAP could not yet carry (audit gaps G-T4, G-T5, G-T6, the rest
of G-T8, G-T10 and G-T11, plus the APU bleed). This is the prerequisite for migrating FSHANGAR onto FSGAP without an
upload regression. The migration itself is the next block.

## One native connection

Telemetry uses **the transport's existing SimConnect connection**. It does not open a second client.

```text
SimConnectSimulator ── ISimConnectSessionFactory ── 1 × SimConnectClient
   ├─ identity poll          (2 s, then 5 s once stable)
   ├─ FAST group read        every 1 s    ┐
   ├─ NORMAL group read      every 2 s    │
   ├─ SLOW group read        every 5 s    ├─ ISimConnectSession.ReadTelemetryGroupAsync<TGroup>
   └─ ENVIRONMENT group read every 10 s   ┘
```

- Each group is one struct and **one native request**. SimConnect.NET builds one data definition from the struct
  and returns every field in a single response. This is the batched pattern the audited applications already used.
- About 1.8 group reads per second, plus 0.2–0.5 identity reads per second. 0.9.0 widened the existing structs
  instead of adding requests. The one new request is the 10 s weather group.
- The architecture tests check that only `SimConnectNetSessionFactory` creates clients and that only
  `SimConnectSimulator` holds a factory. The transport tests check that telemetry flows over a single opened session.

## Groups, SimVars and units

Names and units come from the audit ([audits/fenix-fsgap-mapping.md](audits/fenix-fsgap-mapping.md)), except for three
standard SimVars that neither audited application read. Those three were read successfully live in BLOCK 5. The
0.9.0 SimVars are exactly the ones FSHANGAR reads (`SimConnectVariableRegistry`), with the same units.

### FAST: every 1 s (26 SimVars)

| SimVar (unit requested) | Contract field | Conversion |
|---|---|---|
| `PLANE LATITUDE` / `PLANE LONGITUDE` (Degrees) | `Flight.LatitudeDegrees` / `LongitudeDegrees` | none; 1 Hz per ADR 0005 |
| `PLANE ALTITUDE` (Feet) | `Flight.AltitudeFeet` (MSL) | none |
| `PLANE ALT ABOVE GROUND` (Feet) | `Flight.HeightAboveGroundFeet` | none |
| — | `Flight.RadioAltitudeFeet` | **Unavailable.** A different quantity; no trusted generic SimVar |
| `AIRSPEED INDICATED` (Knots) | `Flight.IndicatedAirspeedKnots` | none |
| `GROUND VELOCITY` (Knots) | `Flight.GroundSpeedKnots` | none |
| `VERTICAL SPEED` (Feet per second) | `Flight.VerticalSpeedFeetPerMinute` | × 60 |
| `PLANE TOUCHDOWN NORMAL VELOCITY` (Feet per second) | `Flight.TouchdownVerticalSpeedFeetPerMinute` | `-|v| × 60`; exactly 0 → Unknown (no touchdown recorded) |
| `PLANE HEADING DEGREES MAGNETIC` (Degrees) *(not in the audit)* | `Flight.HeadingMagneticDegrees` | none |
| `PLANE PITCH DEGREES` (Degrees) | `Flight.PitchDegrees` (positive nose up) | sign inverted (MSFS: positive nose down) |
| `PLANE BANK DEGREES` (Degrees) | `Flight.BankDegrees` (positive right wing down) | sign inverted (MSFS: positive left wing down) |
| `G FORCE` (GForce) | `Flight.GLoad` | none |
| `SIM ON GROUND` (Bool) | `Flight.OnGround` | non-zero = true |
| `OVERSPEED WARNING` (Bool) | `Warnings.Overspeed` | non-zero = true; ≥ 1 Hz |
| `FLAP SPEED EXCEEDED` (Bool) | `Warnings.FlapSpeedExceeded` | idem |
| `GEAR SPEED EXCEEDED` (Bool) | `Warnings.GearSpeedExceeded` | idem |
| `STALL WARNING` (Bool) | `Warnings.Stall` | idem |
| `INCIDENCE ALPHA` (Degrees) — 0.9.0 | `Flight.AngleOfAttackDegrees` | none |
| `TOTAL WEIGHT` (Kilograms) — 0.9.0 | `Flight.GrossWeightKilograms` | none |
| `ACCELERATION BODY X/Y/Z` (GForce) — 0.9.0 | `Flight.BodyAccelerationXG` / `YG` / `ZG` | none. MSFS body axes: X right, Y up, Z forward. GForce verified live by FSHANGAR |
| `AILERON LEFT/RIGHT DEFLECTION PCT` (Percent) — 0.9.0 | `FlightControls.AileronLeftDeflectionPercent` / `AileronRightDeflectionPercent` | none |
| `ELEVATOR DEFLECTION PCT` (Percent) — 0.9.0 | `FlightControls.ElevatorDeflectionPercent` | none |
| `RUDDER DEFLECTION PCT` (Percent) — 0.9.0 | `FlightControls.RudderDeflectionPercent` | none |

### NORMAL: every 2 s (13 SimVars)

| SimVar (unit requested) | Contract field | Conversion |
|---|---|---|
| `GEAR HANDLE POSITION` (Percent) | `LandingGear.HandleDown` | ≥ 50 % = down |
| `GEAR CENTER POSITION` (Percent) | `LandingGear.Units["nose"]` | none |
| `GEAR LEFT POSITION` (Percent) *(not in the audit)* | `LandingGear.Units["left-main"]` | none |
| `GEAR RIGHT POSITION` (Percent) *(not in the audit)* | `LandingGear.Units["right-main"]` | none |
| `FLAPS HANDLE PERCENT` (Percent) | `FlightControls.FlapsHandlePercent` (the lever) | none |
| `TRAILING EDGE FLAPS LEFT/RIGHT PERCENT` (Percent) | `FlightControls.FlapSurfaces["trailing-left"/"trailing-right"]` (the surfaces) | none |
| `SPOILERS LEFT/RIGHT POSITION` (Percent) | `FlightControls.SpeedBrakeDeploymentPercent` | the larger side |
| `BRAKE LEFT/RIGHT POSITION` (Percent) — 0.9.0 | `LandingGear.BrakeLeftPercent` / `BrakeRightPercent` | **none**, see [Brakes](#brakes-no-100) |
| `STEER INPUT CONTROL` (Percent) — 0.9.0 | `LandingGear.SteeringInputPercent` | none (−100 full left … +100 full right) |
| `ANTISKID BRAKES ACTIVE` (Bool) — 0.9.0 | `LandingGear.AntiskidActive` | non-zero = true |

Flight controls is fed by two groups: the deflections come from FAST (1 s), the flaps and the speed brake from NORMAL
(2 s). Each group replaces only its own fields of the section, and each value keeps its own `ObservedAt`.

#### Brakes: no ×100

FSHANGAR requests `BRAKE LEFT/RIGHT POSITION` in "Percent", notes that it recorded a 0–1 fraction on 2026-08-31, and
multiplies by 100. FSGAP does **not** carry that multiplication over. A read-only probe on 2026-09-24 (same SimVar, same
SimConnect.NET 0.2.2, one connection, Fenix A319 with the parking brake set) read:

| Unit requested | Value |
|---|---|
| Percent | 99.9998688697815 |
| Percent Over 100 | 0.9999986886978149 |
| Position | 0.9999986886978149 |

SimConnect honours "Percent" here, and ×100 would have reported about 10 000 %. The FSHANGAR migration must drop its
×100 when it moves to FSGAP.

### SLOW: every 5 s (23 SimVars, engines 1 and 2)

| SimVar (unit requested) | Contract field | Conversion |
|---|---|---|
| `GENERAL ENG COMBUSTION:n` (Bool) | `Engines[n].Running` | non-zero = true |
| `TURB ENG N1:n` / `TURB ENG N2:n` (Percent) | `Engines[n].N1Percent` / `N2Percent` | none |
| `GENERAL ENG EXHAUST GAS TEMPERATURE:n` (Celsius) | `Engines[n].EgtCelsius` | none |
| `ENG FUEL FLOW PPH:n` (Pounds per hour) | `Engines[n].FuelFlowKilogramsPerHour` | × 0.45359237 |
| `GENERAL ENG STARTER ACTIVE:n` (Bool) — 0.9.0 | `Engines[n].StarterActive` | non-zero = true |
| `GENERAL ENG OIL TEMPERATURE:n` (Celsius) — 0.9.0 | `Engines[n].OilTemperatureCelsius` | none |
| `GENERAL ENG OIL PRESSURE:n` (Psi) — 0.9.0 | `Engines[n].OilPressurePsi` | none |
| `GENERAL ENG THROTTLE LEVER POSITION:n` (Percent) — 0.9.0 | `Engines[n].ThrottleLeverPercent` | none (negative in the reverse range) |
| `GENERAL ENG REVERSE THRUST ENGAGED:n` (Bool) — 0.9.0 | `Engines[n].ReverserEngaged` | non-zero = true |
| — | `Engines[n].FireDetected` | **Unavailable.** No generic source |
| `PNEUMATICS APU BLEED AIR` (Bool) — 0.9.0 | `Apu.BleedOn` | non-zero = true. The other APU values stay Unavailable |
| `PRESSURIZATION CABIN ALTITUDE` (Feet) — 0.9.0 | `Pressurization.CabinAltitudeFeet` | none |
| `PRESSURIZATION CABIN ALTITUDE RATE` (Feet per second) — 0.9.0 | `Pressurization.CabinAltitudeRateFeetPerMinute` | × 60 (as vertical speed) |

`Engines[n].Index` starts at 1. Two engines is an implementation limit, not a contract one: every audited
consumer flies an A32x.

### ENVIRONMENT: every 10 s (5 SimVars) — 0.9.0

| SimVar (unit requested) | Contract field | Conversion |
|---|---|---|
| `AMBIENT TEMPERATURE` (Celsius) | `Environment.OutsideAirTemperatureCelsius` | none |
| `AMBIENT WIND DIRECTION` (Degrees) | `Environment.WindDirectionDegreesTrue` | none |
| `AMBIENT WIND VELOCITY` (Knots) | `Environment.WindSpeedKnots` | none |
| `AMBIENT PRECIP STATE` (Mask) | `Environment.Precipitation` | 2 → `None`, 4 → `Rain`, 8 → `Snow`; any other value → **Unknown** (never guessed) |
| `AMBIENT PRECIP RATE` (Millimeters of Water) | `Environment.PrecipitationRateMillimeters` | none |

Ten seconds is the audited cadence: weather changes over minutes. Structural icing (`STRUCTURAL ICE PCT`) was never
read by either application and is not added.

Every conversion lives in `TelemetryConversions`, and every mapping in `GenericTelemetryMapper`. Both are pure and
tested, including against golden fixtures that recompute the audited applications' formulas. The fixtures cover
parked, taxi, takeoff, climb, cruise, approach, landing, gear in transit, flaps handle ahead of surfaces and overspeed;
the 0.9.0 fields are filled in for parked, cruise and landing.

In the generic telemetry, everything outside these groups stays **Unavailable** or empty:

- the APU apart from its bleed;
- inertial references and fuel pumps;
- electrical buses and batteries;
- hydraulics;
- fire zones.

Aircraft providers may supply some of these from their own sources
(Fenix: [fenix-system-telemetry.md](fenix-system-telemetry.md)). Batteries and hydraulic reservoirs are deliberately
not read generically, because their SimVar index meaning differs per aircraft: on Fenix, battery index 2 and hydraulic
index 3 are wrong.

## Snapshot, freshness and streaming

- **One immutable snapshot.** A group read replaces its own sections and keeps the others. Each merge builds a new
  record, so a snapshot already handed out never changes.
- **ObservedAt.** Every value carries its group's read time.
- **Freshness.** Freshness uses Core's `TelemetryFreshness` with `FsgapOptions.Telemetry.StaleAfter` (15 s by
  default). A value is still fresh at exactly `StaleAfter` and becomes Unknown just after it.
  - Snapshots are aged on each publication and on each `GetSnapshotAsync`.
  - A group that stops arriving turns Unknown while the others keep flowing.
  - Every 0.9.0 field is aged too; a reflection test fails if a `TelemetryValue` field is ever left out.
  - The 10 s weather group fits comfortably inside the default 15 s. An application that lowers `StaleAfter` below
    10 s will see weather flicker to Unknown between reads, which is accurate.
- **Streaming.** `StreamAsync` first yields the current snapshot, then at most one snapshot per
  `TelemetryStreamOptions.Interval` (1 s by default), always the latest.
  - Each enumeration is an independent latest-value subscription.
  - A slow consumer skips intermediate snapshots without holding anything up.
  - A consumer that throws ends only its own enumeration.
  - Disposing the simulator completes the streams.

## Lifecycle rules

| Event | Telemetry |
|---|---|
| No aircraft loaded (main menu, before the first identity read) | no group read; stays Unavailable |
| Aircraft change | reset to Unavailable. A read issued for the previous aircraft is dropped (generation counter) |
| A group fails (unsupported SimVar, native error) | only that group's values expire. One warning when it starts failing, one information line when it recovers. The connection stays up |
| Connection lost | the snapshot is re-published aged. Values turn Unknown once `StaleAfter` has passed, published without a poll |
| `StopAsync` | reset to Unavailable |
| `DisposeAsync` | streams complete |

## Fenix policy

The transport knows no aircraft. `FSGAP.Fenix` composes on it with Core's `TransformedTelemetryProvider`:

```csharp
new FenixAircraftProvider(catalog, genericTelemetry: simulator.Telemetry /* , simulatorVariables, aircraftDetector (0.6.0) */)
// session.Telemetry = generic snapshot → FenixGenericTelemetryPolicy.Apply → Fenix overlay (0.6.0) → Fenix snapshot
```

| Generic value | Fenix session | Reason |
|---|---|---|
| Flight state, position, attitude, speeds, VS, touchdown, G | **accepted** | stock flight model, identical in the audited applications |
| Angle of attack, gross weight, body accelerations (0.9.0) | **accepted** | audit §1.1 (GENERIC); read by FSHANGAR on Fenix |
| Warnings (overspeed, flap speed, gear speed, stall) | **accepted** | read by both applications on Fenix |
| Engines: running, N1, N2, EGT, fuel flow | **accepted** | audit §1.2 |
| Engines: oil, starter, thrust lever, reverser (0.9.0) | **accepted** | audit §1.2 (GENERIC). The "starter" listed as wrong on Fenix is the **APU** starter, which is not read |
| Gear handle and legs | **accepted** | audit §1.5 (centre leg); left and right legs read live |
| Wheel brakes, steering input, antiskid (0.9.0) | **accepted** | audit §1.5 (GENERIC); see live validation for steering |
| Flaps handle and trailing-edge surfaces | **accepted** | surfaces verified on Fenix (audit §1.5) |
| Control surface deflections (0.9.0) | **accepted** | audit §1.5 (GENERIC) |
| Cabin pressurization, weather (0.9.0) | **accepted** | audit §1.6 (GENERIC) |
| `SpeedBrakeDeploymentPercent` | **masked → Unavailable** | `SPOILERS LEFT/RIGHT POSITION` has the wrong scale on Fenix (audit §1.5) |
| `Apu.BleedOn` (0.9.0) | **masked → Unavailable** | audit §1.3: "not verified on Fenix; FSGAP.Fenix confirms it or marks it Unavailable". Not yet confirmed, see below |
| APU (RPM, starter, generator), engine anti-ice, slats, yellow hydraulics, BAT2 | not read generically; nothing to mask | "confirmed wrong on Fenix" in the audit |
| IRS, fuel pumps, fire panel, green/blue hydraulics | **supplied by the Fenix overlay since 0.6.0** | see [fenix-system-telemetry.md](fenix-system-telemetry.md) |
| Green/blue hydraulic reservoirs, BAT1 voltage (0.9.0) | **supplied by the Fenix overlay** | Fenix-specific SimVar indices, see [fenix-system-telemetry.md](fenix-system-telemetry.md) |
| APU operating state, electrical buses, yellow hydraulics, BAT2 | no validated Fenix source | Unavailable |

With generic telemetry, a Fenix session declares `FlightState`, `Warnings`, `Engines`, `LandingGear`,
`FlightControls`, `Pressurization` and `Environment`. When it also has a variable reader, it adds the system sections
(0.6.0; `Electrical` since 0.9.0). `FlightControls` stays declared because flaps are supported even though the speed
brake is masked. `Apu` is not declared: its one generic value is masked. Without generic telemetry, the session declares
`AircraftCapabilities.None`, as in 0.4.0.

**APU bleed on Fenix, for the FSHANGAR migration.** FSHANGAR currently uploads the unverified stock value. On a Fenix
session, FSGAP reports it as Unavailable. Unmasking it needs one live observation with the APU running and APU BLEED
selected on, compared with the ECAM BLEED page. That is a cockpit action and was not performed here.

The generic telemetry has no failure logic. Fenix failures (0.7.0) go through the Fenix EFB, not SimConnect; a
failure acts on the aircraft, and its effects appear in the telemetry like any other change ([fenix-failures.md](fenix-failures.md)).

## Live validation

**LIVE TEST, 2026-09-24 (0.5.0)**, MSFS 2024, Fenix A319 CFM (C-GBIA), parked at the gate at LFMN with both engines
running, sample run for 90 s:

- all three groups read without any error on one connection;
- plausible values:
  - position 43.66448 / 7.22689, altitude 22 ft, AGL 9 ft;
  - heading 222°;
  - N1 31.3 %, N2 73.3 %, EGT 535 °C, fuel flow 412 kg/h per engine;
  - gear handle down, legs 100/100/100, flaps 0;
- the speed brake is masked on the Fenix session (generic 0 → n/a);
- touchdown vertical speed stays Unknown before any landing.

**LIVE TEST, 2026-09-24 (0.9.0)**, same aircraft and situation, read-only. Two sample runs (30 s and 15 s) and a
read-only unit probe:

- all four groups read without any error on one connection; weather arrives after its first 10 s cycle;
- flight: AoA 0.8°, gross weight 56 211–56 284 kg (falling slowly with fuel burn), body accelerations 0.000 g;
- engines: oil 111 °C / 77 psi, thrust levers 12 %, starter off, reverser off, on both engines;
- brakes 100 / 100 with the parking brake set (`BRAKE PARKING POSITION` = 1 in the probe), antiskid off;
- deflections: ailerons 0 / 0, elevator −20 %, rudder 0;
- cabin altitude −145 ft at a field elevation of 22 ft, rate +10 fpm;
- weather: OAT 25.0 °C, wind 200° / 6 kt, precipitation mask 2 → `None`, rate 0.0;
- APU bleed: generic 0, APU switch 0, Fenix session n/a (masked);
- Fenix overlay: reservoirs green 97 %, blue 97 % (pressures 2816 / 2823 psi), BAT1 28.0 V, yellow and BAT2 n/a.

**Observed, not explained:**

- `STEER INPUT CONTROL` reads −99.99 % (probe: −0.9999 in "Percent Over 100"), steady, with the aircraft parked and
  nobody touching the controls. It is passed through unchanged. The likeliest cause is an unused hardware axis
  bound to steering; the probe cannot tell that apart from a Fenix artefact. Consumers should not treat it as a
  proven tiller position on Fenix.
- Elevator −20 % at rest; FSHANGAR recorded −0.43 % on 2026-08-31. It is passed through unchanged.

**Not verified live (AUTOMATED ONLY):**

- the pitch and bank sign conventions (the aircraft was static);
- values in motion: IAS, VS, warnings set to true, gear and flap transitions, touchdown;
- 0.9.0 values in motion: partial braking, reverser engaged, starter active, non-zero body accelerations, a cabin
  climb, rain or snow, APU bleed on;
- telemetry expiry after a real disconnection (MSFS must not be closed during validation).

The live sample (`samples/FSGAP.SimConnect.Console`) prints a compact telemetry view every 2 s, including the 0.9.0
fields, so a future flight can close these points.
