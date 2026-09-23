# Existing implementation → normalized FSGAP contract

BLOCK 1 of FSGAP_SDK, 2026-09-23. Evidence and file references are in
[fenix-existing-code-audit.md](fenix-existing-code-audit.md). **No contract has been changed.** The gaps below are
proposals for later blocks.

> **Status after BLOCK 2 (version 0.2.0).** This document is kept as the BLOCK 1 snapshot. The P1 gaps are now in
> the contracts:
>
> - G-S1/G-S2: simulator contracts;
> - G-X1: `FsgapOptions`;
> - G-I1: `LiveryFolder`;
> - G-I3: `IInstalledAircraftCatalog`;
> - G-T1/G-T2: height above ground and touchdown vertical speed;
> - G-T3: `Warnings`;
> - G-T7: `LandingGear`;
> - G-T8 (flaps only): flap handle vs `FlapSurfaces`;
> - G-T12: freshness;
> - G-F1/G-F6: `FailureKey` + `FailureCatalog`.
>
> References to `FailureType` and `FlightControls.FlapsExtensionPercent` below describe the BLOCK 0 model; both
> were removed. P2 and P3 gaps are still open.

Column meanings:

- **Source:** FSH = FSHANGAR client, FLP = FLIPPP client, both = identical or near-identical code in both.
- **Class:** GENERIC (stock MSFS), FENIX (Fenix-specific), DERIVED (computed by an app), GEN/FNX (generic SimVar
  whose index or semantics is Fenix-specific).
- **FSGAP target:** member of the BLOCK 0 model, `—` when none exists.
- **Gap:** `No`, or a gap id from §4.
- **Action later:**
  - *Generic provider*: implement in the future generic SimConnect layer, shared by every provider.
  - *FSGAP.Fenix*: implement in the Fenix provider, internal.
  - *App*: stays in the application.
  - *Drop*: do not migrate.

---

## 1. Telemetry

### 1.1 Flight state

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `FastGroupVars.SimOnGround` | SimVar `SIM ON GROUND` (Bool), 1 Hz | on ground | GENERIC | `Flight.OnGround` | No | Generic provider |
| both | `FastGroupVars.AglFt` | `PLANE ALT ABOVE GROUND` (Feet), 1 Hz | geometric height above ground | GENERIC | — (`RadioAltitudeFeet` is a different quantity) | **G-T1** | Generic provider |
| both | `FastGroupVars.AltitudeFtMsl` | `PLANE ALTITUDE` (Feet), 1 Hz | MSL altitude | GENERIC | `Flight.AltitudeFeet` | No | Generic provider |
| both | `FastGroupVars.IasKt` | `AIRSPEED INDICATED` (Knots), 1 Hz | IAS | GENERIC | `Flight.IndicatedAirspeedKnots` | No | Generic provider |
| both | `FastGroupVars.GroundSpeedKt` | `GROUND VELOCITY` (Knots), 1 Hz | GS | GENERIC | `Flight.GroundSpeedKnots` | No | Generic provider |
| both | `FastGroupVars.VerticalSpeedFps` → ×60 | `VERTICAL SPEED` (Feet per second), 1 Hz | VS | GENERIC | `Flight.VerticalSpeedFeetPerMinute` | No (conversion inside the provider) | Generic provider |
| both | `FastGroupVars.PitchDeg` / `BankDeg` | `PLANE PITCH DEGREES` / `PLANE BANK DEGREES` (Degrees; the native unit is radians) | attitude | GENERIC | `Flight.PitchDegrees` / `BankDegrees` | No, but **the sign convention must be verified** (MSFS pitch is positive nose-down) | Generic provider |
| both | `FastGroupVars.GForce` | `G FORCE` (GForce), 1 Hz | load factor | GENERIC | `Flight.GLoad` | No | Generic provider |
| both | `EnvironmentGroupVars.LatitudeDeg` / `LongitudeDeg` | `PLANE LATITUDE` / `PLANE LONGITUDE` (Degrees), 10 s | position | GENERIC | `Flight.LatitudeDegrees` / `LongitudeDegrees` | No, but see G-T13 (cadence) | Generic provider |
| none | — | (no heading SimVar is read) | magnetic heading | GENERIC | `Flight.HeadingMagneticDegrees` | No (in the contract, no consumer yet) | Generic provider, optional |
| both | `FastGroupVars.TouchdownNormalVelocityFps` | `PLANE TOUCHDOWN NORMAL VELOCITY` (ft/s), 1 Hz | touchdown rate | GENERIC | — | **G-T2** | Generic provider |
| both | `FastGroupVars.AoaDeg` | `INCIDENCE ALPHA` (Degrees) | angle of attack | GENERIC | — | G-T4 | Generic provider (FSH upload only) |
| both | `FastGroupVars.TotalWeightKg` | `TOTAL WEIGHT` (Kilograms) | gross weight | GENERIC | — | G-T4 | Generic provider (FSH upload only) |
| both | `FastGroupVars.AccelerationBodyX/Y/Z` | `ACCELERATION BODY X/Y/Z` (GForce) | body accelerations | GENERIC | — | G-T5 | Generic provider (FSH wear only) |
| FSH 5 s, FLP 1 Hz | `OverspeedWarning` | `OVERSPEED WARNING` (Bool) | VMO/MMO exceedance | GENERIC | — | **G-T3** | Generic provider, **≥ 1 Hz** |
| FSH 5 s, FLP 1 Hz | `FlapSpeedExceeded` | `FLAP SPEED EXCEEDED` (Bool) | VFE exceedance | GENERIC | — | **G-T3** | Generic provider, ≥ 1 Hz |
| FSH 5 s, FLP 1 Hz | `GearSpeedExceeded` | `GEAR SPEED EXCEEDED` (Bool) | VLE exceedance | GENERIC | — | **G-T3** | Generic provider, ≥ 1 Hz |
| both | `SystemsGroupVars.StallWarning` | `STALL WARNING` (Bool), 5 s | stall warning | GENERIC | — | G-T3 | Generic provider |

