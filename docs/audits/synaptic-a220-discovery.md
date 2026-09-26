# Synaptic A220-300 — discovery and compatibility audit (BLOCK 10A)

Read-only audit, 2026-09-26, against FSGAP_SDK 0.9.0 (`e2f27eb`). Nothing in this block is a production
integration: no `SynapticAircraftProvider`, no Synaptic telemetry overlay, no failure provider, no write to the
aircraft, and no change to FSHANGAR, FLIPPP or fenixhangarweb. The block numbering follows the FSGAP BLOCK series
(BLOCK 9 was the FSGAP 0.9.0 parity work); it is unrelated to the "BLOCK 10 — FLIPPP migration" row of the
extraction plan.

The companion file [synaptic-a220-variable-mapping.csv](synaptic-a220-variable-mapping.csv) holds every officially
documented Synaptic variable (397 distinct names, 407 rows because ten are documented under two panels) with its
documented meaning, documented access, semantic tag and FSGAP classification.

**Headline.** The aircraft is installed on this machine (MSFS 2024 Marketplace streamed package) but was **not
loaded** during the audit: MSFS 2024 was running a Fenix A321 in flight, and a flight in progress was not interrupted.
Every A220 finding below is therefore documentation-based; nothing is `GENERIC_VALIDATED`. The official variable
index is large (397 L-vars) but is almost entirely **switch positions, lamps and aurals**: it documents no APU
operating state, no hydraulic pressure, no battery voltage, no IRS mode, no fire detection and no failure interface.

## 1. Sources

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

## 2. Environment and live status

| | |
|---|---|
| MSFS running | yes (1.8.16.0) |
| Aircraft loaded during the audit | `FenixA321 CFM WF SC`, F-GMZC, in flight (FL226 climbing, 287 kt) |
| Synaptic A220 loaded | **no** (installed, not loaded; the flight was not interrupted) |
| Live A220 descriptor | **not captured** |
| Live A220 telemetry / LVAR probe | **not run** |
| Live negative detection case | yes: the five candidate rules were evaluated live against the Fenix A321 (§4) |
| FSGAP sample on the running simulator | connected, one native connection, all four generic groups flowing, Fenix overlay flowing |

## 3. Live descriptor (C)

| Field | Value | Source | Confidence |
|---|---|---|---|
| TITLE | not observed | — | UNKNOWN. Hypothesis: the preset titles of `presets/inibuilds/a220-300` and `a220-300_nocabin` (MSFS 2024 loads a preset, as with the Fenix). |
| ATC ID | not observed | — | UNKNOWN |
| LIVERY FOLDER | not observed | — | UNKNOWN. Per the livery documentation, a livery folder under `Synaptic_A220/liveries/inibuilds/`. |
| LIVERY NAME | not observed | — | UNKNOWN. `[General] name` of `livery.cfg` (documented example "Air Baltic A220-300"). |

The sample now prints these four values and the rule verdicts for any aircraft (`--synaptic-probe`); one run with the
A220 loaded closes this table.

## 4. Detection (E)

The detector must recognize the Synaptic A220, not any title containing A220/A223/Airbus. Candidate rules, as
implemented in the discovery harness (`samples/FSGAP.SimConnect.Console/SynapticDiscovery.cs`), graded on evidence:

