# Synaptic A220-300 — discovery and live qualification (BLOCK 10A, 10A-LIVE)

Read-only audit of the Synaptic Simulations A220-300 against FSGAP_SDK 0.9.0.

- **BLOCK 10A** (2026-09-26, `dfc76d6`/`358dd55`): documentation-based audit. The aircraft was installed but not
  loaded.
- **BLOCK 10A-LIVE** (2026-09-26 evening and 2026-09-27 morning): the aircraft flown in MSFS 2024 with the discovery
  harness attached. **This version supersedes the documentation-only statuses**; every row now says whether it is
  `LIVE_VALIDATED`, `DOCUMENTATION_ONLY` or `NOT_TESTED`.

Nothing here is a production integration: no `SynapticAircraftProvider`, no Synaptic overlay, no failure provider, no
write to the aircraft (no LVAR write, no H-Event, no K-Event), no change to FSHANGAR, FLIPPP or fenixhangarweb.

> **BLOCK 10B.2 note.** The provider built from this audit now exists: `FSGAP.Synaptic`
> ([synaptic-a220.md](../synaptic-a220.md)), automated qualification only. Two deliberate departures from §7.3/§9:
> `L:A22X APU Switch` = 1 (never observed live) maps to Unknown instead of "on", and
> `LandingGear.AntiskidActive` is **not** masked (inconclusive, not proven wrong). Master alerts are deferred: no
> existing contract field maps cleanly. The audit text below is unchanged.
FSGAP stays 0.9.0. The block numbering follows the FSGAP BLOCK series; it is unrelated to the "BLOCK 10 — FLIPPP
migration" row of the extraction plan.

Companion files:

- [synaptic-a220-variable-mapping.csv](synaptic-a220-variable-mapping.csv): every officially documented Synaptic
  variable (397 names) with documented meaning, semantic tag, FSGAP classification and, since 10A-LIVE, a
  `live_status` / `live_observation` for the 57 variables read live.
- [fixtures/synaptic-a220/](fixtures/synaptic-a220/): eight sanitized live captures (the eighth is the landing rollout) (descriptor, generic FSGAP
  snapshot with value states, the 57 documented variables, 33 stock SimVars) for BLOCK 10B tests.

> **BLOCK 10B.1 note (0.10.0-preview.1, branch `feature/synaptic-a220`).** The two generic foundations this audit asked
> for now exist: `FuelPumpTelemetry.Mode` (`Off` / `Auto` / `On`, gap G-F1) and the vendor-neutral
> `IInstalledLiveryService` ([../simulator-installed-liveries.md](../simulator-installed-liveries.md)). `FSGAP.Synaptic`
> itself is **not** implemented yet.

## 0. Headline (10A-LIVE)

1. **Detection works without a vendor marker.** No descriptor field names Synaptic or iniBuilds. The product is
   identified by its exact preset titles plus `ATC MODEL` = `A220-300` and `ATC TYPE` = `223`.
2. **Generic flight data is sound; generic engine secondaries are not.** Position, speeds, attitude, gear, flaps,
   control surfaces, N1, N2 and EGT match the cockpit. Engine running, fuel flow, starter, oil temperature and oil
   pressure are wrong, the stock APU and electrical SimVars are dead, and cabin altitude disagrees with the EICAS.
3. **Validated generic coverage: 39 of 73 fields (53 %)** after the landing flight. 9 generic fields are wrong and must be masked, 3 need a
   Synaptic overlay, 13 have no source, 9 are still untested.
4. **Fuel pump AUTO is real and cannot be expressed by `IsOn`.** `FuelPumpMode` stays the one P0 contract change.
5. **Registration has no authoritative source for Marketplace liveries.** `ATC ID` is not bound to the livery (a Delta
   and an Air Baltic livery both reported `C-FFCO`, the Air France one `I-BMTO`, `C-FFCO`, then empty). Marketplace
   livery **files** cannot be scanned, but BLOCK 10A.5 showed that SimConnect **enumerates** all of them and that the
   livery folder carries a registration-shaped token that can be used as a *derived* value
   ([synaptic-a220-livery-discovery.md](synaptic-a220-livery-discovery.md)).
6. **Minimum overlay: 6 documented variables, one polling group** (8 with the master alerts).
7. **No failure interface.** `FailureCapabilities.None` is unchanged.

## 1. Live environment

| | |
|---|---|
| MSFS | MSFS 2024, Microsoft Store build `Microsoft.Limitless` 1.8.16.0 |
| Aircraft package | Marketplace streamed `fs24-inibuilds-aircraft-a220`; Synaptic version not observable (packed content; docs current = 1.0.10) |
| Session 1 (2026-09-26, 23:33–23:52 local) | LFKF Figari, cold and dark, three livery/preset combinations, batteries, ground power, APU selector, boost pumps, parking brake, hydraulic selectors. Engines not started (see §7.5). |
| Session 2 (2026-09-27, 10:23–12:22 local) | LFLC Clermont-Ferrand, gate, batteries and ground power on; APU, both engine starts, 44 % N1 run-up with flaps 2, taxi with tiller and brakes, takeoff runway 08, gear up, gear down with flaps FULL, gear and flaps up, climb through FL130 (flight still in progress when the audit was written; no landing). |
| Session 3 (2026-09-27, 16:04–17:15 local) | LFKF round trip: takeoff 16:07, cruise FL300, speedbrakes extended in the descent, autobrake LO, landing 17:12:11 with ground spoilers, manual then hard braking, APU off in flight. Reversers not used; boost pumps and anti-ice not cycled |
| Presets seen | `A220-300` (cabin) and `A220-300 - No Cabin` |
| Liveries seen | Delta (`DELTA N324DU`), Air Baltic (`AIR BALTIC YL-CSM`), Air France (`AIR FRANCE F-HZUF`) |
| Cockpit references | two EICAS captures by the pilot (engines at idle; 44 % N1, flaps 2), the MFD configuration page (engine variant), EICAS memos |
| Harness | `samples/FSGAP.SimConnect.Console -- --synaptic-probe --synaptic-fixture <dir>`, 57 documented variables + 33 stock SimVars every 5 s, generic telemetry every 2 s, 29 700 log lines in session 2, **0 read failures** |
| Native SimConnect connections | 1 (the sample owns one `SimConnectSimulator`; every read goes through it; unchanged architecture tests) |

## 2. Live descriptor

`ATC MODEL` and `ATC TYPE` were not read by FSGAP 0.9.0. 10A-LIVE adds them to the transport's identity request (gap
G-D1): they fill the existing, previously always-null `AircraftDescriptor.Model` and `.Manufacturer` verbatim. No
contract change, no inference.