### 1.2 Engines (index `n` = 1, 2; `EngineTelemetry.Index = n`)

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `Engine{n}Combustion` | `GENERAL ENG COMBUSTION:n` (Bool), 5 s | engine running | GENERIC | `Engines[n].Running` | No | Generic provider |
| both | `Eng{n}N1Pct` / `Eng{n}N2Pct` | `TURB ENG N1:n` / `TURB ENG N2:n` (Percent) | spool speeds | GENERIC | `Engines[n].N1Percent` / `N2Percent` | No | Generic provider |
| both | `Eng{n}EgtC` | `GENERAL ENG EXHAUST GAS TEMPERATURE:n` (Celsius) | EGT | GENERIC | `Engines[n].EgtCelsius` | No | Generic provider |
| both | `Eng{n}FuelFlowPph` | `ENG FUEL FLOW PPH:n` (lb/h) | fuel flow | GENERIC | `Engines[n].FuelFlowKilogramsPerHour` | No (converted inside the provider) | Generic provider |
| both | `Eng{n}OilTempC` / `Eng{n}OilPressurePsi` | `GENERAL ENG OIL TEMPERATURE:n` / `OIL PRESSURE:n` | oil | GENERIC | — | **G-T6** | Generic provider (FLP engine envelope uses them) |
| both | `Engine{n}StarterActive` | `GENERAL ENG STARTER ACTIVE:n` | starter | GENERIC | — | G-T6 | Generic provider |
| both | `Eng{n}ThrottlePct` | `GENERAL ENG THROTTLE LEVER POSITION:n` | thrust lever | GENERIC | — | G-T6 | Generic provider |
| both | `Reverse{n}Engaged` | `GENERAL ENG REVERSE THRUST ENGAGED:n` | reverser | GENERIC | — | G-T6 | Generic provider (FSH UI) |
| both | `Eng{n}AntiIce` (registry only) | `ENG ANTI ICE:n`, **confirmed wrong on Fenix** | anti-ice | GEN/FNX | — | — | Drop (no Fenix source) |
| both (FSH experimental) | `L:I_ENG_FIRE_n`, `L:I_OH_FIRE_ENGn_BUTTON` | LVAR, 10 Hz | ENG FIRE warning light | FENIX | `Engines[n].FireDetected` **only once proven** | UNCERTAIN (lights also turn on during a test) | FSGAP.Fenix; leave Unavailable until proven |