| Rule | Test | Grade | Why |
|---|---|---|---|
| R1 | `TITLE` contains `Synaptic` | PLAUSIBLE | The simobject is `synaptic_a220` and the developer brands the docs; whether the preset title carries the word is unobserved. |
| R2 | `TITLE` contains `A220` or `A223`, and none of `Fenix`, `Asobo`, `FSLTL` | PLAUSIBLE | Would also match a future non-Synaptic A220 (none exists for MSFS today besides Asobo's AI model, excluded). Needs a positive marker to become RELIABLE. |
| R3 | `TITLE` or `LIVERY FOLDER` contains `A22X` | WEAK | `A22X` is the project name (fonts, variable prefix); nothing says it appears in titles or folders. |
| R4 | `TITLE` contains `iniBuilds` and `A220` | WEAK | Publisher marker; the docs use `inibuilds` in preset and livery paths, the title is unknown. |
| R5 | `TITLE` contains `Airbus` | REJECT | Control rule: fires on Fenix, FlyByWire and iniBuilds Airbus titles. |

Negative cases:

| Descriptor | Expected | Result |
|---|---|---|
| `FenixA321 CFM WF SC` / `AFR-F-GMZC-8761` | no rule fires | **LIVE TEST 2026-09-26: R1–R5 all "no"; no Synaptic variable read** |
| `FenixA319 CFM SL`, `FenixA320 IAE WF` | no rule fires | by construction (R2 excludes `Fenix`; no `A220`) |
| Asobo passive `A220` family (AI) | never the user aircraft; R2 excludes `Asobo` | not live-tested |
| FSLTL AI `A220` models | R2 excludes `FSLTL` | not live-tested |
| iniBuilds A300/A340/A350/A380, FlyByWire A380, PMDG 737/777, C172 | no rule fires | by construction (no `A220`/`A223`/`Synaptic`) |
| A random title containing `A220` (e.g. a third-party livery pack title) | R2 fires | **known weakness of R2** |

**Recommendation.** No rule is RELIABLE yet. Ship no rule in this block. After one live capture, the production rule
should combine a positive Synaptic marker (title word, livery folder prefix or, preferably, `ATC MODEL`/`ATC TYPE`
strings, see gap G-D1) with the `A220` model token, exactly as the Fenix rule requires the `Fenix` marker.

## 5. Identity (D)

| Identity field | Proposed value | Evidence | Classification |
|---|---|---|---|
| Developer | `Synaptic Simulations` | docs site, simobject name `synaptic_a220`; publisher is iniBuilds ("iniBuilds in collaboration with Synaptic Simulations") | DOCUMENTED_INFERENCE |
| Manufacturer | `Airbus` | product page, docs ("Airbus A220-300") | DOCUMENTED_INFERENCE |
| Family | `A220` | product page | DOCUMENTED_INFERENCE |
| Model | `A220-300` | product page; presets `a220-300`, `a220-300_nocabin`; tag `ext_a223_fuselage`; only variant sold | DOCUMENTED_INFERENCE |
| IcaoType | `BCS3` | ICAO Doc 8643. **`A223` is rejected for this field**: it is not an ICAO designator. | DOCUMENTED_INFERENCE |
| Variant | null | PW1521G vs PW1524G thrust rating not documented | UNKNOWN |
| EngineVariant | `PW1500G` | product page ("Pratt & Whitney PW1500G"); single option | DOCUMENTED_INFERENCE |
| WingtipConfiguration | null | single airframe configuration; the docs never name it | UNKNOWN (leave null rather than invent `Winglets`) |
| Registration | see §6 | ATC ID (live) and/or livery metadata | LIVE_OBSERVED / CATALOG_DERIVED, unknown today |
| OperatorIcao | null | the documented `livery.cfg` has no `icao_airline`; Marketplace liveries are not readable | UNKNOWN |
| Livery | `LIVERY NAME` | simulator | LIVE_OBSERVED (not captured) |

The "with cabin / no cabin" preset distinction is a cabin model choice, not an identity fact (same treatment as the
Fenix cabin tokens: recognized, not exposed).

## 6. Livery and registration (F)

**Metadata sources (documented, MSFS 2024 only).**

- `livery.cfg`: `[Version]`, `[General] name`, `[Selection] required_tags = "ext_a223_fuselage"`,
  `[Panel_DynamicParameters] param.0 = "config,<id>"`.
- `Config/<id>/checklists.json` and `defaults.json`, copied by the livery package build.
- The docs say the `config` id is "your livery's registration **or some other unique identifier**", chosen for
  file lookup, with collisions leading to files "not picked up correctly". It is therefore a **configuration key**,
  not a registration field.
- Nothing in the docs mentions `atc_id`, `atc_airline` or `icao_airline`; standard MSFS 2024 `livery.cfg` allows a
  `[FLTSIM]` section, so real liveries may carry them. Unverified.

**Registration resolution order (proposed, conservative).**

1. Installed livery matched by `LIVERY FOLDER` → `livery.cfg [FLTSIM] atc_id` if present (as for Fenix).
2. Simulator `ATC ID` if not empty.
3. Otherwise unknown. **The `config,<id>` parameter is never used as a registration** until real liveries show it is
   consistently one; it can be stored as an opaque `ConfigurationId` for diagnostics.
4. Never the folder name or the display name.

**Operator strategy.** `icao_airline` from the livery if present; otherwise null. Never derived from a name.

**Catalog requirement.** A `SynapticInstalledAircraftCatalog` is only useful for liveries that exist as files:
iniManager installs and third-party liveries under `Community`. On this machine the aircraft and its livery pack are
Marketplace `.fsarchive` content and **cannot be scanned**; the ATC ID is then the only registration source. Decision
deferred to BLOCK 10B after the live ATC ID reading: if ATC ID is populated for Marketplace liveries, the catalog is P2;
if it is empty, the catalog is P1 and the shared MSFS installation locator must move out of FSGAP.Fenix (gap G-X2).

**Unknowns.** Livery folder naming convention, presence of `[FLTSIM]` fields, whether `ATC ID` is populated, whether
the loaded title differs per preset.

## 7. Generic FSGAP 0.9.0 telemetry matrix (G)

Status vocabulary: `GENERIC_VALIDATED` (read live on the A220 and cross-checked), `GENERIC_PLAUSIBLE` (official or
structural evidence that the stock SimVar is honoured, not read live), `GENERIC_WRONG` (proven wrong),
`SYNAPTIC_OVERLAY_REQUIRED` (the state exists only in documented Synaptic variables), `NOT_SUPPORTED` (no source at
all), `NOT_TESTED` (a stock candidate exists, no evidence either way). "Live value" is empty everywhere: the A220 was
not loaded. Fields examined: 74 (the `Engines` and `Units`/`FlapSurfaces` fields are counted once, not per instance).

### 7.1 Flight state and warnings

| FSGAP field | Live value | Status | Overlay required | Notes |
|---|---|---|---|---|
| `Flight.OnGround` | — | GENERIC_PLAUSIBLE | no | Simulator-owned physics; the aircraft uses the MSFS flight model (changelog "Flight Model": gear compression, suspension, Mach drag). |
| `Flight.LatitudeDegrees`, `LongitudeDegrees` | — | GENERIC_PLAUSIBLE | no | Simulator-owned. |
| `Flight.AltitudeFeet`, `HeightAboveGroundFeet` | — | GENERIC_PLAUSIBLE | no | Simulator-owned. |
| `Flight.RadioAltitudeFeet` | — | NOT_SUPPORTED | — | Unavailable generically; no Synaptic radio-altitude value documented (only callout aurals). |
| `Flight.IndicatedAirspeedKnots`, `GroundSpeedKnots`, `VerticalSpeedFeetPerMinute` | — | GENERIC_PLAUSIBLE | no | Simulator-owned. The displays may use their own air data; the SimVar stays the simulator's. |
| `Flight.TouchdownVerticalSpeedFeetPerMinute` | — | GENERIC_PLAUSIBLE | no | Simulator-owned; changelog 1.0.10 tunes touchdown behaviour in the stock model. |
| `Flight.HeadingMagneticDegrees`, `PitchDegrees`, `BankDegrees`, `GLoad` | — | GENERIC_PLAUSIBLE | no | Simulator-owned. |
| `Flight.AngleOfAttackDegrees`, `GrossWeightKilograms` | — | GENERIC_PLAUSIBLE | no | Simulator-owned. Weight: the EFB loadsheet loads the simulator's payload stations (documented feature). |
| `Flight.BodyAccelerationXG/YG/ZG` | — | GENERIC_PLAUSIBLE | no | Simulator-owned. |
| `Warnings.Overspeed` | — | NOT_TESTED | maybe | Stock warning depends on `aircraft.cfg` limits; the aircraft has its own `L:A22X Aural Overspeed` / `Aural Overspeed Pre Alert`. Cross-check in 10B. |
| `Warnings.FlapSpeedExceeded`, `GearSpeedExceeded` | — | NOT_TESTED | maybe | Same; no Synaptic equivalent documented. |
| `Warnings.Stall` | — | NOT_TESTED | maybe | `L:A22X Aural Stall` exists for cross-check. |

### 7.2 Engines (I)

| FSGAP field | Live value | Status | Overlay required | Notes |
|---|---|---|---|---|
| `Engines[n].Running` | — | NOT_TESTED | unknown | Custom PW1500G/FADEC simulation; whether `GENERAL ENG COMBUSTION` is maintained is undocumented. Fenix (also custom) does maintain it. |
| `Engines[n].N1Percent`, `N2Percent`, `EgtCelsius` | — | NOT_TESTED | unknown | No Synaptic engine variable documented at all; the EICAS values cannot be reached by LVAR. Compare `TURB ENG N1/N2`, `GENERAL ENG EXHAUST GAS TEMPERATURE` with the EICAS in 10B. Non-zero is not proof. |
| `Engines[n].FuelFlowKilogramsPerHour` | — | NOT_TESTED | unknown | Same. |
| `Engines[n].StarterActive` | — | NOT_TESTED | unknown | `L:A22X Eng Start Mode` is the selector, not the starter. |
| `Engines[n].OilTemperatureCelsius`, `OilPressurePsi` | — | NOT_TESTED | unknown | Changelog 1.0.9/1.0.10 fix EICAS oil rounding/colour, which suggests aircraft-computed values; SimVar path unknown. |
| `Engines[n].ThrottleLeverPercent` | — | NOT_TESTED | unknown | The aircraft consumes the stock `THROTTLE*` events (inputs doc); `L:A22X Throttle n TLA` is animation only. |
| `Engines[n].ReverserEngaged` | — | NOT_TESTED | unknown | Stock reverse events are consumed (inputs doc). |
| `Engines[n].FireDetected` | — | NOT_SUPPORTED | — | No detection variable. `Aural Left/Right Engine Fire` is an aural, `L/R Eng Fire` is the pushbutton (§13). |
| `Engines[n].FireHandlePulled` | — | SYNAPTIC_OVERLAY_REQUIRED | yes | `L:A22X L Eng Fire` / `R Eng Fire`: "fire pushbutton has been pressed". Same semantics as the Fenix field (crew action). |
| `Engines[n].FireWarningLit` | — | NOT_SUPPORTED | — | No fire light variable documented. |

### 7.3 Landing gear, brakes, steering

| FSGAP field | Live value | Status | Overlay required | Notes |
|---|---|---|---|---|
| `LandingGear.HandleDown` | — | GENERIC_PLAUSIBLE | no | No Synaptic gear-handle variable and no gear input event are documented: the stock gear handle is used. `L:A22X Alternate Gear` is the separate emergency handle. |
| `LandingGear.Units[nose/left-main/right-main].ExtensionPercent` | — | GENERIC_PLAUSIBLE | no | Stock gear (changelog tunes stock compression/suspension). Semantics to confirm live: transit values, alternate extension. |
| `LandingGear.BrakeLeftPercent`, `BrakeRightPercent` | — | NOT_TESTED | maybe | Inputs doc: `BRAKES_LEFT` / `BRAKES_RIGHT` "ramp the same shared commanded brake pressure as `BRAKES`". Differential braking is not modelled from inputs; whether `BRAKE LEFT/RIGHT POSITION` reflects the commanded pressure, and in what scale, is unknown (Fenix precedent: scaling issue). |
| `LandingGear.SteeringInputPercent` | — | GENERIC_PLAUSIBLE | no | Inputs doc: `STEERING_SET` is "used as the tiller's output that reports the commanded steering position back to the simulator". Sign and range to confirm. |
| `LandingGear.AntiskidActive` | — | NOT_TESTED | unknown | No Synaptic variable; stock `ANTISKID BRAKES ACTIVE` behaviour unknown. |
| parking brake | — | (no field) | — | Gap G-B1. `L:A22X Parking Brake` (commanded) and stock `BRAKE PARKING POSITION` both candidates. |

### 7.4 Flight controls (J)

| FSGAP field | Live value | Status | Overlay required | Notes |
|---|---|---|---|---|
| `FlightControls.FlapsHandlePercent` | — | GENERIC_PLAUSIBLE | optional | Changelog 1.0.4: "`FLAPS HANDLE INDEX` variable not set to current detent" fixed for third-party compatibility; `FLAPS_*` events map to detents 0–5. **Information loss:** six detents (0, 1, 2, 3, 4, FULL) collapse into a percentage; `L:A22X Flap Lever` (0–5) or `FLAPS HANDLE INDEX` keeps the detent (gap G-C1). |
| `FlightControls.FlapSurfaces[trailing-left/right].ExtensionPercent` | — | NOT_TESTED | unknown | The aircraft simulates flap/slat travel times (changelog 1.0.10); whether `TRAILING EDGE FLAPS LEFT/RIGHT PERCENT` follows is unknown. |
| `FlightControls.SpeedBrakeDeploymentPercent` | — | NOT_TESTED | unknown | `L:A22X Spoiler Lever` is the lever "written for animation", not the surfaces. Stock `SPOILERS LEFT/RIGHT POSITION` unknown (Fenix precedent: wrong scale, masked). |
| `FlightControls.AileronLeft/RightDeflectionPercent`, `ElevatorDeflectionPercent`, `RudderDeflectionPercent` | — | NOT_TESTED | unknown | Fly-by-wire: pilot input (`L:A22X L Sidestick X/Y`, `Rudder Pedals`, animation only) ≠ command ≠ surface. The sidestick variables **must never** feed these fields. The stock deflection SimVars may reflect the surfaces if the FBW drives the stock control system; unverified. |

Pilot input / command / actual surface separation on the A220, as documented:

| Layer | Documented source | FSGAP use |
|---|---|---|
| Pilot input | `L:A22X L Sidestick X/Y`, `L:A22X Rudder Pedals` (animation), stock `AXIS_*` events consumed | none (animation) |
| Command | none documented (FBW/PFCC internal) | none |
| Actual surface | stock deflection SimVars, unverified | current fields, NOT_TESTED |
| Flap lever detent | `L:A22X Flap Lever` 0–5; stock `FLAPS HANDLE INDEX` maintained since 1.0.4 | gap G-C1 |
| Stabilizer | `L:A22X Horizontal Stabilizer` (animation ratio) | none |

### 7.5 APU (L)

| Concept | Source | Semantic | FSGAP field | Status |
|---|---|---|---|---|
| APU selector | `L:A22X APU Switch` 0 Off / 1 Run / 2 Start | CONTROL_POSITION | `Apu.MasterSwitchOn` (Run or Start ⇒ on) | SYNAPTIC_OVERLAY_REQUIRED |
| APU running | none documented; stock `APU PCT RPM` unverified | — | `Apu.Running` | NOT_TESTED |
| APU available | none documented | — | `Apu.Available` | NOT_TESTED (stock RPM threshold would be a guess) |
| APU generator commanded | `L:A22X APU Gen Off` (switch selected off) | CONTROL_POSITION | none (gap G-E1) | overlay candidate |
| APU generator online | none documented; `APU Gen Fail Lamp` is a fault, `APU Gen Off Lamp` an annunciator | — | none | NOT_SUPPORTED |
| APU bleed commanded | `L:A22X APU Bleed Off` (switch selected off) | CONTROL_POSITION | `Apu.BleedOn` (selected = not off) | SYNAPTIC_OVERLAY_REQUIRED; stock `PNEUMATICS APU BLEED AIR` NOT_TESTED |
| APU bleed flowing | none documented | — | none | NOT_SUPPORTED |
| APU fault | `L:A22X APU Gen Fail Lamp`, `APU Bleed Fail Lamp` | FAULT_INDICATION | none (gap G-E1/G-P1) | overlay candidate |
| APU fire | `L:A22X Aural APU Fire` only (aural playing) | EVENT | `Apu.FireDetected` | NOT_SUPPORTED. No APU fire pushbutton variable is documented either: `Apu.FireHandlePulled` NOT_SUPPORTED |

Selector position is never equated with operating state: `APU Switch = Run` says nothing about RPM or AVAIL.

### 7.6 Fuel pumps (K)

Documented: `L:A22X L Boost Pump` and `R Boost Pump`, enum **0 Off / 1 Auto / 2 On** (switch position). No pump
pressure, no low-pressure fault, no operating state.

| FSGAP field | Status | Notes |
|---|---|---|
| `FuelPumps[left/right].IsOn` | SYNAPTIC_OVERLAY_REQUIRED **with a contract gap** | `IsOn` (two-state) cannot carry AUTO. AUTO must not be flattened to true or to false. |
| `FuelPumps[*].Fault` | NOT_SUPPORTED | no documented source |

Proposed vendor-neutral evolution (gap G-F1, **P0**): add `TelemetryValue<FuelPumpMode> Mode` with
`FuelPumpMode { Off, Auto, On }` next to `IsOn`; keep `IsOn` for two-position pumps (Fenix) and leave it Unavailable
when the pump has a three-position switch; `IsOperating` and `Fault` stay Unavailable until a source exists. The
enum is a fuel-pump concept, not a generic integer switch; the same three values recur on other A220 selectors (§10).

### 7.7 Pressurization, environment

| FSGAP field | Live value | Status | Overlay required | Notes |
|---|---|---|---|---|
| `Pressurization.CabinAltitudeFeet`, `CabinAltitudeRateFeetPerMinute` | — | NOT_TESTED | unknown | Custom pressurization (`Man Press`, `Manual Rate`, `Auto Press Fail Lamp`, `Aural Cabin Altitude` documented); no cabin altitude variable documented. Whether stock `PRESSURIZATION CABIN ALTITUDE` is written is unknown. |
| `Environment.OutsideAirTemperatureCelsius`, `WindDirectionDegreesTrue`, `WindSpeedKnots`, `Precipitation`, `PrecipitationRateMillimeters` | — | GENERIC_PLAUSIBLE | no | Simulator weather, aircraft-independent (read live on the Fenix session at FL226 today: −21.7 °C, 276°/13 kt, none). |

### 7.8 Sections with no generic source

| FSGAP field | Status | Notes |
|---|---|---|
| `InertialReferences[*].Mode/Aligned/Fault` | NOT_SUPPORTED | §14 |
| `ElectricalBuses[*].Powered` | NOT_TESTED | no Synaptic bus state; stock `ELECTRICAL MAIN BUS VOLTAGE` index meaning unknown |
| `Batteries[*].VoltageVolts` | NOT_TESTED | no Synaptic battery variable; stock `ELECTRICAL BATTERY VOLTAGE:n` index meaning unknown (Fenix: index 2 wrong) |
| `HydraulicSystems[*].PressurePsi`, `ReservoirPercent` | NOT_TESTED | no Synaptic pressure/quantity variable; stock `HYDRAULIC PRESSURE:1..3` index meaning unknown (Fenix: index 3 wrong) |
| `HydraulicSystems[*].Pressurized` | NOT_SUPPORTED | no source, no threshold |
| `FireZones[*].FireDetected` | NOT_SUPPORTED | `Aural Cargo Fire` / `Aural Smoke` are aurals |

### 7.9 Coverage (H)

| Count | |
|---|---|
| Fields examined | 74 |
| GENERIC_VALIDATED | 0 |
| GENERIC_PLAUSIBLE | 28 (19 flight state, gear handle, gear units, steering input, flap handle, 5 environment) |
| GENERIC_WRONG | 0 (nothing could be proven wrong without a live read) |
| SYNAPTIC_OVERLAY_REQUIRED | 4 (engine fire pushbutton, APU master, APU bleed selected, fuel pump mode) |
| NOT_SUPPORTED | 11 (radio altitude, engine fire detection and light, APU fire detection and handle, 3 IRS, pump fault, hydraulic pressurized, fire zones) |
| NOT_TESTED | 31 (4 warnings, 10 engine fields, APU available/running, buses, batteries, hydraulic pressure/reservoir, 2 brakes, antiskid, flap surfaces, speed brake, 4 deflections, 2 pressurization) |

- **Validated generic coverage: 0 / 74 = 0 %.** Nothing was read on the A220.
- **Documented-plausible generic coverage: 28 / 74 = 38 %.** Fields the official documentation or the simulator's
  ownership of the value supports.
- **Potential generic coverage: (28 + 31) / 74 = 80 %.** The ceiling if every NOT_TESTED stock SimVar proves right in
  10B. It is a ceiling, not a claim; the Fenix precedent (speed brake scale, hydraulic index 3, battery index 2)
  says some will not.
- The 15 remaining fields (20 %) need an overlay (4, with one P0 contract change) or have no source (11).

## 8. Synaptic variable inventory (full detail in the CSV)

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

## 9. Multistate controls (>2 meaningful positions)

| Control | Values | Reusable concept |
|---|---|---|
| `L/R Boost Pump` | Off / Auto / On | `FuelPumpMode` (G-F1, P0) |
| `PTU`, `ACMP 2B/3A/3B` | Off / Auto / On | hydraulic pump selector mode (G-H1, P2) |
| `L/R Cowl Anti Ice`, `Wing Anti Ice` | Off / Auto / On | `AntiIceMode` (G-I1, P1) |
| `Crossbleed` | Closed / Auto / Open | pneumatic valve selector (G-P1, P1) |
| `APU Switch` | Off / Run / Start | APU selector (G-U1, P2; `MasterSwitchOn` suffices for P0) |
| `Bus Isolation Mode` | Main / Auto / Ess | electrical selector (G-E1, P1) |
| `Autobrake` | RTO / OFF / LO / MED / HI | application-specific |
| `Flap Lever` | detents 0–5 | `FlapsDetentIndex` (G-C1, P1) |
| `Manual Transfer`, `Fwd Cargo Air`, `Taxi Lights`, `Emergency Lights`, `Seat Belt`, `No PED`, `Eng Start Mode`, `Emer Transmitter`, `Annun Lights` | 3–4 positions | deferred / not relevant |

Recommendation: one small vendor-neutral enum per **system concept** (`FuelPumpMode`, `AntiIceMode`, a
`SelectorMode { Off, Auto, On }` shared by hydraulic pumps and anti-ice if the semantics prove identical). No
generic "integer switch value" and no per-vendor enum.

## 10. Pneumatics (M), hydraulics (N), electrical (O), fire (P), anti-ice (Q), IRS (R), alerts (S)

### 10.1 Pneumatics — control / actual / fault

| Item | Control | Actual state | Fault | FSGAP today |
|---|---|---|---|---|
| Left bleed | `L Bleed Off` | none | `L Bleed Fail Lamp` | none |
| Right bleed | `R Bleed Off` | none | `R Bleed Fail Lamp` | none |
| APU bleed | `APU Bleed Off` | none (stock `PNEUMATICS APU BLEED AIR` untested) | `APU Bleed Fail Lamp` | `Apu.BleedOn` (selection semantics) |
| Crossbleed | `Crossbleed` Closed/Auto/Open | none | none | none |
| Left / right pack | `L/R Pack Off` | none | `L/R Pack Fail Lamp` | none |
| Pack flow | `Pack Flow` (HI) | `Pack Flow Lamp` (UNKNOWN) | none | none |
| Ram air | `Ram Air` | `Ram Air Lamp` (UNKNOWN) | none | none |
| Trim air | `Trim Air Off` | `Trim Air Off Lamp` (UNKNOWN) | none | none |
| Temperature | three knobs, `Manual Temperature`, cargo air | none | none | none |

Contract coverage: none. Proposed `PneumaticsTelemetry` (bleed sources with `Selected`/`Fault`, crossbleed mode,
packs with `Selected`/`Fault`) is **P1**: no consumer uploads pneumatics today.

### 10.2 Hydraulics — control vs state

| Item | Control | Pressure / state | FSGAP today |
|---|---|---|---|
| PTU | `PTU` Off/Auto/On | none | none |
| ACMP 2B, 3A, 3B | `ACMP …` Off/Auto/On | none | none |
| System 1 / 2 SOV | `Hyd 1/2 SOV` (selected on) | `Hyd 1/2 SOV Lamp` (CLOSED, UNKNOWN) | none |
| System 1/2/3 pressure | — | **none documented**; stock `HYDRAULIC PRESSURE:1..3` untested | `HydraulicSystems[*].PressurePsi` |
| Fault lamps | — | none beyond the SOV lamps | — |

No pressure may be inferred from a selector. `HydraulicSystems` stays empty (or present with every value
Unavailable) until a live read proves the stock indices; the product page's "hydraulic failure modelling" is a
simulation feature, not a readable state.

### 10.3 Electrical — minimum useful normalized subset

Documented: generator switches (L, R, APU) with FAIL/OFF lamps, generator disconnect switches with OIL/DISC lamps, RAT
switch with an "online" lamp, bus isolation selector, cabin power switch, 55 circuit breakers (write). Not documented:
bus powered states, voltages, currents, battery state.

Proposed subset (P1, `ElectricalSourceTelemetry { Id, Name, Selected, Fault, Online }` as a collection
`ElectricalSources`; buses and batteries stay as they are):

| Id | Selected | Fault | Online |
|---|---|---|---|
| `gen-1`, `gen-2` | `L/R Gen Off` inverted | `L/R Gen Fail Lamp` | Unavailable |
| `gen-apu` | `APU Gen Off` inverted | `APU Gen Fail Lamp` | Unavailable |
| `rat` | `RAT Gen` | Unavailable | `RAT Gen Lamp` (documented meaning) |

Plus one `BusIsolationMode` (Main/Auto/Ess) only if a consumer asks (P2). No circuit breakers.

### 10.4 Fire — control / warning / detection / aural / test

| Concept | Engine 1 / 2 | APU | Cargo |
|---|---|---|---|
| Control | `L/R Eng Fire` (pushbutton pressed: cuts power sources, arms extinguishing) → `Engines[n].FireHandlePulled` | **none documented** | none |
| Warning light | none documented | none | none |
| Detection | none documented → `FireDetected` Unavailable | none | none (`FireZones` empty) |
| Aural | `Aural Left/Right Engine Fire` (playing) | `Aural APU Fire` | `Aural Cargo Fire`, `Aural Smoke` |
| Test | none documented | none | none |
| Agent discharge | none documented | none | none |

An aural is never mapped to `FireDetected` or `FireWarningLit`. The A220 fire section of a future session therefore
carries only the two pushbutton states.

### 10.5 Anti-ice

| Item | Control | Actual / fault |
|---|---|---|
| Left / right engine cowl | `L/R Cowl Anti Ice` Off/Auto/On | none |
| Wing | `Wing Anti Ice` Off/Auto/On | none |
| Probe heat | `Probe Heat` (read/write; the ICCP "clears this back to off itself each tick (ground probe-heat test pulse)") | `Probe Heat Lamp` (ground-test lamp) |
| Window / windshield heat | `L/R Side Window Heat Off`, `L/R Windshield Heat Off` | OFF lamps (UNKNOWN) |

`AntiIceTelemetry { EngineCowl[n].Mode, Wing.Mode, ProbeHeatOn?, WindowHeat[*].Selected }` with
`AntiIceMode { Off, Auto, On }` is useful and vendor-neutral (Fenix has ENG/WING anti-ice pushbuttons too).
Priority **P1**: initial A220 support ships without it. Probe heat stays out until the self-clearing behaviour is
understood live.

### 10.6 IRS / inertial

Search of the official documentation for IRS, IRU, ADIRU, inertial, alignment, navigation source, reversion: only
`L/R IRS Reversion` and `L/R ADS Reversion` (pushbuttons pressed), `Reversion Mode` (display), `L/R Nav Source`
(toggle when set), and changelog notes that FMS fuel entry is blocked "when IRSs are not aligned" (so alignment is
simulated, but not exposed). **`InertialReferences` cannot be populated honestly: Unavailable (empty collection,
capability not declared).** No undocumented discovery.

### 10.7 Alerts, CAS, aurals

| Signal | Kind | Documented variable |
|---|---|---|
| Master caution lit | state (WARNING_INDICATION) | `Caution PBA` |
| Master warning lit | state (WARNING_INDICATION) | `Warning PBA` |
| Acknowledge | button action (write) | `Master Caution Warning` |
| Generic attention-getter | aural | `Aural Warning`, `Aural Caution` |
| CAS messages | **not exposed** (changelog names messages such as `L ICE DET FAIL`, `FDR ACCEL FAIL`, but no variable carries the CAS list) | — |
| 90 other aurals | aural (event) | `Aural …` |
| FAIL lamps | fault indications | 13 lamps |

A general multi-aircraft need exists (Fenix has MASTER WARNING/CAUTION lights and fire aurals too): a small
`AircraftAlert` state (`MasterCautionLit`, `MasterWarningLit`) is **P1**; a `CockpitEvent` stream (aural started,
button pressed) is **P2** and should wait for a consumer, as decided for Fenix (audit gap G-C2).

## 11. H-Events and input events

- **H-Events:** the SimVars page announces "L-Vars and H-Events" and lists **none**. Status: NOT_DOCUMENTED. Nothing
  was sent.
- **Input events** (`/pilots/inputs`): stock MSFS key events are consumed for brakes, autobrake, elevator/aileron/rudder
  axes and trims (stock trim axis events are deliberately masked), spoilers, autopilot disconnect, the whole FCP,
  engine masters, engine anti-ice (`ANTI_ICE_SET_ENG1/2`), throttles and reversers, flaps (mapped to detents),
  steering, transponder, COM/NAV radios, `BAROMETRIC`, `PAUSE_ON`. Consequences for FSGAP: no aircraft-specific code is
  needed for anything an application would *command* through standard MSFS bindings, and the standard **readable**
  counterparts of those inputs (flap handle index, throttle lever position, steering, parking brake, transponder,
  COM frequencies) are the natural generic candidates to test first. FSGAP reads only.

## 12. Failure capability (T)

| Capability | Status | Evidence |
|---|---|---|
| Catalog | NOT_FOUND | No failure list in the docs, the changelog, the EFB feature list, the installed readable files or the `synaptic.s3db` persistence schema (one pilot-waypoints table). The product page's "hydraulic failure modelling" and "persistent maintenance" describe simulation behaviour, not an API. |
| Read active | NOT_DOCUMENTED | Only FAIL lamps, `Caution/Warning PBA` and aurals exist; they are indications, not a failure list (rule of §41). |
| Trigger | NOT_FOUND | No documented variable, event, EFB page or endpoint. |
| Clear | NOT_FOUND | Same. |
| Stable external IDs | NOT_FOUND | None documented. |

The EFB is an in-sim HTML instrument (`html_ui/pages/vcockpit/instruments/efb-a220`); no local HTTP interface is
documented, unlike the Fenix EFB, and none was looked for. No LVAR write, HTTP call, WASM or process memory inspection,
or file patching was performed. **Future `FSGAP.Synaptic` sessions declare `FailureCapabilities.None` and use
`UnsupportedFailureProvider`.**

## 13. Contract gaps (U)

| Gap | Why | Proposed normalized solution | Priority |
|---|---|---|---|
| G-F1 fuel pump mode | `IsOn` cannot express Off/Auto/On; flattening AUTO would lie | `FuelPumpTelemetry.Mode : TelemetryValue<FuelPumpMode>` (`Off`, `Auto`, `On`); `IsOn` kept for two-position pumps | **P0** |
| G-C1 flap detent | 6 detents lose information in `FlapsHandlePercent` | optional `FlightControls.FlapsDetentIndex : TelemetryValue<int>` (0 = up), generic source `FLAPS HANDLE INDEX` for every aircraft | P1 |
| G-B1 parking brake | no field; FSHANGAR already reads `BRAKE PARKING POSITION` | `LandingGear.ParkingBrakeSet : TelemetryValue<bool>` (generic source, Synaptic cross-check) | P1 |
| G-I1 anti-ice | no section; A220 and Fenix both have it | `AntiIceTelemetry` (engine cowl modes, wing mode, probe/window heat selected) with `AntiIceMode` | P1 |
| G-A1 master alerts | no field for master caution/warning | `AlertsTelemetry { MasterCautionLit, MasterWarningLit }` | P1 |
| G-E1 electrical sources | `ElectricalBuses`/`Batteries` cannot carry generator selection/fault/RAT | `ElectricalSources` collection (`Selected`, `Fault`, `Online`) | P1 |
| G-P1 pneumatics | no section | `PneumaticsTelemetry` (bleeds, crossbleed, packs: selected + fault) | P1 |
| G-D1 richer descriptor | detection has only TITLE/ATC ID/LIVERY FOLDER/LIVERY NAME; the A220 title is unknown and may be generic | detector also reads the string SimVars `ATC MODEL`, `ATC TYPE`, `CATEGORY` into the existing `AircraftDescriptor.Model/Manufacturer` (contract already has the fields) | P1 (transport change, no contract change) |
| G-X2 shared MSFS locator | `MsfsInstallationLocator` is internal to FSGAP.Fenix; a Synaptic catalog would duplicate it | move to a vendor-neutral place (FSGAP.Core or a small FSGAP.Msfs assembly) | P1 if a catalog is built, else P2 |
| G-H1 hydraulic selectors | pump modes and SOVs have no field | `HydraulicSystemTelemetry.PumpModes` / `ShutoffValveSelected` | P2 |
| G-U1 APU selector | Off/Run/Start beyond `MasterSwitchOn` | `Apu.SelectorMode` | P2 |
| G-A2 cockpit events | aurals, button presses | `CockpitEvent` stream | P2 (needs a consumer) |

Everything else (autobrake, flight guidance modes, exterior lights, cabin controls, circuit breakers) stays outside
FSGAP: it is not a universal cockpit API.

## 14. Future FSGAP.Synaptic (V) and overlay size (W)

```text
MSFS
 |
 +--> FSGAP.SimConnect            generic telemetry (unchanged), descriptor (+ ATC MODEL/TYPE, G-D1)
 |
 +--> FSGAP.Synaptic
        SynapticAircraftProvider      Match: production rule from the live capture (Dedicated); AttachAsync
        SynapticIdentityResolver      §5 values; registration per §6; no guess
        SynapticInstalledAircraftCatalog   only if ATC ID proves empty for Marketplace liveries (§6)
        SynapticGenericTelemetryPolicy     masks the stock values 10B proves wrong (mirror of the Fenix policy)
        SynapticSystemTelemetrySource      documented L:A22X variables, batched on ISimulatorVariableReader
        SynapticTelemetryComposer          generic → mask → overlay → freshness (TransformedTelemetryProvider)
        capabilities                       only sections with a proven source; Failures = None
        failures                           UnsupportedFailureProvider
                 |
                 v
          AircraftTelemetry  ──►  FSHANGAR, FLIPPP (normalized only; never A22X names, H-Events or raw values)
```

Minimum production overlay (after 10B proves each source):

| | |
|---|---|
| Synaptic-specific variables proposed | **6 for P0** (`L/R Boost Pump`, `APU Switch`, `APU Bleed Off`, `L/R Eng Fire`); **up to 40** with the P1 gaps (anti-ice 3, flap lever 1, parking brake 1, master caution/warning 2, bleeds/packs/crossbleed 5 + 5 fail lamps, generators 3 + 3 fail lamps, RAT 2, bus isolation 1, hydraulic selectors 6, `Lamp Test` 1 to discount lamps during a test) |
| Polling groups | 2: `alerts` (master caution/warning) at 1 s; `systems` (selectors, fail lamps) at 2 s. One emitted struct each, one native request each |
| Cadence | 1 s + 2 s (selectors and lamps hold for seconds; no 10 Hz) |
| Estimated additional reads/s | 1.5, while a Synaptic session lives and the A220 is loaded. With the generic 1.8 and the identity 0.2–0.5: **about 3.5–3.8 native reads/s**, on the single connection |

## 15. Live validation (X)

| Domain | Validation | Result | Notes |
|---|---|---|---|
| Official variable index | DOCUMENTATION ONLY | 397 L-vars, 0 H-Events | repo commit `4abccfd` |
| Installed package, version, presets | LIVE (files, read-only) | Marketplace `fs24-inibuilds-aircraft-a220`, version unreadable, two presets | |
| Live descriptor of the A220 | NOT TESTED | aircraft not loaded | |
| Candidate detection rules, negative case | LIVE TEST | Fenix A321: no rule fires, zero Synaptic reads | 2026-09-26 19:00 |
| Generic telemetry on the A220 | NOT TESTED | | the same four groups flowed on the Fenix session at FL226 |
| Synaptic LVAR probe | NOT TESTED | harness ready (`--synaptic-probe`) | |
| Failure interface | DOCUMENTATION ONLY + installed files | none found | |
| Persistence database schema | LIVE (files, read-only copy) | `__syn_pilot_waypoints` only | |
| Golden fixtures | NOT CREATED | no live data; the harness writes them (`--synaptic-fixture <dir>`) | |

## 16. Architecture confirmation (Y)

| | |
|---|---|
| Native SimConnect connections | 1 (the harness reads through `ISimulatorVariableReader` on the transport) |
| Production Synaptic provider added | NO |
| Production Synaptic overlay added | NO (guard tests: no `Synaptic`/`A22X`/`A220` name in the transport, no `A22X`/`INI_GPU` string in the Abstractions, Core, SimConnect and Fenix binaries) |
| A220 write operations | NO |
| Failure injection | NO |
| FSHANGAR / FLIPPP / fenixhangarweb modified | NO |
| FSGAP version | 0.9.0 unchanged |

## 17. Risks

- **Title and folder conventions unknown.** Detection and registration cannot be designed with confidence until one
  live capture exists. Two presets may yield two titles.
- **Marketplace content is opaque.** No manifest, no `aircraft.cfg`, no livery files: version cannot be pinned and a
  livery catalog cannot scan Marketplace liveries. iniManager installs (Community folder) would be scannable but were
  not present.
- **Version drift.** The docs track 1.0.10; the Marketplace build may lag. Variable names are documented but not
  promised stable; the 1.0.x changelogs already changed variable behaviour (`FLAPS HANDLE INDEX`, aural resets).
- **Lamps are ambiguous.** 39 OFF/ON/CLOSED lamps have no documented driver; only transitions observed live can
  classify them. `Lamp Test` must be read to discount lamps during a test.
- **Write-action variables.** 81 documented variables are write triggers; reading them is harmless but their value is
  meaningless. `Probe Heat` self-clears each tick.
- **Stock SimVars may be wrong,** as on Fenix (spoilers, hydraulic index 3, battery index 2). Non-zero is not
  validation; every NOT_TESTED row needs an EICAS/synoptic comparison.
- **Performance.** The aircraft is known to be heavy on the main thread (changelog 1.0.9); polling stays slow and
  batched.
- **Numbering collision.** "BLOCK 10" in the extraction plan is the FLIPPP migration; this series uses 10A/10B for
  the Synaptic work.

## 18. Recommended BLOCK 10B

1. **Live capture first (no code change).** With MSFS 2024 and the A220 loaded (both presets, one Marketplace
   livery, one third-party livery if available): `dotnet run --project samples/FSGAP.SimConnect.Console --
   --synaptic-probe --synaptic-fixture <dir>` in cold-and-dark, then APU on with APU bleed on, engines running at
   the gate, a taxi with brakes and steering, and one flight segment with flaps and gear transitions. Record TITLE,
   ATC ID, LIVERY FOLDER, LIVERY NAME; compare every NOT_TESTED row of §7 with the EICAS/synoptic pages; note each
   lamp transition.
2. **Contracts (FSGAP.Abstractions, 0.10.0):** G-F1 `FuelPumpMode` (P0). Add the P1 gaps only where the capture
   proves a source: G-C1 `FlapsDetentIndex`, G-B1 `ParkingBrakeSet`, G-I1 `AntiIceTelemetry`, G-A1 alerts. Nothing
   else.
3. **Transport (FSGAP.SimConnect):** G-D1, read `ATC MODEL`/`ATC TYPE`/`CATEGORY` into the descriptor if the title
   proves insufficient. Optional generic `FLAPS HANDLE INDEX` and `BRAKE PARKING POSITION` for every aircraft.
4. **FSGAP.Synaptic (new assembly):** recognizer from the captured title(s) with the positive-marker rule and the
   negative set of §4; identity per §5; generic policy masking what the capture proved wrong; overlay with the P0 six
   variables and the proven P1 ones; capabilities per proven section; failures None. Move `MsfsInstallationLocator`
   out of FSGAP.Fenix only if a catalog is needed.
5. **Tests:** recognizer positives (captured titles) and negatives (Fenix, iniBuilds, PMDG, Asobo, C172, titles
   containing "A220"); mapper tests for every enum (Off/Auto/On never collapses); golden fixture from the capture;
   composition and lifecycle tests mirroring the Fenix ones; architecture tests (no SimConnect reference in
   FSGAP.Synaptic, `A22X` names confined to one file, no write API).
6. **Live qualification:** a second session with the provider attached, single connection confirmed, every
   declared section compared with the cockpit, then the report.
7. **Version:** 0.10.0 after the qualification, packages produced, applications untouched until then.