| Field | Delta, cabin preset | Air Baltic, no-cabin preset | Air France, no-cabin preset | Invariant? |
|---|---|---|---|---|
| TITLE | `A220-300` | `A220-300 - No Cabin` | `A220-300 - No Cabin` | per preset |
| ATC ID | `C-FFCO` | `C-FFCO` | `I-BMTO`, then `C-FFCO`, then empty; empty all of session 2 | **not livery-bound** |
| ATC MODEL | `A220-300` | `A220-300` | `A220-300` | yes |
| ATC TYPE | `223` | `223` | `223` | yes |
| LIVERY FOLDER | `DELTA N324DU` | `AIR BALTIC YL-CSM` | `AIR FRANCE F-HZUF` | per livery |
| LIVERY NAME | `Delta A220-300` | `Air Baltic A220-300` | `Air France A220-300` | per livery |

Negative descriptors captured live in the same sessions:

| Aircraft | TITLE | ATC MODEL | ATC TYPE |
|---|---|---|---|
| iniBuilds A380 (two presets, two liveries) | `A380-800 EA Basic`, `A380-800 RR Basic` | `A380-800` | `Airbus` |
| Fenix A321 (10A) | `FenixA321 CFM WF SC` | not read then | not read then |

## 3. Detection conclusion

| Rule | Delta | Air Baltic | Air France | A380 | Fenix A321 | Grade after live |
|---|---|---|---|---|---|---|
| R1 TITLE contains `Synaptic` | no | no | no | no | no | **REJECT** (never fires) |
| R2 TITLE contains `A220`/`A223`, not Fenix/Asobo/FSLTL | fires | fires | fires | no | no | PLAUSIBLE, too loose |
| R3 `A22X` in TITLE or LIVERY FOLDER | no | no | no | no | no | **REJECT** |
| R4 TITLE contains `iniBuilds` and `A220` | no | no | no | no | no | **REJECT** |
| R5 TITLE contains `Airbus` | no | no | no | no | no | REJECT (control) |
| R6 ATC MODEL contains `A220`/`A223` | fires | fires | fires | no | no | PLAUSIBLE |
| R7 `Synaptic` in ATC TYPE, TITLE or folder | no | no | no | no | no | **REJECT** |

**Candidate production rule (for 10B, not implemented):**

```text
TITLE, trimmed, equals (ordinal, case-insensitive) one of
    "A220-300"
    "A220-300 - No Cabin"
AND ATC MODEL equals "A220-300"
AND ATC TYPE  equals "223"
```

- **Positive evidence.** The two titles are exactly the two presets of the installed simobject
  (`presets/inibuilds/a220-300`, `a220-300_nocabin`). `ATC TYPE = 223` is unusual: stock and other add-on aircraft
  report a manufacturer (`Airbus` on the iniBuilds A380), so the three-way combination is specific to this product's
  `aircraft.cfg`.
- **Negative evidence.** The iniBuilds A380 (same publisher) and the Fenix A321 do not match. Asobo and FSLTL A220
  models are AI traffic (`fs24-asobo-passiveaircraft-a220family`), never the user aircraft reported by `TITLE`. A
  third-party livery pack cannot change `TITLE`, `ATC MODEL` or `ATC TYPE`: they come from the preset, and a
  `livery.cfg` only selects it through `required_tags`.
- **Remaining risk.** There is no vendor marker at all, so the rule identifies the product by its configuration
  strings. A Synaptic update that renames a preset, a future A220-100, or another developer shipping identical strings
  would break or broaden it. BLOCK 10B should log unknown `A220` titles as "not supported" rather than guess, and
  keep the title list in one place.

## 4. Identity conclusion

| Field | Value | Evidence | Classification |
|---|---|---|---|
| Developer | `Synaptic Simulations` | simobject `synaptic_a220`, docs; nothing in the descriptor | DOCUMENTED_INFERENCE |
| Manufacturer | `Airbus` | product page; **not** from `ATC TYPE`, which reads `223` | DOCUMENTED_INFERENCE |
| Family | `A220` | product page, `ATC MODEL` | DOCUMENTED_INFERENCE |
| Model | `A220-300` | `ATC MODEL` = `A220-300` on every livery; title | **LIVE_OBSERVED (DIRECT)** |
| IcaoType | `BCS3` | ICAO Doc 8643 (A223 is not an ICAO designator) | DOCUMENTED_INFERENCE |
| Variant | null | not observable | UNKNOWN |
| EngineVariant | `PW1500G` | product page. The MFD configuration page showed `ENGINE VARIANT PW1524G` on the Air France aircraft, but no variable exposes it and it may be a per-aircraft setting, so the family is the honest value | DOCUMENTED_INFERENCE (PW1524G LIVE_OBSERVED on screen only) |
| WingtipConfiguration | null | single configuration, never named | UNKNOWN |
| Registration | null | see §5 | UNKNOWN |
| OperatorIcao | null | Marketplace liveries unreadable; not in the descriptor | UNKNOWN |
| Livery | `LIVERY NAME` | simulator | LIVE_OBSERVED |

## 5. Registration and livery decision

- **ATC ID is populated but not livery-bound.** It reported `C-FFCO` for a Delta (N324DU) and an Air Baltic (YL-CSM)
  livery, `I-BMTO` and `C-FFCO` for Air France F-HZUF while loading, then stayed empty for the whole second session.
  It is not a registration source.
- **LIVERY FOLDER** is stable and unique per livery (`DELTA N324DU`, `AIR BALTIC YL-CSM`, `AIR FRANCE F-HZUF`). It
  happens to contain the registration for these three, but the FSGAP rule stands: **never parse a registration out of
  a folder or display name**.
- **Livery metadata: three different things (revised by BLOCK 10A.5).**
  - *Filesystem scanning*: impossible for Marketplace liveries (`.fsarchive`); no `livery.cfg`, `atc_id` or
    `config,<id>` can be read.
  - *SimConnect enumeration* (`SimConnect_EnumerateSimObjectsAndLiveries`): lists all 11 Marketplace A220 liveries under
    both presets, in ~70 ms, on the single connection.
  - *Deep probe* (temporary AI aircraft): gives each livery's `LIVERY FOLDER` (e.g. `SWISS HB-JCO`), but the ATC ID
    stays blank with an empty tail number. Diagnostic only.