### 1.3 APU

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `ApuBleedActive` | `PNEUMATICS APU BLEED AIR` (Bool), 5 s | APU bleed | GENERIC | `Apu.BleedOn` | No, but **not verified on Fenix** | Generic provider; FSGAP.Fenix confirms it or marks it Unavailable |
| both (registry only) | `ApuRpmPct`, `ApuStarterPct`, `ApuGeneratorActive` | `APU PCT RPM`, `APU PCT STARTER`, `APU GENERATOR ACTIVE`, **confirmed wrong on Fenix** | APU state | GEN/FNX | `Apu.Running` / `Available` | No contract gap; **no Fenix source exists yet** | FSGAP.Fenix, new LVAR discovery (out of scope until a consumer asks) |
| none | — | APU MASTER / START not read | APU master | FENIX | `Apu.MasterSwitchOn` | No contract gap; no source | Leave Unavailable |
| FSH (discovery) | `L:S_OH_FIRE_APU_TEST`, `L:S_OH_FIRE_APU_BUTTON`, `L:S_OH_FIRE_APU_AGENT` | LVAR, 10 Hz | APU fire test / handle / agent | FENIX | — (crew action, not state) | G-C2 | FSGAP.Fenix internal (defer) |

### 1.4 Inertial references, fuel pumps, fire

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both (discovery) | `L:S_OH_NAV_IR{1,2,3}_MODE` | LVAR 0/1/2, 10 Hz | ADIRS mode selector | FENIX | `InertialReferences[i].Mode` (Off / Navigation / Attitude) | No | FSGAP.Fenix |
| none | — | alignment and IR FAULT not read | aligned / fault | FENIX | `InertialReferences[i].Aligned` / `Fault` | No contract gap; no source | Leave Unavailable |
| both (discovery) | `L:S_OH_FUEL_{LEFT,CENTER,RIGHT}_{1,2}` | LVAR 0/1, 10 Hz | tank pump **switch** position | FENIX | `FuelPumps[id].IsOn` with ids `left-1`, `left-2`, `center-1`, `center-2`, `right-1`, `right-2` | Doc only (G-C1: `IsOn` = selected, not pressure) | FSGAP.Fenix |
| none | — | pump FAULT lights not read | pump fault | FENIX | `FuelPumps[id].Fault` | No contract gap; no source | Leave Unavailable |
| both (discovery / experimental) | `L:S_OH_FIRE_ENG{1,2}_TEST`, `L:S_OH_CARGO_SMOKE_TEST`, agent pushbuttons, fire handles, MASTER WARNING switch | LVAR counters and switches | crew actions on the fire panel | FENIX | — | G-C2 | FSGAP.Fenix internal (defer) |
| both (experimental) | Agent lights `L:I_OH_FIRE_ENG{1,2}_AGENT{1,2}_{L,U}`, `L:I_MIP_MASTER_WARNING_{CAPT,FO}[_L]` | LVAR lights | fire-test reaction signals | FENIX | — | G-C3 | FSGAP.Fenix internal diagnostic |
| none | — | cargo and lavatory smoke detection not read | fire zones | FENIX | `FireZones[]` | No contract gap; no source | Leave empty |

### 1.5 Gear, flight controls, brakes

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `GearHandleDown` | `GEAR HANDLE POSITION` (Percent), 5 s | gear lever | GENERIC | — | **G-T7** | Generic provider (FSH UI) |
| both | `GearPositionPct` | `GEAR CENTER POSITION` (Percent) | nose gear extension (used as a proxy) | GENERIC | — | G-T7 | Generic provider |
| both | `FlapsPositionPct` | `FLAPS HANDLE PERCENT` (Percent), 5 s | flap lever | GENERIC | `FlightControls.FlapsExtensionPercent` is ambiguous (lever or surface?) | **G-T8** | Generic provider |
| both | `FlapsLeftPct` / `FlapsRightPct` | `TRAILING EDGE FLAPS LEFT/RIGHT PERCENT` (verified on Fenix) | flap surfaces | GENERIC | — | G-T8 | Generic provider |
| both (registry only) | `SlatsLeft/RightPct` | `LEADING EDGE FLAPS …`, **not wired on Fenix** | slats | GEN/FNX | — | — | Drop for Fenix |
| both (registry only) | `SpoilerLeft/RightPct` | `SPOILERS LEFT/RIGHT POSITION`, wrong scale on Fenix | spoilers | GEN/FNX | `FlightControls.SpeedBrakeDeploymentPercent` | No; **Unavailable for Fenix until verified** | Generic provider; Fenix override |
| both | `AileronLeft/RightPct`, `ElevatorPct`, `RudderPct` | `* DEFLECTION PCT` | surface deflections | GENERIC | — | G-T8 | Generic provider (FSH wear only) |
| both | `BrakeLeft/RightFraction` ×100 | `BRAKE LEFT/RIGHT POSITION` (returns 0–1) | brake pedals | GENERIC | — | G-T8 | Generic provider |
| both | `SteerInputPct`, `AntiskidActive` | `STEER INPUT CONTROL`, `ANTISKID BRAKES ACTIVE` | steering / antiskid | GENERIC | — | G-T8 | Generic provider (FSH upload only) |
| both (registry only) | `BrakeTemperatureLeft/Right`, `GearSkiddingFactor` | no stock SimVar | — | FENIX (future) | — | — | Drop |

