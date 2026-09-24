# Generic flight telemetry (FSGAP.SimConnect, 0.5.0)

`SimConnectSimulator.Telemetry` is an `ITelemetryProvider` for whatever aircraft is loaded in MSFS. It reads stock
SimVars only. It never reads LVARs, never calls a vendor API, and does not know what aircraft it is reading.
Aircraft integrations compose on top of it (see [Fenix policy](#fenix-policy)).

## One native connection

Telemetry uses **the transport's existing SimConnect connection**. It does not open a second client.

```text
SimConnectSimulator ── ISimConnectSessionFactory ── 1 × SimConnectClient
   ├─ identity poll        (2 s, then 5 s once stable)
   ├─ FAST group read      every 1 s    ┐
   ├─ NORMAL group read    every 2 s    ├─ ISimConnectSession.ReadTelemetryGroupAsync<TGroup>
   └─ SLOW group read      every 5 s    ┘
```

- Each group is one struct and **one native request**. SimConnect.NET builds one data definition from the struct
  and returns every field in a single response. This is the batched pattern the audited applications already used.
- About 1.7 group reads per second, plus 0.2–0.5 identity reads per second.
- The architecture tests check that only `SimConnectNetSessionFactory` creates clients and that only
  `SimConnectSimulator` holds a factory. The transport tests check that telemetry flows over a single opened session.

## Groups, SimVars and units

Names and units come from the audit ([audits/fenix-fsgap-mapping.md](audits/fenix-fsgap-mapping.md)), except for three
standard SimVars that neither audited application read. Those three were read successfully live in BLOCK 5.

### FAST: every 1 s (17 SimVars)

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

### NORMAL: every 2 s (9 SimVars)

| SimVar (unit requested) | Contract field | Conversion |
|---|---|---|
| `GEAR HANDLE POSITION` (Percent) | `LandingGear.HandleDown` | ≥ 50 % = down |
| `GEAR CENTER POSITION` (Percent) | `LandingGear.Units["nose"]` | none |
| `GEAR LEFT POSITION` (Percent) *(not in the audit)* | `LandingGear.Units["left-main"]` | none |
| `GEAR RIGHT POSITION` (Percent) *(not in the audit)* | `LandingGear.Units["right-main"]` | none |
| `FLAPS HANDLE PERCENT` (Percent) | `FlightControls.FlapsHandlePercent` (the lever) | none |
| `TRAILING EDGE FLAPS LEFT/RIGHT PERCENT` (Percent) | `FlightControls.FlapSurfaces["trailing-left"/"trailing-right"]` (the surfaces) | none |
| `SPOILERS LEFT/RIGHT POSITION` (Percent) | `FlightControls.SpeedBrakeDeploymentPercent` | the larger side |

### SLOW: every 5 s (10 SimVars, engines 1 and 2)

| SimVar (unit requested) | Contract field | Conversion |
|---|---|---|
| `GENERAL ENG COMBUSTION:n` (Bool) | `Engines[n].Running` | non-zero = true |
| `TURB ENG N1:n` / `TURB ENG N2:n` (Percent) | `Engines[n].N1Percent` / `N2Percent` | none |
| `GENERAL ENG EXHAUST GAS TEMPERATURE:n` (Celsius) | `Engines[n].EgtCelsius` | none |
| `ENG FUEL FLOW PPH:n` (Pounds per hour) | `Engines[n].FuelFlowKilogramsPerHour` | × 0.45359237 |
| — | `Engines[n].FireDetected` | **Unavailable.** No generic source |

`Engines[n].Index` starts at 1. Two engines is an implementation limit, not a contract one: every audited
consumer flies an A32x.

Every conversion lives in `TelemetryConversions`, and every mapping in `GenericTelemetryMapper`. Both are pure and
tested, including against golden fixtures (parked, taxi, takeoff, climb, cruise, approach, landing, gear in transit,
flaps handle ahead of surfaces, overspeed) that recompute the audited applications' formulas.

Everything outside these groups stays **Unavailable**: APU, inertial references, fuel pumps, electrical,
hydraulics and fire zones.

## Snapshot, freshness and streaming

- **One immutable snapshot.** A group read replaces its own sections and keeps the others. Each merge builds a new
  record, so a snapshot already handed out never changes.
- **ObservedAt.** Every value carries its group's read time.
- **Freshness.** Freshness uses Core's `TelemetryFreshness` with `FsgapOptions.Telemetry.StaleAfter` (15 s by
  default). A value is still fresh at exactly `StaleAfter` and becomes Unknown just after it.
  - Snapshots are aged on each publication and on each `GetSnapshotAsync`.
  - A group that stops arriving turns Unknown while the others keep flowing.
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
new FenixAircraftProvider(catalog, genericTelemetry: simulator.Telemetry)
// session.Telemetry = generic snapshot → FenixGenericTelemetryPolicy.Apply → Fenix snapshot
```

| Generic value | Fenix session | Reason |
|---|---|---|
| Flight state, position, attitude, speeds, VS, touchdown, G | **accepted** | stock flight model, identical in the audited applications |
| Warnings (overspeed, flap speed, gear speed, stall) | **accepted** | read by both applications on Fenix |
| Engines: running, N1, N2, EGT, fuel flow | **accepted** | audit §1.2 |
| Gear handle and legs | **accepted** | audit §1.5 (centre leg); left and right legs read live |
| Flaps handle and trailing-edge surfaces | **accepted** | surfaces verified on Fenix (audit §1.5) |
| `SpeedBrakeDeploymentPercent` | **masked → Unavailable** | `SPOILERS LEFT/RIGHT POSITION` has the wrong scale on Fenix (audit §1.5) |
| APU (RPM, starter, generator), engine anti-ice, slats, yellow hydraulics, BAT2 | not read generically; nothing to mask | "confirmed wrong on Fenix" in the audit |
| APU, IRS, fuel pumps, fire, hydraulics, electrical | **deferred to BLOCK 6** (Fenix values) | Unavailable until then |

A Fenix session declares `FlightState`, `Warnings`, `Engines`, `LandingGear` and `FlightControls`; `FlightControls`
stays declared because flaps are supported even though the speed brake is masked. Without generic telemetry, the
session declares `AircraftCapabilities.None`, as in 0.4.0.

## Live validation

**LIVE TEST, 2026-09-24**, MSFS 2024, Fenix A319 CFM (C-GBIA), parked at the gate at LFMN with both engines running,
sample run for 90 s:

- all three groups read without any error on one connection;
- plausible values:
  - position 43.66448 / 7.22689, altitude 22 ft, AGL 9 ft;
  - heading 222°;
  - N1 31.3 %, N2 73.3 %, EGT 535 °C, fuel flow 412 kg/h per engine;
  - gear handle down, legs 100/100/100, flaps 0;
- the speed brake is masked on the Fenix session (generic 0 → n/a);
- touchdown vertical speed stays Unknown before any landing.

**Not verified live (AUTOMATED ONLY):**

- the pitch and bank sign conventions (the aircraft was static);
- values in motion: IAS, VS, warnings set to true, gear and flap transitions, touchdown;
- telemetry expiry after a real disconnection (MSFS must not be closed during validation).

The live sample (`samples/FSGAP.SimConnect.Console`) prints a compact telemetry view every 2 s, so a future flight
can close these points.