- **Decision (revised by BLOCK 10A.5): catalog FEASIBLE, built on enumeration.** A `SynapticInstalledAircraftCatalog`
  can list the installed fleet from SimConnect enumeration (one entry per livery name, presets merged), learn each
  livery's folder whenever the user loads it, and report a registration only as `Derived` from that folder with a
  confidence (`Authoritative` only from a readable `livery.cfg`). `OperatorIcao` stays null (`ATC AIRLINE` is blank).
  Algorithm: [synaptic-a220-livery-discovery.md §11](synaptic-a220-livery-discovery.md#11-recommended-catalog-algorithm-not-implemented).

## 6. Generic FSGAP 0.9.0 telemetry — live matrix

Statuses: `GENERIC_VALIDATED` (live, cross-checked against the cockpit or physical consistency over state changes),
`GENERIC_WRONG` (live, contradicted by the cockpit or dead), `SYNAPTIC_OVERLAY_REQUIRED` (no generic source, a
documented Synaptic source exists), `NOT_SUPPORTED` (no source at all), `STILL_NOT_TESTED`. Fields are counted once
per contract property (engine and gear-unit fields once, not per instance): **73 fields** (10A reported 74; the
recount removes a double count in the engine block).

### 6.1 Flight state (19)

| Field | Live value(s) | Status | Notes |
|---|---|---|---|
| OnGround | true at the gate and during the roll, false from 12:15:59 (AGL 177 ft) | GENERIC_VALIDATED | transition at liftoff |
| Latitude / Longitude | 45.7850 / 3.1525 on the runway 08 threshold at LFLC | GENERIC_VALIDATED | position moves along the runway axis during the roll |
| AltitudeFeet | 1 100 ft on the runway (LFLC elevation 1 090 ft), 13 824 ft in the climb | GENERIC_VALIDATED | |
| HeightAboveGroundFeet | 11 ft parked (gear height), 177 → 2 873 ft after liftoff | GENERIC_VALIDATED | |
| RadioAltitudeFeet | Unavailable (by design) | NOT_SUPPORTED | stock `RADIO HEIGHT` read 10.6 ft parked and 1 035 ft at ~1 030 ft AGL: a generic candidate for a later generic change, not A220-specific |
| IndicatedAirspeedKnots | 0 → 153 kt at rotation, 169 kt in the climb | GENERIC_VALIDATED | consistent with GS and the reported 9 kt wind |
| GroundSpeedKnots | 0 → 160 kt at liftoff, 13–18 kt taxi | GENERIC_VALIDATED | |
| VerticalSpeedFeetPerMinute | +2 190 to +3 761 fpm in the climb | GENERIC_VALIDATED | matches the altitude change between samples (1 242 → 1 507 ft in 6 s ≈ 2 650 fpm) |
| TouchdownVerticalSpeedFeetPerMinute | Unknown until 17:12:11, then **−90 fpm** (session 3), held through the rollout | GENERIC_VALIDATED | appears at the touchdown sample; the last airborne VS samples were −208 and −198 fpm (2 s averages at 16 and 12 ft), consistent with a soft flare |
| HeadingMagneticDegrees | 80° on the runway 08 roll, right turn 91 → 114° | GENERIC_VALIDATED | |
| PitchDegrees | −0.7° parked, +12.6° at rotation, +16.7° in the climb | GENERIC_VALIDATED | sign nose-up positive confirmed |
| BankDegrees | +9.6° / +13.7° while the heading increased | GENERIC_VALIDATED | sign right-wing-down positive confirmed |
| GLoad | 1.00 parked, 1.32 at rotation, 0.80–1.12 in manoeuvres | GENERIC_VALIDATED | |
| AngleOfAttackDegrees | +2 to +5° in flight | GENERIC_VALIDATED | **caveat:** reads about ±180° when the aircraft is stationary (undefined airflow, 2 392 samples); consumers must ignore it at zero airspeed. A simulator artefact, not A220-specific |
| GrossWeightKilograms | 51 208 kg at the gate falling to 50 945 kg in the climb | GENERIC_VALIDATED | decreases with APU and engine burn (EICAS fuel 4 820 → 4 780 kg between the two captures) |
| BodyAccelerationX/Y/ZG | Z +0.24 to +0.30 g on the takeoff roll, 0 parked | GENERIC_VALIDATED | forward acceleration consistent with the IAS build-up |

### 6.2 Warnings (4)

| Field | Live | Status | Notes |
|---|---|---|---|
| Overspeed, FlapSpeedExceeded, GearSpeedExceeded, Stall | false throughout | STILL_NOT_TESTED ×4 | no exceedance flown (the A220's own Warning PBA lit once for a takeoff-configuration alert while all four stayed false, which is correct: that is not an envelope warning) |

### 6.3 Engines (13) — compared with the EICAS

| Field | EICAS idle (eng 1 / 2) | FSGAP idle | EICAS 44 % N1 | FSGAP 44 % N1 | Difference | Status |
|---|---|---|---|---|---|---|
| Running | both running | **false / false** | running | **false / false** | never true, not even in flight | **GENERIC_WRONG** |
| N1Percent | 18.5 / 18.5 | 18.8–19.1 / 18.8–19.1 | 44.5 / 44.5 | 43.9–44.1 | ≤ 0.6 | GENERIC_VALIDATED |
| N2Percent | 64.9 / 64.6 | 64.2–64.6 | 80.8 / 80.5 | 80.2–80.5 | ≤ 0.6 | GENERIC_VALIDATED |
| EgtCelsius | 538 / 562 (engine 1 just started) | 540 / 562 | 698 / 694 | 696 / 692 | ≤ 2 °C | GENERIC_VALIDATED |
| FuelFlowKilogramsPerHour | 330 / 325 kg/h | **0 / 0** | 715 / 700 kg/h | **0 / 0** | always 0, also at takeoff thrust | **GENERIC_WRONG** |
| StarterActive | — | **false during both starts** (N2 rising 10 → 64 % on engine 1, 12:06–12:07) | — | false | never true | **GENERIC_WRONG** |
| OilTemperatureCelsius | 32 / 42 °C | **22 / 22** | 41 / 51 °C | **22 / 22** | constant at about the OAT | **GENERIC_WRONG** |
| OilPressurePsi | 118 / 110 psi | **82 / 82** | 143 / 134 psi | **104 / 104** | 25–30 % low, both engines identical | **GENERIC_WRONG** |
| ThrottleLeverPercent | lever at idle | 0 | — | 14 (44 % N1), 100 at takeoff | leads N1 during spool-up (72–93 % while N1 still 19–26 %) | GENERIC_VALIDATED (lever position, not thrust) |
| ReverserEngaged | — | false | — | false | reversers not used | STILL_NOT_TESTED |
| FireDetected | — | — | — | — | no source | NOT_SUPPORTED |
| FireWarningLit | — | — | — | — | no documented light | NOT_SUPPORTED |
| FireHandlePulled | — | — | — | — | `L/R Eng Fire` read 0 throughout (no fire drill) | SYNAPTIC_OVERLAY_REQUIRED |

The stock engine model is used for the spool (N1, N2, EGT) but Synaptic's FADEC and oil systems do not write
`GENERAL ENG COMBUSTION`, `ENG FUEL FLOW PPH`, `GENERAL ENG STARTER ACTIVE` or the oil SimVars. No documented
Synaptic variable exposes them either, so these five fields must be **masked to Unavailable** on an A220 session.
Deriving "running" from N2 would be an inference; it is not proposed.

### 6.4 APU (6)

| Field | Live | Status |
|---|---|---|
| Available | stock `APU PCT RPM` = 0 while the APU ran (EICAS memo `APU ON`, APU bleed started both engines) | NOT_SUPPORTED |
| Running | same | NOT_SUPPORTED |
| MasterSwitchOn | `L:A22X APU Switch` 0 → 2 when selected, 2 for the rest of the session | SYNAPTIC_OVERLAY_REQUIRED (map 0 = off, non-zero = on; see §7.3) |
| BleedOn (contract: "APU bleed air is selected on") | generic `PNEUMATICS APU BLEED AIR` = **false** throughout, including both engine starts on APU bleed | **GENERIC_WRONG** (overlay `L:A22X APU Bleed Off` = 0 gives the documented selection) |
| FireDetected | no source (APU fire aural only) | NOT_SUPPORTED |
| FireHandlePulled | no documented APU fire pushbutton | NOT_SUPPORTED |

### 6.5 Fuel pumps, IRS, fire zones (6)

| Field | Status | Notes |
|---|---|---|
| FuelPumps[*].IsOn | SYNAPTIC_OVERLAY_REQUIRED (with contract gap) | §7.1 |
| FuelPumps[*].Fault | NOT_SUPPORTED | no source |
| InertialReferences Mode / Aligned / Fault | NOT_SUPPORTED ×3 | §7.9 |
| FireZones[*].FireDetected | NOT_SUPPORTED | aurals only |

### 6.6 Electrical (2), hydraulics (3), pressurization (2), environment (5)

| Field | Live | Status |
|---|---|---|
| ElectricalBuses[*].Powered (stock `ELECTRICAL MAIN BUS VOLTAGE:1/2`) | 0 V in every state, including engines and APU running | **GENERIC_WRONG** (not read by FSGAP generically today; must never be) |
| Batteries[*].VoltageVolts (stock `ELECTRICAL BATTERY VOLTAGE:1/2`) | 28.0 V constant, cold and dark to flight | STILL_NOT_TESTED (no cockpit voltage compared; constant value suspicious) |
| HydraulicSystems[*].PressurePsi (stock `HYDRAULIC PRESSURE:1/2/3`) | 1/2: decay to ~3 psi unpowered, 2 830–2 870 psi with engines; 3: 2 957–2 991 psi even cold with all ACMPs off | STILL_NOT_TESTED (1/2 physically consistent, 3 suspect; no HYD page comparison) |
| HydraulicSystems[*].ReservoirPercent | 96–100 % | STILL_NOT_TESTED |
| HydraulicSystems[*].Pressurized | no source | NOT_SUPPORTED |
| Pressurization.CabinAltitudeFeet | 989–992 ft on the ground (field 1 090 ft) while the EICAS showed CAB ALT **900** then **700** | **GENERIC_WRONG** |
| Pressurization.CabinAltitudeRateFeetPerMinute | ~0 on the ground while the EICAS showed RATE ↓ **310** | **GENERIC_WRONG** |
| Environment (OAT, wind direction and speed, precipitation, rate) | 21.7 °C, 192°/9 kt, none | GENERIC_VALIDATED ×5 (simulator weather, aircraft-independent; same SimVars validated on Fenix) |

### 6.7 Landing gear (6) and flight controls (7)

| Field | Live | Status | Notes |
|---|---|---|---|
| HandleDown | down at the gate, up after liftoff, down, up again | GENERIC_VALIDATED | |
| Units (nose / left-main / right-main) | 100 → 0 in about 12 s after gear up; 3 → 82 % then back to 0 when the handle was reversed mid-travel | GENERIC_VALIDATED | handle and legs differ during transit, as designed |
| BrakeLeftPercent / BrakeRightPercent | 100 / 100 with pedals, 0 released, **27–37 % pulsing with the parking brake set** (71–73 % in session 1); **differential** on the landing rollout (16/13, 1/8, 25/0, 29/9), then 100/100 hard braking at 58 kt | GENERIC_VALIDATED ×2 (0–100 scale, no ×100 issue) | the parking brake does **not** read 100 %. Left/right were identical in sessions 1–2 but differ on the rollout of session 3, so the two sides are independent readings (the docs' shared commanded pressure applies to the *keyboard* brake events). Consumers must not infer the parking brake from brake percent |
| SteeringInputPercent | −89 … +100 during taxi, −5 to −8 at rest | GENERIC_VALIDATED (range) | sign: 3 of 5 clear turns agree with positive = right; weak, recorded as such |
| AntiskidActive | false throughout, **including 100/100 braking at 58 → 17 kt** (session 3) | STILL_NOT_TESTED (inconclusive) | no skid may have occurred on a dry runway; the A220 has no antiskid switch. Mask on an A220 session until a transition is seen |
| FlapsHandlePercent | 0, 20 (1), 40 (2, EICAS "2"), 60 (3), 100 (FULL) | GENERIC_VALIDATED | percent = detent × 20; stock `FLAPS HANDLE INDEX` 0/2/5 agrees |
| FlapSurfaces (trailing left / right) | 27 % at flaps 2, 100 % at FULL, travels over ~10 s | GENERIC_VALIDATED | lags the handle as expected |
| SpeedBrakeDeploymentPercent | 0 on the ground, 1–5 % in manoeuvres (roll spoilers), **24 %** with the speedbrake lever in the descent (FL300), **99 %** from the touchdown sample (ground spoilers) | GENERIC_VALIDATED | unlike Fenix, `SPOILERS LEFT/RIGHT POSITION` has the right scale on the A220; stock `SPOILERS HANDLE POSITION` did not follow the lever and is not used |
| AileronLeft / Right | 0 parked, −19 … +14 % in turns, left = right sign | GENERIC_VALIDATED ×2 | actual surfaces (see elevator) |
| ElevatorDeflectionPercent | **−100 unpowered**, −51 then −2 once hydraulics pressurize, −53 with forward stick on the roll, +3 … +7 in the climb | GENERIC_VALIDATED | the droop without hydraulic pressure proves these are surface positions, not stick inputs |
| RudderDeflectionPercent | −94 … −100 unpowered, follows pedals and tiller on the ground, +3 … +4 in flight | GENERIC_VALIDATED | |

### 6.8 Coverage

| Count | |
|---|---|
| Fields examined | 73 |
| GENERIC_VALIDATED | 39 |
| GENERIC_WRONG | 9 (engine running, fuel flow, starter, oil temperature, oil pressure; APU bleed; bus powered; cabin altitude and rate) |
| SYNAPTIC_OVERLAY_REQUIRED | 3 (fuel pump state, APU master switch, engine fire pushbutton) |
| NOT_SUPPORTED | 13 (radio altitude, engine fire detected and fire light, APU available/running/fire detected/fire handle, pump fault, 3 IRS, hydraulic pressurized, fire zones) |
| STILL_NOT_TESTED | 9 (4 warnings, reverser, battery voltage, hydraulic pressure and reservoir, antiskid) |

- **Validated generic coverage: 39 / 73 = 53.4 %** (37 after the first flight; touchdown and speed brake added by the landing flight).
- **Potential total coverage: (39 + 3 overlay + 9 untested) / 73 = 69.9 %**, the ceiling if every untested field passes.
  The documentation-based 10A ceiling (80 %) was too high: live evidence turned 9 fields from "plausible or untested"
  into "wrong".
- **Synaptic overlay fields actually required: 3** (plus the replacement for the masked APU bleed, which reuses the
  same overlay group).

## 7. Systems, live

### 7.1 Fuel boost pumps — critical

| Position | Value | Evidence |
|---|---|---|
| OFF | 0 (documented) | seen only while the aircraft loads; not selected live |
| AUTO | 1 | default after load; left and right returned to 1 after ON |
| ON | 2 | right 1 → 2 → 1 (23:46:35–23:46:40), left 1 → 2 → 1 (23:46:45–23:46:50), stable between 5 s reads |

**Can AUTO be represented honestly by the existing `FuelPumpTelemetry.IsOn`? NO.** AUTO is the normal in-flight
selection and means "the system decides", neither on nor off. Mapping it to `true` or `false` would publish a guess.
`FuelPumpMode { Off, Auto, On }` is **CONFIRMED P0**.

### 7.2 Flap detents

`FLAPS HANDLE INDEX` (stock) read 0, 2 and 5 at the same moments as `FlapsHandlePercent` 0, 40 and 100, and the EICAS
showed `2` and FULL. `FLAPS NUM HANDLE POSITIONS` = 5, so the percentage is exactly `index × 20`: no information is
lost on the A220. The documented `L:A22X Flap Lever` stayed 0 throughout and is **not usable**. Gap G-C1 is
**downgraded to P2**: if ever needed, it is a generic field fed by the stock index for every aircraft.

### 7.3 APU — control versus actual

| Concept | Generic source | Synaptic source | Observed | Actual meaning | Recommended future source |
|---|---|---|---|---|---|
| Selector | stock `APU SWITCH` = 0 always | `L:A22X APU Switch` | 0, then 2 from selection until the end (over 2 h) | documented 0 Off / 1 Run / 2 Start; 1 never seen while the APU ran, so the live meaning of 2 is "selected, running" or the docs' order is wrong | overlay, `MasterSwitchOn` = value ≠ 0 only |
| Running / available | stock `APU PCT RPM` = 0 always | none | EICAS memo `APU ON`; APU bleed started both engines | APU ran; no readable state | none (Unavailable) |
| Generator commanded | stock `APU GENERATOR SWITCH` = 0 | `APU Gen Off` = 0 | never changed | selected on | defer |
| Generator online | stock `APU GENERATOR ACTIVE` = 0 | none | — | — | none |
| Bleed commanded | stock `PNEUMATICS APU BLEED AIR` = 0 | `APU Bleed Off` = 0 | never changed; bleed was used | selected on | overlay → `Apu.BleedOn` |
| Bleed flowing | stock `APU BLEED PRESSURE RECEIVED BY ENGINE` = 0 | none | — | — | none |
| Fault | — | `APU Gen/Bleed Fail Lamp` = 0 | no fault | not exercised | documentation only |
| Fire | — | `Aural APU Fire` only | — | — | none |

The stock APU SimVars are dead on this aircraft. The selector enum contradicts the documentation; only "off / not off"
is safe.

### 7.4 Pneumatics — control / actual / fault

| Item | Control (documented) | Live | Actual state | Fault |
|---|---|---|---|---|
| Left / right bleed | `L/R Bleed Off` | 0 (on) throughout | none documented; stock `BLEED AIR ENGINE:1/2` = 1 even cold and dark (not usable) | `L/R Bleed Fail Lamp` 0 |
| APU bleed | `APU Bleed Off` | 0 | none | `APU Bleed Fail Lamp` 0 |
| Crossbleed | `Crossbleed` (0/1/2) | 1 (Auto) | none | — |
| Left / right pack | `L/R Pack Off` | 0 | none | `L/R Pack Fail Lamp` 0 |

All CONTROL_POSITION or FAULT_INDICATION; none changed, so their semantics stay documentation-only. No airflow is
inferred from a switch.

### 7.5 Hydraulics — selector versus pressure

| Item | Selector (live) | Pressure (stock, live) | Conclusion |
|---|---|---|---|
| System 1 | PTU 1 (Auto) | decays 2 586 → 3 psi in cold and dark with PTU at Auto; 2 830 psi with engines | selector ≠ pressure (proven) |
| System 2 | ACMP 2B 0 → 1 | same pattern; 2 860 psi with engines | idem |
| System 3 | ACMP 3A/3B 0 → 1 | 2 957–2 991 psi **even cold with ACMPs Off** | index 3 suspect (as on Fenix) |

Session 1's failed engine start is consistent with the evidence: the APU selector went to START but the APU bleed
never produced a start there (no EICAS confirmation). Session 2 started normally.

### 7.6 Electrical

| Item | Live | Use |
|---|---|---|
| Stock bus voltages | 0 V always | wrong, never read |
| Stock battery voltage 1/2 | 28.0 V constant | untested |
| Stock `GENERAL ENG GENERATOR ACTIVE:1/2` | 0 with both engines running | wrong |
| Stock `ELECTRICAL MASTER BATTERY`, `EXTERNAL POWER ON` | follow the cockpit | not in the contract |
| `L/R/APU Gen Off`, `Gen Fail Lamp`, `RAT Gen`, `RAT Gen Lamp`, `Cabin Power Off` | 0 throughout | documentation only |
| `Bus Isolation Mode` | 1 (Auto) | documentation only |
| `L:INI_GPU_AVAIL` | 1 → 0 when ground power was removed | diagnostic |

**Minimum useful subset: none for V1.** Nothing electrical is both readable and validated. `ElectricalSources` is
deferred until a generator transition can be observed.

### 7.7 Fire

`L/R Eng Fire` stayed 0 (pushbuttons not pressed). No warning, test or aural state was produced; no fire was created.
`FireDetected` stays Unavailable. The overlay carries the two pushbutton states only.

### 7.8 Anti-ice

Cowl left/right and wing read 1 (Auto) throughout; 0 only during the load; OFF and ON were not selected live. The
three-state enum is documented, not live-confirmed. Stock `ENG ANTI ICE:1/2` read 0 and `STRUCTURAL DEICE SWITCH` 0
while the selectors were at Auto: no generic source. **AntiIceTelemetry: DEFER** (no consumer, not proven).

### 7.9 IRS

No documented variable and no generic SimVar gives an inertial reference mode, alignment or fault. **`InertialReferences`
stays Unavailable** (empty, capability not declared). Nothing was reverse engineered.

### 7.10 Master warning / caution

`L:A22X Caution PBA` toggled about 14 times (parking brake changes, taxi, configuration), `L:A22X Warning PBA` lit once
for 10 s during the run-up (a takeoff-configuration alert). Both behave as **illuminated-annunciator states** that
clear on their own; whether a crew acknowledge also clears them was not tested. Live semantic: WARNING_INDICATION,
LIVE_VALIDATED.

## 8. Contract gap decisions

| Gap | 10A priority | Live decision | Why |
|---|---|---|---|
| G-F1 `FuelPumpMode` | P0 | **CONFIRMED P0** | AUTO live on both pumps; `IsOn` cannot express it |
| G-C1 flap detent | P1 | **DOWNGRADE → P2** | percent = index × 20; stock index available; Synaptic lever variable dead |
| G-B1 parking brake | P1 | **CONFIRMED P1 (generic)** | stock `BRAKE PARKING POSITION` matches the Synaptic variable on every transition; brake percent does not reveal it (27–37 %) |
| G-I1 `AntiIceTelemetry` | P1 | **DEFER** | only Auto seen; no consumer |
| G-A1 master alerts | P1 | **CONFIRMED P1** | both annunciators live-validated; a general need (Fenix has them too) |
| G-E1 electrical sources | P1 | **DOWNGRADE → DEFER** | nothing readable changed; stock electrical dead |
| G-P1 pneumatics | P1 | **DOWNGRADE → DEFER** | controls static, no actual-state source |
| G-D1 richer descriptor | P1 | **CONFIRMED, done in the transport** | `ATC MODEL`/`ATC TYPE` are required for a specific detection rule; no contract change |
| G-X2 shared MSFS locator | P1 if catalog | **DEFER** | the catalog would use SimConnect enumeration, not package files |
| (new, 10A.5) livery enumeration service | — | **P1, generic** | `SimConnect_EnumerateSimObjectsAndLiveries` works for every aircraft, including Marketplace content |
| (new, 10A.5) registration source/confidence | — | **P1, generic** | a derived registration must be labelled as such on `InstalledAircraft` |
| G-H1 hydraulic selectors | P2 | **DEFER** | selectors live but no consumer; pressure proven independent |
| G-U1 APU selector mode | P2 | **REJECT** | live enum contradicts the docs; `MasterSwitchOn` suffices |
| G-A2 `CockpitEvent` | P2 | **DEFER** | no consumer |
| (new) generic radio altitude | — | **P2, generic** | stock `RADIO HEIGHT` works here; must be checked on Fenix before enabling for everyone |

## 9. Future overlay (BLOCK 10B)

| Variable | FSGAP field | Priority |
|---|---|---|
| `L:A22X L Boost Pump`, `L:A22X R Boost Pump` | `FuelPumps[left/right].Mode` (+ `IsOn` Unavailable) | P0 |
| `L:A22X APU Switch` | `Apu.MasterSwitchOn` (≠ 0) | P0 |
| `L:A22X APU Bleed Off` | `Apu.BleedOn` (= 0), replacing the masked generic value | P0 |
| `L:A22X L Eng Fire`, `L:A22X R Eng Fire` | `Engines[1/2].FireHandlePulled` | P0 |
| `L:A22X Caution PBA`, `L:A22X Warning PBA` | new alerts section (only if G-A1 is added) | P1 |

- **6 variables** for P0, **8** with the alerts. Down from the 10A estimate of up to 40.
- **One polling group** every **2 s** (selectors and annunciators hold for seconds): **0.5 native reads/s**.
- Total with the generic telemetry (about 1.8 reads/s) and identity polling (0.2–0.5): **about 2.5–2.8 native reads/s**
  on the single connection.
- Generic mask for an A220 session: engine `Running`, `FuelFlowKilogramsPerHour`, `StarterActive`,
  `OilTemperatureCelsius`, `OilPressurePsi`; `Apu.BleedOn` (replaced by the overlay); `Pressurization` (both fields);
  `LandingGear.AntiskidActive` (never true, even under hard braking).
  Batteries, buses and hydraulics are not read generically and must not be added for the A220.

## 10. Failure capabilities

Unchanged: catalog NOT_FOUND, read active NOT_DOCUMENTED, trigger NOT_FOUND, clear NOT_FOUND, stable IDs NOT_FOUND. No
new official surface appeared during the live sessions; nothing was probed. `FailureCapabilities.None`.

## 11. Scope guard

The vendor-name guards apply to the vendor-neutral assemblies only: `FSGAP.Abstractions` (public surface forbids
`Synaptic` and `A22X`), `FSGAP.Core` (no `Synaptic`/`A22X` type or member), `FSGAP.SimConnect` (no `Synaptic`, `A22X`,
`A220` member; no `A22X`/`INI_GPU` string in its binary, nor in Abstractions and Core), and `FSGAP.Fenix` (no
`A22X` string, no Synaptic type). None of them inspects any other assembly, so a future `FSGAP.Synaptic` project, its
tests and fixtures may use those names freely, exactly as FSGAP.Fenix uses "Fenix".

## 12. Risks

- **No vendor marker.** Detection relies on configuration strings; a preset rename breaks it (§3).
- **Registration only derived** for Marketplace liveries (§5; one of ten folders is inconsistent with its operator).
- **Documentation drift.** Three documented variables contradict live behaviour: the `APU Switch` enum (1 never seen),
  `Flap Lever` (never moves) and the `Autobrake` level order (the pilot's first level read 4, documented as HI). Every future overlay variable must be live-proven, not taken from the docs.
- **Untested fields.** Warnings, reverser, antiskid and hydraulics still need a
  dedicated check in 10B qualification.
- **AoA at standstill** reads ±180°: harmless but must be documented for consumers.

## 13. Recommended BLOCK 10B (not implemented)

1. **Contracts (0.10.0):** `FuelPumpMode` + `FuelPumpTelemetry.Mode` (P0). Optionally `LandingGear.ParkingBrakeSet`
   (generic, `BRAKE PARKING POSITION`) and a minimal alerts section (`MasterCautionLit`, `MasterWarningLit`) if a
   consumer asks. Nothing else.
2. **FSGAP.Synaptic (new assembly):** recognizer = the rule of §3; identity per §4 (registration derived from a learned livery folder or null; operator null);
   generic policy masking the fields of §9; one overlay group with the six P0 variables at 2 s; capabilities:
   FlightState, Engines, LandingGear, FlightControls, Environment, FuelPumps, Apu, Fire (and Warnings once tested);
   not Pressurization, Electrical, Hydraulics, InertialReferences; failures `UnsupportedFailureProvider`. Catalog, if
   built: enumeration + learned livery folders + derived registrations ([livery audit §11](synaptic-a220-livery-discovery.md#11-recommended-catalog-algorithm-not-implemented)).
3. **Tests:** recognizer positives from the three live descriptors, negatives from the A380, Fenix, Asobo/FSLTL-style
   titles and any title merely containing A220; mapper tests (Auto never collapses; APU switch 2 → on); golden tests
   from `fixtures/synaptic-a220/`; policy and composition tests mirroring Fenix; architecture tests (no SimConnect
   reference, `A22X` names confined to one file, read-only).
4. **Live qualification:** one full flight with landing (touchdown, reverser, antiskid, speed brake), boost pumps
   through OFF, and the session's capabilities compared with the cockpit.
5. **Version:** 0.10.0 after qualification. Applications untouched until then.

---

# Appendix A — documentation sources (BLOCK 10A)


### 1.1 Official (primary)

| Source | What it gives | Version / date |
|---|---|---|
| `https://docs.synapticsim.com/pilots/simvars` (repo `github.com/synapticsim/docs`, commit `4abccfd`, 2026-09-25 "Mark 1.0.10 as current") | "Index of all simulator variables (L-Vars and H-Events) that the A220 reads and writes, organized by system." 397 `L:A22X …` / `L:INI_…` names with unit and one-line meaning. **Contains zero H-Event** despite its description. | 1.0.10 |
| `https://docs.synapticsim.com/pilots/inputs` | Stock MSFS input events the aircraft listens to (brakes, flight controls, FCP, engines, flaps, steering, transponder, radios, `BAROMETRIC`, `PAUSE_ON`). | 1.0.10 |
| `https://docs.synapticsim.com/liveries/config` | MSFS 2024 `livery.cfg` layout: `[Selection] required_tags = "ext_a223_fuselage"`, `[Panel_DynamicParameters] param.0 = "config,<id>"`, `Config/<id>/checklists.json` and `defaults.json`, MSFS 2024 simobject path `SimObjects/Airplanes/Synaptic_A220/liveries/inibuilds/<Livery>/livery.cfg`. | 1.0.10 |
| `https://docs.synapticsim.com/liveries/paint-kit`, `/liveries/checklists`, `/liveries/defaults` | Paint kit link; electronic checklist editor (`ecl.synapticsim.com`) with "sensed" items bound to aircraft variables; FMS defaults page is a placeholder. | 1.0.10 |
| `https://docs.synapticsim.com/changelog` (1.0.0 → 1.0.10) | Stock SimVars the aircraft maintains: `FLAPS HANDLE INDEX` (1.0.4), `AUTOPILOT ALTITUDE LOCK VAR` (1.0.4), `KOHLSMAN SETTING MB/HG/STD:1..3` and `AUTOPILOT MANAGED SPEED IN MACH` (1.0.9). EFB pages named: performance calculator, loadsheet, charts, ground equipment, pause at TOD, time compression. No failure feature in any entry. | 1.0.0 (2026-07-28) … 1.0.10 (2026-09-25) |
| `https://docs.synapticsim.com/ref/troubleshooting` | Package/simobject names: MSFS 2020 `SimObjects\Airplanes\inibuilds_aircraft-a220\panel\systems.wasm`; MSFS 2024 `SimObjects\Airplanes\Synaptic_A220\attachments\inibuilds\Part_Interior_A223_Cockpit\panel\systems.wasm`; WASM cache paths; Marketplace vs iniManager coexistence warning. | 1.0.10 |
| `https://docs.synapticsim.com/ref/sentry` | Opt-in crash telemetry; build tags `2024-pc`, `2024-xbox`, `2020-pc`, `2020-xbox`. Not an integration surface. | 1.0.10 |
| `https://inibuilds.com/products/synaptic-a220-msfs` | "iniBuilds in collaboration with Synaptic Simulations"; A220-300; PW1500G; MSFS 2020/2024; version 1.0.10; feature text "hydraulic failure modelling, brake and tyre temperatures … and persistent maintenance"; EFB feature list. No SDK, API or LVAR mention. | 1.0.10 |
| ICAO Doc 8643 (via `doc8643.com/aircraft/BCS3`) | Airbus A220-300 = ICAO type designator **BCS3** (L2J, M). `A223` is an IATA-style code, not an ICAO designator. | — |

### 1.2 Installed files (read-only)

| Item | Value |
|---|---|
| MSFS | MSFS 2024, Microsoft Store/Xbox build `Microsoft.Limitless` **1.8.16.0** (`appxmanifest.xml`), running during the audit |
| Packages root (`UserCfg.opt`) | `<InstalledPackagesPath>\StreamedPackages\` |
| Aircraft package | `fs24-inibuilds-aircraft-a220` (Marketplace streamed package, `Content.xml`: `active="Activated"`; the MSFS 2020 twin `fs20-inibuilds-aircraft-a220` is `SystemDisabled`) |
| Livery package | `fs24-inibuilds-a220-liveries` (Activated) |
| AI traffic | `fs24-asobo-passiveaircraft-a220family` (Asobo passive aircraft, not the user aircraft) |
| Simobject | `content/simobjects/airplanes/synaptic_a220` (folder names only; the content is in `.fsarchive` containers) |
| Presets (LocalCache mirror) | `SimObjects/Airplanes/synaptic_a220/presets/inibuilds/a220-300` and `a220-300_nocabin`: **two loadable presets** |
| Readable text | `common/config/state.CFG` only (persisted `[LocalVars]` display brightness values and engine hours). No `aircraft.cfg`, `manifest.json`, `layout.json` or `livery.cfg` is readable: Marketplace content is packed |
| Fonts | `html_ui/fonts/a22xmono-*.ttf`, `a220mkp-regular.ttf`; `html_ui/pages/vcockpit/instruments/efb-a220/` (EFB is an in-sim HTML instrument) |
| WASM work folder | `LocalState\WASM\MSFS2024\inibuilds-aircraft-a220\work\`: `NavigationData\` (Navigraph cycle + `db.s3db`) and `synaptic.s3db` |
| `synaptic.s3db` schema (strings scan of a copy) | one table, `__syn_pilot_waypoints`. **No failure, fault or maintenance table.** |
| Installed A220 version | **UNKNOWN**: the Marketplace package carries no readable manifest. The docs and store say 1.0.10 is current (2026-09-25); the Marketplace build may lag. |
| Community folder | no Synaptic/iniManager A220 package; the 144 Fenix liveries scanned by the existing catalog are unaffected |

Community evidence (secondary): flightsim.to lists Synaptic A220-300 liveries "MSFS2024 only (with cabin)", consistent
with the two presets above. No community page inspected exposes the loaded `TITLE`.


# Appendix B — official variable inventory (BLOCK 10A; live statuses in the CSV)


397 distinct documented variables; **0 documented H-Events**; 0 event counters. Distinct counts:

| FSGAP classification | Count | Semantic tag | Count |
|---|---|---|---|
| MIGRATE_CANDIDATE | 38 | CONTROL_POSITION | 190 |
| DEFER | 67 | EVENT (92 aurals "currently playing", write-action and read/write flags) | 117 |
| APPLICATION_SPECIFIC | 39 | COMMAND_MODE (flight guidance) | 19 |
| DIAGNOSTIC_ONLY | 5 | FAULT_INDICATION (FAIL/OIL/DISC lamps) | 13 |
| AMBIGUOUS | 40 | ACTUAL_SYSTEM_STATE | 9 |
| NOT_RELEVANT (55 circuit breakers, lighting, radios, FMS flags, callouts…) | 208 | ANIMATION_ONLY | 8 |
| | | WARNING_INDICATION (master caution/warning) | 2 |
| | | UNKNOWN (39 annunciator lamps whose driver is undocumented) | 39 |
| Documented access: READ 296, WRITE_ACTION 81 ("when set", "toggles", "pulls"), READ_WRITE 13, ANIMATION 7 | | EVENT_COUNTER | 0 |

By system (MIGRATE_CANDIDATE unless stated):

| Group | Variables | Semantic | Class |
|---|---|---|---|
| Fuel | `L Boost Pump`, `R Boost Pump` (0 Off/1 Auto/2 On) | CONTROL_POSITION | MIGRATE_CANDIDATE (P0 gap) |
| | `Manual Transfer` (Off/Right/Center/Left), `Gravity Transfer` | CONTROL_POSITION | DEFER |
| APU | `APU Switch` (Off/Run/Start), `APU Gen Off`, `APU Bleed Off` | CONTROL_POSITION | MIGRATE_CANDIDATE |
| | `APU Gen Fail Lamp`, `APU Bleed Fail Lamp` | FAULT_INDICATION | MIGRATE_CANDIDATE |
| | `APU Gen Off Lamp`, `APU Bleed Off Lamp` | UNKNOWN | AMBIGUOUS |
| Pneumatic | `L Bleed Off`, `R Bleed Off`, `Crossbleed` (Closed/Auto/Open) | CONTROL_POSITION | MIGRATE_CANDIDATE |
| | `L/R Bleed Fail Lamp` | FAULT_INDICATION | MIGRATE_CANDIDATE |
| | `L/R Bleed Off Lamp` | UNKNOWN | AMBIGUOUS |
| Packs / air | `L Pack Off`, `R Pack Off` | CONTROL_POSITION | MIGRATE_CANDIDATE |
| | `L/R Pack Fail Lamp` | FAULT_INDICATION | MIGRATE_CANDIDATE |
| | `Pack Flow`, `Ram Air`, `Trim Air Off`, `Recirc Air Off`, temperature knobs, cargo air, `Manual Temperature` | CONTROL_POSITION | DEFER |
| Hydraulics | `PTU`, `ACMP 2B`, `ACMP 3A`, `ACMP 3B` (Off/Auto/On), `Hyd 1 SOV`, `Hyd 2 SOV` | CONTROL_POSITION | MIGRATE_CANDIDATE (no pressure exists) |
| | `Hyd 1/2 SOV Lamp` (CLOSED) | UNKNOWN | AMBIGUOUS |
| Electrical | `L Gen Off`, `R Gen Off`, `APU Gen Off`, `RAT Gen`, `Bus Isolation Mode` (Main/Auto/Ess) | CONTROL_POSITION | MIGRATE_CANDIDATE |
| | `L/R/APU Gen Fail Lamp` | FAULT_INDICATION | MIGRATE_CANDIDATE |
| | `RAT Gen Lamp` ("lit once the RAT generator is producing usable voltage") | ACTUAL_SYSTEM_STATE | MIGRATE_CANDIDATE |
| | `L/R Gen Disc`, `Gen Oil/Disc Lamp`, `Cabin Power Off` | CONTROL_POSITION / FAULT_INDICATION | DEFER |
| | 55 `Circuit Breaker …` ("pulls … when set") | CONTROL_POSITION (write) | NOT_RELEVANT |
| | `L:INI_GPU_AVAIL` | ACTUAL_SYSTEM_STATE | DIAGNOSTIC_ONLY |
| Fire | `L Eng Fire`, `R Eng Fire` (pushbutton pressed) | CONTROL_POSITION | MIGRATE_CANDIDATE |
| | `Aural Left/Right Engine Fire`, `Aural APU Fire`, `Aural Cargo Fire`, `Aural Smoke` | EVENT | DEFER |
| Anti-ice | `L/R Cowl Anti Ice`, `Wing Anti Ice` (Off/Auto/On) | CONTROL_POSITION | MIGRATE_CANDIDATE (P1 gap) |
| | `Probe Heat` (read/write, self-cleared each tick) | UNKNOWN | AMBIGUOUS |
| | `L/R Side Window Heat Off`, `L/R Windshield Heat Off` (+ lamps) | CONTROL_POSITION / UNKNOWN | DEFER / AMBIGUOUS |
| Landing gear | `Alternate Gear`, `Nose Steer Off`, `L Tiller`, `Gear Aural` (read/write) | CONTROL_POSITION / EVENT | DEFER / AMBIGUOUS |
| Brakes | `Parking Brake` | CONTROL_POSITION | MIGRATE_CANDIDATE (P1 gap) |
| | `Autobrake` (RTO/OFF/LO/MED/HI) | CONTROL_POSITION | APPLICATION_SPECIFIC |
| | `Alternate Brake` (+ lamp) | CONTROL_POSITION | DEFER |
| Flight controls | `Flap Lever` (0–5) | CONTROL_POSITION | MIGRATE_CANDIDATE (P1 gap) |
| | `Alternate Flap`, `PFCC 1/2/3 Off` | CONTROL_POSITION | DEFER |
| | `L Sidestick X/Y`, `Rudder Pedals`, `Horizontal Stabilizer`, `Spoiler Lever`, `Throttle 1/2 TLA` | ANIMATION_ONLY | NOT_RELEVANT |
| Warnings / CAS | `Caution PBA`, `Warning PBA` (annunciator illuminated) | WARNING_INDICATION | MIGRATE_CANDIDATE (P1 gap) |
| | `Master Caution Warning` (acknowledge when set) | EVENT (write) | NOT_RELEVANT |
| | `Aural Warn Inhibit`, `TAWS * Inhibit` | CONTROL_POSITION | DEFER |
| Aurals | 92 `Aural …` booleans "currently playing" | EVENT | DEFER (fire, smoke, cabin, master), APPLICATION_SPECIFIC (stall, overspeed, TAWS, dual input), NOT_RELEVANT (callouts, tones, tests) |
| Navigation / inertial | `L/R IRS Reversion`, `L/R ADS Reversion` (pushbutton pressed), `Reversion Mode` | CONTROL_POSITION | DEFER / NOT_RELEVANT |
| Flight guidance | `AP Master`, `AT Master`, `FG *` modes, `Selected FPA`, flight directors | COMMAND_MODE | APPLICATION_SPECIFIC |
| Other | `Flight Stage` (Hangar…Final), `Lamp Test`, `RDC Powered` | ACTUAL_SYSTEM_STATE | DIAGNOSTIC_ONLY |
| | `Continuous Ignition`, `Eng Start Mode`, pressurization/oxygen/ditching controls, exterior lights | CONTROL_POSITION | DEFER |
| | cockpit lighting, display brightness, radios, altimeters, chronos, FMS flags, pause at TOD | CONTROL_POSITION / EVENT | NOT_RELEVANT |

**Semantic rules applied.** A "switch is selected/position" variable is a CONTROL_POSITION even when it drives a
system. A "FAIL/OIL/DISC lamp" is a FAULT_INDICATION. An "OFF/ON/CLOSED/OPEN lamp" is UNKNOWN: the documentation
does not say whether it follows the switch or the system, so it is AMBIGUOUS until a live transition shows which
(39 lamps). An aural "currently playing" is an EVENT, never a state. Anything "for animation" is ANIMATION_ONLY.
Write-action variables ("when set") are never read for state.