### 1.6 Hydraulics, electrical, pressurization, environment

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `HydPumpPressureGreen/Blue` | `HYDRAULIC PRESSURE:1/2` (Psi) | circuit pressure | GEN/FNX (index 1 = green, 2 = blue; 3 = yellow is **broken**) | `HydraulicSystems[green\|blue].PressurePsi` (and `Pressurized` derived from a threshold) | No | FSGAP.Fenix owns the index→circuit mapping; yellow stays Unavailable |
| both | `HydReservoirPctGreen/Blue` | `HYDRAULIC RESERVOIR PERCENT:1/2` | reservoir quantity | GEN/FNX | — | G-T9 | FSGAP.Fenix |
| both (registry only) | `HydPumpActive*`, PTU, reservoir air pressure | unverified or no SimVar | — | — | — | — | Drop |
| both | `BatteryVoltageBat1` | `ELECTRICAL BATTERY VOLTAGE:1` (BAT2 = index 2 is **broken**) | battery voltage | GEN/FNX | — (`ElectricalBuses` has only `Powered`) | G-T9 | FSGAP.Fenix |
| both (registry only) | AC/DC bus volts and amps, battery load and capacity | no usable SimVar | buses | FENIX (future LVARs) | `ElectricalBuses[]` | No contract gap; no source | Leave empty |
| both | `CabinAltitudeFt`, `CabinAltitudeRateFps` | `PRESSURIZATION CABIN ALTITUDE` / `…RATE` | pressurization | GENERIC | — | G-T10 | Generic provider (FSH upload only) |
| both | `AmbientTempC`, `WindDirectionDeg`, `WindSpeedKt`, `PrecipState`, `PrecipRateMmH` | `AMBIENT *`, 10 s | weather | GENERIC | — | **G-T11** | Generic provider (FSH UI and upload) |
| both (registry only) | `StructuralIcePct` | `STRUCTURAL ICE PCT`, never read | icing | GENERIC | — | G-T11 | Later |

---

## 2. Aircraft identification

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `AircraftIdentityVars.Title` | SimVar `TITLE` (String256), 2 s | aircraft.cfg title (for example `FenixA320 CFM WF`) | GENERIC | `AircraftDescriptor.Title` | No | Generic detection source |
| both | `AircraftIdentityVars.LiveryFolder` | `LIVERY FOLDER` (String256), 2 s | exact livery folder name | GENERIC | — (`Livery` is a display name, `PackagePath` is the package) | **G-I1** | Generic detection source |
| both | `AircraftIdentityVars.LiveryName` | `LIVERY NAME` | livery display name | GENERIC | `AircraftDescriptor.Livery` | No (unused today) | Generic detection source |
| both | `AircraftIdentityVars.AtcId` | `ATC ID` (**empty on Fenix**) | registration | GENERIC | `AircraftDescriptor.Registration` | No | Generic detection source |
| both | `FenixCockpitAirframeMatcher` | regex `Fenix\D{0,4}(319\|320\|321)` on TITLE, then LIVERY FOLDER | Fenix yes/no plus model | FENIX | `FenixAircraftProvider.Match` → `AircraftIdentity.Model/Family` | No (replace the placeholder heuristic) | FSGAP.Fenix, **BLOCK 3** |
| FSH | `RunSessionCoordinator.DetectFamily` | `Contains("A319"/"A321"/"A320")` on TITLE | family | DERIVED | same | No | Drop (replaced by the matcher) |
| both | `EngineWingtipDetector` | TITLE tokens and livery `required_tags` (`CFM`/`IAE`, `SL`/`WF`) | engine / wingtip | FENIX | `AircraftIdentity.EngineVariant` / — | G-I2 (wingtip) | FSGAP.Fenix |
| both | `LocalLiveryRegistrationResolver` + `LiveryCacheDatabase.FindByLiveryFolderName` | LIVERY FOLDER → SQLite livery catalog | registration (reconstructed) | FENIX (filesystem) | `AircraftIdentity.Registration` | G-I3 | FSGAP.Fenix |
| both | `FenixLiveryScanner`, `FenixPackageLocator`, `AirframeDetector`, `RegistrationExtractor`, `IniConfigFile`, `AirlineIcaoNormalizer`, `LiverySelection` | filesystem (livery.cfg, aircraft.cfg, JSON, manifest) | installed Fenix aircraft inventory | FENIX | — | **G-I3** | FSGAP.Fenix, behind a normalized contract |
| both | `Msfs2024InstallationLocator` | `UserCfg.opt` → `InstalledPackagesPath` | MSFS 2024 install and package roots | GENERIC (MSFS) | — | G-S4 | Generic MSFS layer |
| both | `icao_airline` from the scan | filesystem | operator ICAO | FENIX | — | G-I2 | FSGAP.Fenix |
| none | — | `ATC MODEL` / `ATC TYPE` / ICAO type not read | ICAO type designator | GENERIC | `AircraftDescriptor.IcaoType` | No | Optional; the Fenix provider fills `IcaoType` from the model |

---

## 3. Failures

| Source | Existing code | Technical mechanism | Meaning | Class | FSGAP target | Gap | Action later |
|---|---|---|---|---|---|---|---|
| both | `FenixFailureInjector.InjectAsync` | `POST 127.0.0.1:8083/fenix/failures/saveManual` `failed:true` | trigger | FENIX | `IFailureProvider.TriggerAsync(FailureCommand)` | **G-F1** (vocabulary) | FSGAP.Fenix, BLOCK 6 |
| both | `FenixFailureInjector.ResetAsync` | same with `failed:false` | clear | FENIX | `IFailureProvider.ClearAsync` | G-F1 | FSGAP.Fenix |
| both | `TryConfirmViaManualListAsync` | `GET /fenix/failures/manual` | readback | FENIX | internal to Trigger/Clear confirmation, **and** `GetActiveFailuresAsync` (list the `failed:true` entries) | No | FSGAP.Fenix |
| both | `FenixFailureCatalog` + embedded JSON (384) | resource | id → Fenix title, ATA | FENIX | internal to FSGAP.Fenix, plus the normalized failure catalog | G-F1 / G-F6 | FSGAP.Fenix |
| both | `FenixLocalApiProbe` | `GET /` with a 2 s timeout | is the EFB gateway up | FENIX | — | **G-F3** | FSGAP.Fenix |
| both | `FenixInjectionResult` Success / Failed / Timeout | — | outcome | FENIX | `FailureCommandResult` Succeeded / Failed | G-F2 (Timeout and Unreachable not distinct) | FSGAP.Fenix |
| FSH | Initial INOP, scheduled failures, webapp commands (`fenix_failure_id` from fenixhangarweb) | server API | scenario delivery and ack | DERIVED / app | — | Should not be in FSGAP; but the **ids must become normalized keys** (G-F1) | App plus fenixhangarweb |
| FLP | `FailureScenarioCatalog` (24 hard-coded Fenix ids), generator, runner, `FinalizeAsync` reset | local | challenge failures | app | — | same | App (switch to normalized keys) |
| FSH | Settings "MANUAL FENIX FAILURE TEST" (catalog combo, Send/Reset) | UI | diagnostic | app | a catalog of `IFailureProvider`-supported failures | G-F6 | App diagnostic on top of the FSGAP catalog |
| FSH | `EngFireTestProbe` | LVAR reactions over a window | FIRE TEST health | FENIX (experimental) | — | G-C3 | FSGAP.Fenix internal diagnostic, not public |

---

## 4. Gap analysis

### A. Already representable (no contract change)

| Need | FSGAP member |
|---|---|
| On ground, MSL altitude, IAS, GS, VS, pitch, bank, G, lat/lon | `FlightStateTelemetry` |
| Engine running (combustion), N1, N2, EGT, fuel flow | `EngineTelemetry` |
| APU bleed (and future APU master/running/available) | `ApuTelemetry` |
| ADIRS IR1/IR2/IR3 mode | `InertialReferenceTelemetry.Mode` |
| Six fuel pump switches | `FuelPumpTelemetry.IsOn` |
| Hydraulic green/blue pressure | `HydraulicSystemTelemetry.PressurePsi` / `Pressurized` |
| Values known to be wrong on Fenix (APU RPM, yellow hydraulics, BAT2, slats, spoilers) | `TelemetryValue.Unavailable` plus per-section `TelemetryCapabilities`, exactly the BLOCK 0 principle |
| Active failures read from the EFB list | `IFailureProvider.GetActiveFailuresAsync` (unmapped ids → `FailureType.Unclassified`) |
| Choosing the Fenix provider for a detected aircraft | `AircraftProviderRegistry` + `FenixAircraftProvider.Match` (placeholder rule to be replaced) |
| Snapshot plus stream | `ITelemetryProvider` (groups become internal to the provider) |

### B. Small contract extensions (proposed, not applied)

Priorities:

- **P1**: needed to migrate existing behaviour.
- **P2**: needed by a current consumer, but only for display or upload.
- **P3**: nice to have, or needed only by FSH's wear upload.

| Id | Need found | Current contract | Recommended change | Priority |
|---|---|---|---|---|
| G-S1 | Simulator connection: state (disconnected / waiting / connected / error), 5 s retry, `Paused`, `Crashed`, monotonic elapsed clock, consumer-exception isolation | nothing (BLOCK 0 has no simulator notion) | New `ISimulatorConnection` in Abstractions (state, events). Implementation in a SimConnect transport assembly. | **P1** |
| G-S2 | Aircraft detection source (2 s identity poll) and "aircraft changed" / "flight loaded" events | `AircraftDescriptor` exists; no producer | `IAircraftDetector` (current descriptor plus change event) in Abstractions; SimConnect implementation | **P1** |
| G-S3 | Simulator services: nearest airport (FSH PRODUCTION); airport parking and `FlightLoad` (FLP WIP) | nothing | `ISimulatorFacilities` / `IFlightLoader`, separate from aircraft providers | P2 (nearest airport), P3 (the rest, after FLP's WIP settles) |
| G-S4 | MSFS 2024 installation and package roots | nothing | Internal utility of the MSFS layer (not public at first) | P2 |
| G-X1 | Host configuration: SimConnect client name (`FSHangar`/`FLIPPP`), storage folder for caches (`%LOCALAPPDATA%\FenixHangar` vs `\FLIPPP`), logging | nothing | `FsgapOptions` (application name, data directory) plus a logging hook | **P1** |
| G-I1 | `LIVERY FOLDER` is the key to the Fenix registration | `AircraftDescriptor.Livery` means the display name | Add `AircraftDescriptor.LiveryFolder` | **P1** |
| G-I2 | Wingtip configuration (sharklets / fence), operator ICAO | not in `AircraftIdentity` | Add `AircraftIdentity.OperatorIcao` and `WingtipConfiguration` (open strings) | P2 |
| G-I3 | Installed-aircraft inventory: Fenix livery scan, registration from livery folder, variant data, cache | nothing | `IInstalledAircraftCatalog` (scan with progress, lookup by livery folder or registration), returning normalized records. Fenix implementation internal. | **P1** |
| G-T1 | Height above ground (geometric AGL, not radio altimeter) | only `RadioAltitudeFeet` | Add `Flight.HeightAboveGroundFeet` | **P1** |
| G-T2 | Touchdown normal velocity | — | Add `Flight.TouchdownVerticalSpeedFeetPerMinute` (sign convention documented) | **P1** |
| G-T3 | Overspeed, flap speed, gear speed and stall warnings, sampled at ≥ 1 Hz | — | Add a `WarningsTelemetry` section (bools) | **P1** |
| G-T4 | Angle of attack, gross weight | — | Add to `FlightStateTelemetry` | P3 |
| G-T5 | Body accelerations X/Y/Z | — | Add to `FlightStateTelemetry` | P3 |
| G-T6 | Engine oil temperature and pressure, starter, throttle lever, reverser | — | Add to `EngineTelemetry` | P2 |
| G-T7 | Gear lever and per-leg extension | — | Add a `GearTelemetry` section (handle down, collection of legs) | **P1** |
| G-T8 | Flaps **lever** vs **surface** (left/right); control-surface deflections; brakes, steering, antiskid | only the ambiguous `FlapsExtensionPercent` | Split into `FlapsHandlePercent` plus surface values; add deflections; add a small `BrakesTelemetry` | P1 (flaps), P3 (rest) |
| G-T9 | Hydraulic reservoir quantity; battery voltage | `HydraulicSystemTelemetry` has no quantity; no battery concept | Add `ReservoirPercent`; add a `Batteries` collection (id, voltage) | P3 |
| G-T10 | Cabin altitude and rate | — | Add a `PressurizationTelemetry` section | P3 |
| G-T11 | Weather: OAT, wind, precipitation, structural ice | — | Add an `EnvironmentTelemetry` section | P2 |
| G-T12 | Staleness: no watchdog today, merged values never expire | `TelemetryValue` has no age | The provider turns values older than a threshold into `Unknown`. Add `AircraftTelemetry.SectionTimestamps` only if a consumer needs it. | **P1** |
| G-T13 | Cadence: the apps rely on about 1 Hz (flight), 5 s (systems), 10 s (position) and 10 Hz (cockpit) | one stream `Interval` | Keep the internal groups inside the provider. Document the per-section update-rate guarantee (lat/lon faster than 10 s would change FLP route animation: decide explicitly). | P2 |
| G-F1 | Failure vocabulary: about 30 distinct ids in active use (nav, FMGC, displays, ice, pneumatics, tyres, vibration, smoke…); ids stored server-side | 8-value `FailureType` enum | Replace the closed enum with a **normalized failure catalog**: a string key per failure plus target, ATA chapter and title. The Fenix mapping is internal to FSGAP.Fenix. **Requires a user decision** because fenixhangarweb stores `fenix_failure_id`. | **P1 (blocker for BLOCK 6)** |
| G-F2 | Distinguish timeout and "failure backend unreachable" | `FailureCommandResult` has Succeeded / NotSupported / Rejected / Failed | Add `Unavailable` (backend not reachable) and possibly `TimedOut` | P2 |
| G-F3 | Failure backend availability (the EFB gateway exists only while a Fenix is loaded) | capabilities are static per session | Add `IFailureProvider.GetAvailabilityAsync` (or a status property); capabilities stay static | P2 |
| G-F6 | Listing the available failures (diagnostic combo, scenario authoring) | only `TriggerableTypes` | Covered by the catalog of G-F1 | P1 (with G-F1) |
| G-C1 | Fuel pump `IsOn` means switch selected, not pump running | doc ambiguity | Clarify the XML doc (no API change) | P2 |
| G-C2 | Crew actions (FIRE TEST pressed, fire handle pulled, agent discharged, ADIRS moved) as events | nothing | **Defer.** A normalized `CrewActionEvent` stream only once a consumer really uses it. Today every consumer is inert or logs only. | P3 |
| G-C3 | ENG FIRE TEST probe | nothing | Keep as an **internal** FSGAP.Fenix diagnostic. No public API. | P3 |

### C. Should not be in FSGAP

| Item | Owner |
|---|---|
| Flight phases: FSH `FlightTrackingPhase`, FLP `FlightMotionPhaseResolver` and `FlightSession` state machine (two different rule sets, each tuned for its product) | Apps |
| Landing and touchdown analysis, G min/max, speed-violation events, engine envelope, crash threshold (1500 fpm) | Apps |
| Scoring, debrief, progression, challenges, `AircraftMatcher` (loaded vs expected aircraft), route progress and animation | FLP |
| Failure scenario generation, scheduling (`trigger_after_seconds`), persistence, server acks, command polling, failure-handling tracking | Apps (and fenixhangarweb) |
| Maintenance wear, native-flight upload, outbox, SQLite buffers, heartbeat, RUN start gate, Flight Prep gates, SimBrief, airline logos | FSH |
| Parking suitability and ordering, launch readiness policy (the Fenix wingspan data could move later with the aircraft) | FLP |
| Partial-sample merge (`SimConnectTelemetryMerge`) | Replaced by FSGAP full snapshots; drop |
| STKP / SimToolkitPro | Deprecated. Do not migrate. |
