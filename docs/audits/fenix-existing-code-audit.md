# Fenix / MSFS integration audit — FSHANGAR and FLIPPP

BLOCK 1 of FSGAP_SDK. Read-only audit, 2026-09-23.

This document inventories every mechanism FSHANGAR and FLIPPP use today to talk to MSFS 2024 and to the Fenix
A319/A320/A321. It classifies each one and records what must be preserved when the mechanisms move behind FSGAP.
The line-by-line mapping to FSGAP contracts is in [fenix-fsgap-mapping.md](fenix-fsgap-mapping.md), and the
migration sequence is in [fenix-extraction-plan.md](fenix-extraction-plan.md).

Conventions:

- File references are relative to each application's `windows-client/` folder unless stated otherwise.
- `FSH` = FSHANGAR Windows client, `FLP` = FLIPPP Windows client.
- Test counts are counts of `[Fact]`/`[Theory]` attributes. No application test was executed (read-only audit).

Status legend:

| Status | Meaning |
|---|---|
| PRODUCTION | Reached from the app in normal use, and its output changes user-visible behaviour or server data. |
| USED | Reached at runtime, but the output only goes to logs, diagnostics or display. |
| EXPERIMENTAL | Wired and running, explicitly provisional per its own documentation. |
| DISCOVERY ONLY | Read and logged only, to learn semantics. Nothing consumes the result. |
| DEAD CODE | No production caller (tests at most). |
| UNCOMMITTED WIP | Exists only in uncommitted or untracked files of a working tree. |
| UNKNOWN | Could not be determined statically. |

---

## 1. Repositories audited

| | FSGAP_SDK | FSHANGAR (Windows client) | FLIPPP |
|---|---|---|---|
| Path | `C:\Users\Jean\fsgap_sdk` | `C:\Users\Jean\fenixhangarclient` | `C:\Users\Jean\FLIPPP` |
| Remote | `stellinaconseil-lang/fsgap_sdk` | `stellinaconseil-lang/fenixhangarclient` | `stellinaconseil-lang/FLIPPP` |
| Branch | `main` | `feature/fshangar-client-v2` | `main` |
| HEAD | `353ca52` | `84f3c96` (equal to origin, checked with `git ls-remote`) | `1343f1b` (equal to origin) |
| Working tree at start | clean | clean | **12 local changes** from other ongoing work: 6 modified files, plus untracked `Launch/`, `Sim/Parking/`, `SimConnectFlightLoader.cs`, `TargetAircraftWatcher.cs`, `MainWindow.StartInMsfs.cs` and tests |

**FLIPPP was being edited by another session during the audit.** `Sim/SimConnectFlightLoader.cs` (22:43) and
`MainWindow.StartInMsfs.cs` (22:47) changed while they were being read. After the reading was over, a new untracked
file `Flippp.Core/Runtime/DevStartInMsfsFlags.cs` appeared at 22:58, while this audit was only writing inside
fsgap_sdk. All of these changes are in the "Start in MSFS" WIP area. FLIPPP's HEAD did not change. Findings in those files are marked
UNCOMMITTED WIP and their line numbers are approximate. This audit changed nothing in FLIPPP or FSHANGAR, and ran
no `git fetch` against them.

### 1.1 Which repository is "FSHANGAR"

| Candidate | Remote / state | Verdict |
|---|---|---|
| `C:\Users\Jean\fenixhangarclient` | `fenixhangarclient.git`, `feature/fshangar-client-v2` @ `84f3c96`, synced with origin | **Audited.** Contains the SimConnect connection, telemetry, Fenix cockpit monitor and Fenix failure injection. |
| `C:\Users\Jean\fenixhangarclient-fresh` | Same remote and branch, @ `5a6523c` | A strict ancestor of the audited HEAD (older clone). Not audited separately. |
| `C:\Users\Jean\fenixhangarweb` | `fenixhangarweb.git` | FSHANGAR web and server. It never talks to MSFS, but it hosts **STKP** (§14) and stores `fenix_failure_id` values (§7.5). Audited only for those two points. |
| `C:\Users\Jean\FenixHangarWebConnect` | **No remote, no commit.** Files dated 2026-08-21. | An earlier prototype (WinForms tray, P/Invoke SimConnect, MobiFlight LVAR client), not referenced by the client. Not a production component: DO NOT MIGRATE (§14). |
| Branch `origin/FenixHangarClientPC` (in `fenixhangarclient`) | Last commit `dd81cc0`, 2026-08-25 | A GPLv3 fork of the third-party *Flight-Recorder* project, explicitly rejected in `PORTING_PLAN.md`. DO NOT MIGRATE. |
| Branch `origin/feature/flippp` (in `fenixhangarclient`) | Last commit `5b4a8f8`, 2026-09-16 | The exact snapshot FLIPPP vendored (§10). |
| `Sources/` (Swift, macOS client) in both repos | — | No simulator access; the code comments say "macOS client cannot read SimConnect directly". Out of scope. |

### 1.2 FLIPPP's vendored FenixHangar copy

`FLIPPP/windows-client/src/FenixHangar.Client.{Core,Wpf}` and `tests/FenixHangar.Client.Core.Tests` are a copy of
the FSHANGAR client taken at `5b4a8f8`. They have their own solution (`FenixHangarClient.Windows.sln`) and their own
CI workflow. `FLIPPP.sln`, `Flippp.Core.csproj` and `Flippp.App.csproj` never reference them. FLIPPP's real code
(`Flippp.Core`) was bootstrapped on 2026-09-18 by copying and renaming the FSHANGAR simulator bricks (§10). The
vendored tree is therefore not part of the FLIPPP product and was not inventoried a second time.

---

## 2. FSGAP contracts this audit maps onto (BLOCK 0 recap)

- `IAircraftProvider` (`ProviderId`, `Match`/`CanHandle`, `AttachAsync`) opens an `IAircraftSession` (`Identity`,
  `Capabilities`, `Telemetry`, `Failures`).
- `AircraftDescriptor` holds raw detection facts: Title, Manufacturer, Model, IcaoType, Registration, Livery,
  PackagePath. `AircraftIdentity` holds normalized facts: Developer, Manufacturer, Family, Model, Variant,
  EngineVariant, IcaoType, Registration, Livery.
- `AircraftTelemetry` contains:
  - single objects: Flight, Apu, FlightControls;
  - collections: Engines, InertialReferences, FuelPumps, ElectricalBuses, HydraulicSystems, FireZones.
- Every scalar is a `TelemetryValue<T>` whose state is Unavailable, Unknown or Known.
- `AircraftCapabilities` declares, per session, which telemetry sections the provider reads and which `FailureType`s
  it can trigger or clear.
- Failures are expressed as `FailureType` (8 normalized values plus `Unclassified`) and `FailureTarget`, and use
  `FailureCommand`, `AircraftFailure` and `FailureCommandResult`.
- `AircraftProviderRegistry` picks the provider by `MatchSpecificity`.

Principles kept as-is:

- Unknown is not false or 0.
- A provider is not a session.
- Systems that exist in varying numbers are collections.
- Capabilities are discovered at runtime.
- No vendor id appears in the public API.
- Abstractions do not depend on SimConnect.

---

## 3. Integration technologies found

| Technology | FSH | FLP | Vendor-specific? | Notes |
|---|---|---|---|---|
| SimConnect via NuGet `SimConnect.NET 0.2.2` (typed struct data definitions) | yes | yes | no | The only SimConnect transport. The native `SimConnect.dll` ships in the package. |
| SimConnect SimVars (read) | 67 read + 4 identity strings | 67 read + 4 identity strings | no | Same registry. The FAST/SYSTEMS split differs by 3 variables (§10). |
| SimConnect LVARs (`L:`) (read) | 39 | 39 | **Fenix** | Polled at 10 Hz as one struct, for every aircraft. |
| SimConnect system events | `Crashed`, `Pause_EX1` (consumed) | same, **not consumed**. `FlightLoaded` (WIP). | no | — |
| SimConnect internal native calls reached by **reflection** into `SimConnect.NET.SimConnectNative` | `RequestFacilitiesList_EX1` | the same (dead code), plus `AddToFacilityDefinition`, `RequestFacilityData`, `FlightLoad`, `SubscribeToSystemEvent` (WIP) | no | Fragile across library versions. |
| SimConnect writes (`SetDataOnSimObject`, `TransmitClientEvent`, LVAR/HVAR writes) | none | none, except `SimConnect_FlightLoad` (WIP) | — | Neither app ever sets a cockpit variable. |
| HVARs (`H:`), K-events, calculator code, `execute_calculator_code` | none | none | — | Not found anywhere. |
| MobiFlight WASM / ClientData / named pipes | none | none | — | Found only in the unversioned WebConnect prototype. |
| HTTP to the **Fenix EFB gateway** `http://127.0.0.1:8083` | yes | yes | **Fenix** | Unofficial. `POST /fenix/failures/saveManual`, `GET /fenix/failures/manual`, `GET /`. |
| Filesystem: MSFS 2024 install (`UserCfg.opt` → `InstalledPackagesPath` → package roots) | yes | yes | no (MSFS) | — |
| Filesystem: Fenix packages, liveries (`livery.cfg`, `aircraft.cfg`, `*.json`, `manifest.json`) | yes | yes | **Fenix** | Livery catalog, registration and variant. |
| Filesystem: Fenix presets and `.flt` generation | no | yes (WIP) | **Fenix** | Automatic flight launch. |
| SQLite (`winsqlite3.dll` P/Invoke) | yes | yes | no | Livery cache, plus app buffers. |
| Third-party tracker **SimToolkitPro (STKP)** | fenixhangarweb only | no | third-party | **Deprecated. DO NOT MIGRATE** (§14). |

---

## 4. FSHANGAR — inventory

### 4.1 Transport and connections

| # | Item | File:line | Status |
|---|---|---|---|
| T1 | `SimConnectDataSource` main connection `"FSHangar"`, retry loop every 5 s | `Core/Simulator/SimConnectDataSource.cs:31,147-236` | PRODUCTION |
| T2 | Second, **diagnostic** `SimConnectDataSource` instance (Settings → Diagnostics → Advanced toggle). It runs every poll, including the 10 Hz LVAR poll. Its `SimulatorSampleReceived` has no subscriber. | `Core/AppState.cs:214-239` | USED (duplicate connection) |
| T3 | Short-lived third connection `"FSHangar.FacilityLookup"` for the nearest airport, through reflection on `SimConnect_RequestFacilitiesList_EX1` with hand-reconstructed struct layouts (a 64-byte header, 36 bytes of it unknown) | `SimConnectDataSource.cs:326-478` | PRODUCTION (departure and arrival checks) |
| T4 | System events `Crashed` (id 1) and `Pause_EX1` (id 2, where any non-zero `Data0` counts as paused) | `SimConnectDataSource.cs:175-197` | PRODUCTION (crash rule, recording pause) |
| T5 | `CsvReplayDataSource` | `Core/Simulator/CsvReplayDataSource.cs` | DEAD CODE (tests only; the "Telemetry Replay" toggle only disconnects) |

### 4.2 Data definitions (all batched; no per-variable read exists)

| Group | Content | Mechanism | Cadence |
|---|---|---|---|
| FAST | 19 SimVars | `SimVars.Subscribe<FastGroupVars>(SimConnectPeriod.Second)` | ~1 Hz, native |
| SYSTEMS | 41 SimVars | `PeriodicTimer` + `GetAsync<SystemsGroupVars>(0)` | 5 s |
| ENVIRONMENT | 7 SimVars | `PeriodicTimer` + `GetAsync` | 10 s (first position arrives only after 10 s) |
| Identity | `ATC ID`, `TITLE`, `LIVERY FOLDER`, `LIVERY NAME` (String256) | `PeriodicTimer` + `GetAsync` | 2 s, forever |
| Fenix cockpit | 39 LVARs, unit `"number"` | `PeriodicTimer` + `GetAsync<FenixCockpitVars>(0)` | **100 ms, whatever aircraft is loaded** |

The single source of SimVar names and units is `Core/Simulator/SimConnectVariableRegistry.cs`. It also holds
**42 more registry rows that are never subscribed** (status `ToTestMsfs` or `Unavailable`), each with its live-test
evidence dated 2026-08-31. That evidence is valuable knowledge for the SDK. For example:

- `APU PCT RPM`, `APU GENERATOR ACTIVE`: confirmed wrong on Fenix.
- `HYDRAULIC PRESSURE:3`: yellow circuit not wired on Fenix.
- `ELECTRICAL BATTERY VOLTAGE:2`: broken.
- `ENG ANTI ICE:n`: causal negative.
- Slats: not wired.
- Spoilers: wrong scale.
- Brake temperature: no stock SimVar exists.

The full SimVar list with consumers is in the mapping document (§1 there). Summary of local use besides the
server upload:

- `SIM ON GROUND`, `GENERAL ENG COMBUSTION:1/2`: coordinator gates (airborne, landing, cold start, engines off).
- `PLANE LATITUDE/LONGITUDE`: departure and arrival airport resolution, map.
- `PLANE TOUCHDOWN NORMAL VELOCITY`, `VERTICAL SPEED`: `CrashDetector` (≥ 1500 fpm).
- IAS, GS, altitude, N1/N2/EGT, fuel flow, gear handle, flaps handle, reversers, ambient temperature: Flight
  Tracking UI.
- Everything read is uploaded to `POST api/client/v1/native-flights/{id}/telemetry`, where the server wear
  engine uses it. **Every read SimVar is therefore PRODUCTION in FSHANGAR.**

### 4.3 Fenix-specific accesses

| Mechanism | Detail | Status |
|---|---|---|
| 39 cockpit LVARs | See §6. Consumed by `FenixCockpitMonitor` (edge detection) and `EngFireTestProbe`. Output goes **only to local logs** (`fenixhangar-*.log`, `trace-*.jsonl`). | EXPERIMENTAL / DISCOVERY ONLY |
| Fenix EFB HTTP | See §7. Initial INOP, scheduled failures, webapp commands and the manual diagnostic panel. | PRODUCTION |
| Livery catalog | See §9. Registration resolution and Hangar discovery. | PRODUCTION |

### 4.4 Derived logic in FSHANGAR (stays in the app unless noted)

| Logic | File | Status | Destination |
|---|---|---|---|
| Airborne / landing (on ground held 5 s) / engines-off gates | `Core/Run/RunSessionCoordinator.cs:1160-1183,1261-1274,1690-1701` | PRODUCTION | App (flight lifecycle) |
| Crash detection (`Crashed` event, or impact ≥ 1500 fpm) | `Core/Run/CrashDetector.cs` | PRODUCTION | The event goes to FSGAP (simulator event); the threshold stays in the app |
| Departure/arrival airport check (nearest facility vs server/SimBrief) | `RunSessionCoordinator.cs:860-969,1277-1326` | PRODUCTION | Nearest-airport lookup goes to FSGAP simulator services; the policy stays in the app |
| Recording paused while the sim is paused | `RunSessionCoordinator.cs:1705-1732` | PRODUCTION | The pause event goes to FSGAP; gating stays in the app |
| 3-minute SimConnect reconnect window, then pilot decision | `RunSessionCoordinator.cs:1536-1680` | PRODUCTION | App policy on top of FSGAP connection state |
| UI flight phase, events feed, great-circle distance and ETE, chart history | `Core/Run/FlightTrackingPhase.cs`, `FlightTrackingEvent.cs`, `GreatCircle.cs`, `TelemetryHistoryBuffer.cs` | USED | App |
| Partial-sample merge (`SimConnectTelemetryMerge`) | `Core/SimConnectTelemetrySample.cs:176-190` | PRODUCTION | Replaced by FSGAP full snapshots (§13, defect D1) |
| Unit fixes: VS ft/s×60, brake fraction ×100 | `Core/Simulator/LiveTelemetryMapper.cs:31,95-96` | PRODUCTION | FSGAP provider (unit normalization) |
| Server upload, SQLite buffer, outbox, heartbeat | `NativeFlight/*`, `DemoFlightDatabase.cs` | PRODUCTION | App |

---

## 5. FLIPPP — inventory

FLIPPP's `Flippp.Core/Sim` and `Flippp.Core/Fenix` are a renamed copy of the FSHANGAR bricks (§10). Only
differences and FLIPPP-specific usages are listed here.

### 5.1 Categories

| Cat. | Meaning | FLIPPP items |
|---|---|---|
| **A. Directly from MSFS** | stock SimVars and events | `SIM ON GROUND`, `PLANE ALT ABOVE GROUND`, `GROUND VELOCITY`, `VERTICAL SPEED`, `G FORCE`, `PLANE TOUCHDOWN NORMAL VELOCITY`, `OVERSPEED WARNING`, `FLAP SPEED EXCEEDED`, `GEAR SPEED EXCEEDED`, `GENERAL ENG COMBUSTION:1/2`, `TURB ENG N1/N2`, `EGT`, oil temp and pressure (both engines), `PLANE LATITUDE/LONGITUDE`, `TITLE`, `LIVERY FOLDER`, `ATC ID` (fallback). IAS and altitude are read in Demo mode only. Facility data and `FlightLoad` (WIP). |
| **B. Fenix-specific** | LVARs, EFB HTTP, Fenix files | 39 cockpit LVARs (wired to `FailureHandlingTracker`, but **functionally inert**: all 25 capabilities are `NotObservableYet`). Failure injection and clearing through `127.0.0.1:8083`. Livery scanner and registration. Preset catalog, preset resolver and `FenixGateStart.flt` template (WIP). |
| **C. Computed by FLIPPP from telemetry** | derived | Telemetry merge (gap-fill, no expiry). `FlightSession` state machine (taxi > 3 kt, landing stability 4 s, engines-off 4 s, 2 identity matches). `FlightMotionPhaseResolver` (±500 fpm, AGL 1500/3000 ft, go-around ≥ 800 fpm). Route progress (great circle, monotonic). `FlightAnalyzer` (touchdown fpm, G min/max, overspeed/flap/gear violations, engine envelope). |
| **D. Pure FLIPPP business logic** | — | Scoring, debrief, progression, challenge matching (`AircraftMatcher`), failure scenario generation (tier-based, 24 Fenix ids), failure runner timing, failure-handling tracker, parking suitability and ordering (WIP), launch preparation (WIP). |

Categories C and D **do not migrate to FSGAP**.

### 5.2 Usage facts specific to FLIPPP

- **About 45 of the 67 SimVars read are never consumed by FLIPPP.** They are inherited from FSHANGAR's wear-v2
  upload, which FLIPPP does not have.
- `Crashed` and `Pause_EX1` are subscribed but have no subscriber, so paused-sim samples are processed.
  `CrashDetector`, `FindNearestIcaoAsync`, `FenixLocalApiProbe.ProbeAsync`, `LocalThumbnailResolver`,
  `ExportCatalogToFile`, `SetManualPackageRoot` and `SimulatorSourceMode` are DEAD CODE in FLIPPP.
- FLIPPP moved `OVERSPEED WARNING`, `FLAP SPEED EXCEEDED` and `GEAR SPEED EXCEEDED` from the 5 s group to the 1 Hz
  group, because a 5 s cadence could miss a short exceedance (`Sim/SimConnectVariableRegistry.cs:66-73`).
  **This behaviour must be preserved.**
- The 10 Hz LVAR sample is marshalled to the UI thread with `Dispatcher.BeginInvoke` 10 times per second, for an
  inert result.
- The failure runner **clears** injected failures in `FinalizeAsync` (`Failures/FailureScenarioRunner.cs`).
  FSHANGAR never does.
- WIP launch flow (UNCOMMITTED):
  - query parking (facility data), then generate `Launch.flt`, then `SimConnect_FlightLoad`, then watch the
    target aircraft (3 consecutive matches, 5-minute timeout);
  - telemetry is suspended and resumed around the load, which resets the `ElapsedSeconds` clock mid-round;
  - three new SimConnect clients: `FLIPPP.FacilityLookup`, `FLIPPP.ParkingLookup`, `FLIPPP.FlightLoad`.

---

## 6. Known Fenix mechanisms — the exact reality

All of these are **read-only LVARs** with unit `"number"`, polled at 10 Hz. They are declared in
`SimConnectVariableRegistry.cs:353-409` (FSH) and their semantics live in `FenixCockpitControlRegistry.cs`. The
same code exists in FLP. "Verified" means that a live observation was recorded in the registry notes (first on
2026-09-12, C-FDRP, MSFS 2024 + Fenix A320).

| Mechanism | Exact variable | Kind / values | Verified | FSH status | FLP status |
|---|---|---|---|---|---|
| ENG 1 FIRE TEST | `L:S_OH_FIRE_ENG1_TEST` | **Monotonic counter**; never returns to 0; step +1, +2 or +10 | yes | EXPERIMENTAL (triggers the probe) | DISCOVERY ONLY (inert) |
| ENG 2 FIRE TEST | `L:S_OH_FIRE_ENG2_TEST` | counter | yes | EXPERIMENTAL | DISCOVERY ONLY |
| APU FIRE TEST | `L:S_OH_FIRE_APU_TEST` | counter (0→1, then 1→2 about 3 s later) | yes | DISCOVERY ONLY (no probe) | DISCOVERY ONLY |
| CARGO FIRE (SMOKE) TEST | `L:S_OH_CARGO_SMOKE_TEST` | counter | yes | DISCOVERY ONLY | DISCOVERY ONLY |
| ADIRS IR1 / IR2 / IR3 | `L:S_OH_NAV_IR1_MODE`, `_IR2_MODE`, `_IR3_MODE` | selector 0 OFF / 1 NAV / 2 ATT | yes | DISCOVERY ONLY | DISCOVERY ONLY |
| Fuel pumps (6) | `L:S_OH_FUEL_LEFT_1/2`, `L:S_OH_FUEL_CENTER_1/2`, `L:S_OH_FUEL_RIGHT_1/2` | switch 0 OFF / 1 ON (**switch position**, not pump pressure) | yes | DISCOVERY ONLY | DISCOVERY ONLY |
| ENG FIRE annunciators | `L:I_ENG_FIRE_1/2` | light 0/1 | yes | EXPERIMENTAL (probe input) | DISCOVERY ONLY |
| Fire handles | `L:S_OH_FIRE_ENG1/2_BUTTON`, `L:S_OH_FIRE_APU_BUTTON` | STOWED / PULLED | yes | DISCOVERY ONLY | DISCOVERY ONLY |
| Fire handle lights | `L:I_OH_FIRE_ENG1/2_BUTTON` | light 0/1 | yes | EXPERIMENTAL (probe input) | DISCOVERY ONLY |
| Agent pushbuttons | `L:S_OH_FIRE_ENG{1,2}_AGENT{1,2}`, `L:S_OH_FIRE_APU_AGENT` | counter (**`ENG2_AGENT2` unproven**; registered as a switch) | 4 of 5 | DISCOVERY ONLY | DISCOVERY ONLY |
| Agent lights | `L:I_OH_FIRE_ENG{1,2}_AGENT{1,2}_{L,U}` (8) | light 0/1 (the L/U = lower/upper split is inferred) | yes | EXPERIMENTAL (probe input) | DISCOVERY ONLY |
| MASTER WARNING | `L:S_MIP_MASTER_WARNING_CAPT/FO` (FO switch unproven), `L:I_MIP_MASTER_WARNING_CAPT/FO[_L]` | counter / light (blinks) | 37 of 39 overall | EXPERIMENTAL (lights are probe inputs) | DISCOVERY ONLY |

**Not read anywhere:** APU MASTER, APU START, APU BLEED pushbutton, ENG MASTER 1/2, APU and cargo fire lights,
cargo discharge, ADIRS ON BAT and IR FAULT lights, fuel pump FAULT/OFF lights. APU state therefore has **no Fenix
source today**. The stock `APU PCT RPM` and `APU GENERATOR ACTIVE` were proven wrong on Fenix. Only
`PNEUMATICS APU BLEED AIR` (stock) is read.

### 6.1 ENG FIRE TEST probe (`Core/Simulator/EngFireTestProbe.cs`, FSH only)

| Aspect | Fact |
|---|---|
| Trigger | `ENG1_FIRE_TEST_PRESSED` / `ENG2_FIRE_TEST_PRESSED`. The detector emits them once the counter has been stable for 6 s. There is no RELEASED event. |
| Window | From press start −0.5 s to +6 s, read from the monitor's 20 s edge history. |
| 8 checks per engine | ENG FIRE light, fire handle light, 4 agent light segments, CAPT and F/O MASTER WARNING light. |
| Rule | A signal passes if it turned ON at least once in the window. Blink count is ignored. A light already ON before the test is not counted. |
| Verdict | PASS, ABNORMAL (lists missing signals), or INCONCLUSIVE (no press time, or history possibly pruned). |
| Output | `FIRE-PROBE` log lines and a `fire_test_probe_result` JSONL trace. The `ProbeCompleted` event has **no production subscriber**. |
| Failure correlation | Deliberately not implemented. Live trials with `F_OH_FIRE_ENG1_LOOP_A` and `F_ENG1_EIU` produced no usable signature. The `F_FIRE_FDU1` experiment is paused (no injection done). |
| Status | **EXPERIMENTAL**, logs only. 13 tests (`EngFireTestProbeTests.cs`). |

---

## 7. Failures

### 7.1 Mechanism (identical code in FSH and FLP)

| Item | Value |
|---|---|
| Server | `Fenix.GqlGateway`, the Fenix EFB backend. It listens on IPv4 `0.0.0.0:8083` only while a Fenix aircraft is loaded. Unofficial, reverse-engineered from the EFB's JavaScript (`research/FENIX_FAILURE_PROTOCOL.md`). |
| Base URL | `http://127.0.0.1:8083`, hard-coded (the IPv4 literal avoids about 2 s lost on `::1`). |
| Trigger / clear | `POST /fenix/failures/saveManual`, body `{"id":"F_…","title":"<exact Fenix title>","failureCondition":null,"failed":true\|false}` |
| Readback | `GET /fenix/failures/manual` → `atas[].groups[].failures[]{id,title,failureCondition,failed}`. Used **only** when the POST echo disagrees: 3 attempts, 400 ms apart. Fixes the echo anomaly seen on `F_GEAR_TYRE_PSI_*`. |
| Title requirement | The payload needs Fenix's own title, so an id missing from the embedded catalog is **refused before any HTTP call**. |
| Catalog | Embedded JSON: **384 entries across 19 ATA chapters**. 382 ids match `F_*`; 2 are `B_INT_SFCDC{1,2}F`. The id set is identical to `research/fenix_failure_catalog.(json|csv)`. |
| Timeouts / retries | 3 s per call; no retry on the POST; results Success, Failed or Timeout (`ContractUnknown` is never returned by the real injector). |
| Probe | `GET /` with a 2 s timeout → Reachable / NotRunning / TimedOut / Failed. FSH: USED (Settings button). FLP: DEAD. |
| Conditional arming (`failureCondition`: IAS/alt/time) | Never used. Always null. |
| Tests | Injector plus catalog 13, probe 6 (FSH); equivalent in FLP. |

### 7.2 Failure paths

| App | Path | Source of the Fenix id | Trigger | Clear | Ack / readback | Status |
|---|---|---|---|---|---|---|
| FSH | Initial INOP | server `flight-sessions/prepare` → `initial_inop[].fenix_failure_id` | Immediate, sequential, before flight (`RunSessionCoordinator.cs:1087-1150`). A null id blocks readiness. | **none** | never acked to the server | PRODUCTION |
| FSH | Scheduled failure | server `scheduled_failures[]` (`trigger_after_seconds` ≤ 7200) | `FailureScenarioRunner`: one `Task.Delay` per failure | **none** | `POST failure-events/{id}/ack`, 4 attempts | PRODUCTION |
| FSH | Webapp command | `GET api/client/v1/commands` every 5 s → `INJECT_FAILURE{fenix_failure_id, trigger_after_seconds?, persistent}` | `FailureCommandPollingService` (capability-gated) | **none** | `POST commands/ack`, only on success | PRODUCTION |
| FSH | Manual diagnostic | Settings → "MANUAL FENIX FAILURE TEST" | Send | **Reset** (`failed:false`), the **only** clear path in FSH | UI text | USED |
| FLP | Challenge scenario | `Failures/FailureScenarioCatalog.cs`: **24 Fenix ids hard-coded** by difficulty tier | `FailureScenarioRunner.OnSample` when airborne elapsed time ≥ trigger | **`FinalizeAsync` resets every injected failure** | echo plus fallback | PRODUCTION |
| FLP | Demo | `Demo/DemoFailureInjector.cs` | no network | — | — | USED (demo) |

Further facts:

- FSH has **two independent injector instances**, one in `AppState` for commands and one in `RunSessionViewModel`
  for the coordinator and manual panel. There is no cross-path deduplication.
- No component in either app tracks the set of currently active failures.
- Whether Fenix keeps a manual failure across an aircraft reload is **UNKNOWN** (not documented).

### 7.3 Failure ids used individually

| Where | Ids |
|---|---|
| FLP scenario catalog, Easy | `F_ELEC_STATIC_INVERTER`, `F_FMGC_1`, `F_MCDU_1_RECOVERABLE`, `F_DISPLAY_DU_ECAM_LOWER`, `F_NAV_ADF1`, `F_ICE_AOA_STBY`, `F_FUEL_FQI2`, `F_NAV_GPS1` |
| FLP scenario catalog, Tricky | `F_HYD_PUMP_BLUE`, `F_ELEC_DRIVE_FAILURE_L`, `F_FIRE_LAVATORY_SMOKE`, `F_NAV_ILS1_LOC`, `F_PNEUMATIC_PACK_1_OVERHEAT`, `F_HYD_LOW_BLUE`, `F_BRAKE_WHEEL_1`, `F_ICE_PITOT_FO`, `F_ELEC_DRIVE_FAILURE_R`, `F_HYD_PUMP_YELLOW` |
| FLP scenario catalog, Reckless | `F_HYD_LEAK_BLUE`, `F_ENGINE_1_SURGE`, `F_OH_FIRE_ENG1_LOOP_A`, `F_GEAR_TYRE_PSI_MAIN_1`, `F_ELEC_BUS_AC_1`, `F_VIB_N1_ENG_1` |
| FSH live verification and tests | `F_PNEUMATIC_CPC_1`, `F_GEAR_TYRE_PSI_RIGHT_1`, `F_PNEUMATIC_BLEED_VALVE_1`, `F_ELEC_BUS_DC_1/2` |
| FSH fire-probe trials | `F_OH_FIRE_ENG1_LOOP_A`, `F_ENG1_EIU`; `F_FIRE_FDU1` (paused experiment) |
| FSH server-driven | any catalog id, chosen by fenixhangarweb |

### 7.4 Readiness of the FSGAP failure model

The BLOCK 0 `FailureType` enum has 8 types. The ids actually used span navigation, FMGC, MCDU, displays, ice
protection, pneumatics, brakes, tyres, vibration, lavatory smoke, electrical drives and hydraulic leaks. **The
normalized vocabulary is far too coarse for the observed need.** This is the largest gap of the audit; see the
mapping document §4, gap G-F1.

### 7.5 Principle 6 is violated today, beyond the clients

The Fenix failure id is **business data outside the clients**:

- fenixhangarweb stores `fenix_failure_id` values and sends them to the client (prepare, scenario and commands
  endpoints);
- FLIPPP hard-codes them in its scenario catalog.

Moving the injector into FSGAP.Fenix is therefore not enough. The ids crossing the server/client API must become
normalized FSGAP failure keys, with the Fenix mapping in FSGAP.Fenix. That touches fenixhangarweb, so it is a
**cross-repository decision for the user** (extraction plan, BLOCK 6).

---

## 8. Generic vs Fenix-specific

| Class | Items |
|---|---|
| **GENERIC** (stock MSFS / SimConnect) | Connection lifecycle; `Crashed` and `Pause_EX1`; FAST, SYSTEMS and ENVIRONMENT SimVars (flight state, engines N1/N2/EGT/oil/fuel flow/combustion/starter/throttle/reversers, gear, flaps handle and trailing-edge flaps, brakes, steering, antiskid, speed warnings, stall, pressurization, weather, body accelerations, surface deflections); `TITLE`, `ATC ID`, `LIVERY FOLDER`, `LIVERY NAME`; facility list and facility data; `FlightLoad`; MSFS install locator (`UserCfg.opt`). |
| **GENERIC SimVar with Fenix-specific semantics** | `HYDRAULIC PRESSURE:1/2` = green/blue (index 3 = yellow is broken); `HYDRAULIC RESERVOIR PERCENT:1/2`; `ELECTRICAL BATTERY VOLTAGE:1` = BAT1 (index 2 broken); `BRAKE LEFT/RIGHT POSITION` returns a 0–1 fraction. The index-to-circuit mapping belongs in FSGAP.Fenix, not in the generic layer. |
| **FENIX-SPECIFIC** | 39 cockpit LVARs; EFB HTTP failures and the 384-entry catalog; Fenix package and livery scanning; `TITLE` regex `Fenix\D{0,4}(319|320|321)`; preset and `.flt` template (WIP); the list of stock SimVars known to be wrong on Fenix. |
| **DERIVED in application** | Merge and gap-fill; flight phases (both apps, different rules); landing and touchdown analysis; crash threshold; engines-off gates; departure/arrival policy; route progress; speed-violation events; engine envelope; chart history; ETE. |
| **UNCERTAIN** | `Pause_EX1` bit semantics; sign of `PLANE TOUCHDOWN NORMAL VELOCITY`; what `LIVERY FOLDER` returns for non-Fenix aircraft; what the Fenix LVARs read on a non-Fenix aircraft (assumed 0); whether engine and APU fire lights may be used as "fire detected" (they also light during a test). |

---

## 9. Aircraft detection and identification

| Attribute | FSH rule | FLP rule | Data origin |
|---|---|---|---|
| Aircraft loaded? | `TITLE` not blank (`IRunSimulatorSource.cs:26`) | same | MSFS |
| Is it a Fenix? | **No explicit check in the flight path.** The cockpit monitor alone uses `Fenix\D{0,4}(319|320|321)` on `TITLE`, then `LIVERY FOLDER`. The top bar prints "Fenix {family}" for any A32x title. | `FenixCockpitAirframeMatcher` (same regex) drives the live identity | MSFS TITLE / LIVERY FOLDER |
| A319 / A320 / A321 (live) | `DetectFamily`: `Contains("A319")`, then `"A321"`, then `"A320"` (`RunSessionCoordinator.cs:1752-1758`). It must equal the server fleet family. | regex above | MSFS TITLE (real value observed: `FenixA320 CFM WF`) |
| A319 / A320 / A321 (installed liveries) | `AirframeDetector` regex `(?<![A-Za-z0-9])(?:A|FNX)?\s?-?(319|320|321)(?![A-Za-z0-9])` over, in priority order: `livery.cfg [SELECTION] required_tags`, metadata JSON, `base_container`, `aircraft.cfg` title, package, path, display name | same code | Filesystem. This regex **fails on "FenixA320"**, which is why the live check uses its own regex. |
| Engine CFM / IAE | Never read live. It comes from the server fleet record or the livery scan (`required_tags` exact tag, then free text) | Live: `TITLE` tokens. Scan: same as FSH. | Server / filesystem / TITLE |
| Wingtip (sharklets / fence) | Scan only (`SL`/`SHARKLETS`, `WF`/`WTF`/`WINGTIP_FENCE`) | TITLE plus scan | Filesystem / TITLE |
| Registration | **Reconstructed**: `LIVERY FOLDER` → livery catalog `FindByLiveryFolderName` → record registration; fallback `ATC ID`, which is **empty on Fenix**; NOK if the folder is not in the catalog | same | MSFS LIVERY FOLDER + filesystem (`livery.cfg atc_id` → `aircraft.cfg atc_id` → metadata JSON → folder-name regex) |
| Livery | `LIVERY FOLDER` (raw); `LIVERY NAME` read but unused | same | MSFS |
| Airline ICAO | Scan only (`icao_airline`, exactly 3 letters) | same | Filesystem |
| ICAO type designator | **Not read.** `ATC MODEL`, `ATC TYPE` and ICAO SimVars are read nowhere. | same | — |
| Package | `UserCfg.opt` → roots → folder name or `manifest.json` containing `fenix`/`fnx` | same | Filesystem |

### 9.1 Comparison with the BLOCK 0 placeholder (`FSGAP.Fenix/Detection/FenixAircraftDetector.cs`)

| Aspect | Placeholder | Reality in the apps | Verdict |
|---|---|---|---|
| Fenix marker | "fenix" substring in Title or PackagePath | regex `Fenix\D{0,4}(319|320|321)` on TITLE, then LIVERY FOLDER | Real rule is stricter (marker and model together) and **proven live**. Adopt it. |
| Model | IcaoType, then `A(319|320|321)(?!\d)` in Title, then Model | model is inside the same regex; no ICAO type is read | IcaoType is never available from the apps. Drop the dependency on it. |
| Registration | from descriptor | reconstructed from LIVERY FOLDER plus the livery catalog | Needs `AircraftDescriptor.LiveryFolder` and a Fenix livery catalog (gaps G-I1 and G-I3) |
| Engine / wingtip | not detected | TITLE tokens (FLP) or livery `required_tags` | Adopt the `required_tags` > TITLE order |

**Most reliable rule found:**

1. Family/model: the TITLE regex (live-verified).
2. Engine and wingtip: livery `required_tags` exact tags, with TITLE tokens as the live fallback.
3. Registration: the `LIVERY FOLDER` → catalog lookup (`ATC ID` is known to be empty).

---

## 10. Duplications between FSHANGAR and FLIPPP

FLIPPP copied the FSHANGAR bricks (snapshot `5b4a8f8`) and renamed the namespaces. Comparison ignores whitespace,
`using` lines and namespaces.

| Kind | Files (FLP path ↔ FSH path) | Verdict |
|---|---|---|
| **Exact duplicate** (21 files) | `Fenix/Cockpit/CockpitActionEvent`, `FenixCockpitActionDetector`, `Fenix/Failures/FenixFailureInjector`, `FenixLocalApiProbe`, `IFenixFailureInjector`, `Fenix/ILiveryRegistrationResolver`, `Fenix/Liveries/AirframeDetector`, `EngineWingtipDetector`, `FenixPackageLocator`, `ILocalThumbnailResolver`, `IniConfigFile`, `LiverySelection`, `RegistrationExtractor`, `Flight/CrashDetector`, `Flight/GreatCircle`, `Sim/IAirportLocator`, `ISimulatorDataSource`, `SimConnectAirportLocator`, `SimConnectSampleTracer`, `SimConnectTelemetrySample`, `SimulatorConnectionState` | Move to FSGAP (except CrashDetector and GreatCircle, which are app logic) |
| **Near-exact** (renames only) | `FenixCockpitControlRegistry`, `FenixCockpitSnapshot`, `FenixCockpitAirframeMatcher`, `IRunSimulatorSource`, `Msfs2024InstallationLocator`, `FenixFailureCatalog` (resource name) | Move to FSGAP |
| **Diverged, FLP improved** | `SimConnectVariableRegistry` + `LiveTelemetryMapper` (speed warnings at 1 Hz); `SimConnectDataSource` (app name, logger, diagnostics gate); `FenixLiveryScanner` (progress reporting, two-phase enumeration, prunes `attachments`/`presets`); `LiveryCacheDatabase`, `FenixLiveryModels`, `LiveryScanLogger`; `FenixCockpitMonitor` (re-arm on Fenix-to-Fenix change, **WIP**) | Merge both improvements into the FSGAP version |
| **Conceptual duplicate, different implementation** | Flight phase: `FlightTrackingPhase` (FSH, ±300 fpm, ≤ 2000 ft) vs `FlightMotionPhaseResolver` + `FlightSession` (FLP, ±500 fpm, 1500/3000 ft). Landing: coordinator 5 s vs `FlightSession` 4 s. Failure runner: server-driven (FSH) vs locally generated (FLP). Loggers: `RunLogger` vs `FlipppLogger`. | **Stay app-specific** |
| **Should move to FSGAP** | Connection lifecycle and retry, identity polling, SimVar registry and units, LVAR polling, livery catalog and registration resolution, Fenix airframe matcher, EFB injector and catalog, MSFS install locator, facility lookup | — |

---

## 11. Connection lifecycle

| Situation | FSH | FLP | Generic → FSGAP.Core / transport? |
|---|---|---|---|
| Connect | Only **after** server bootstrap and recovery succeed (`RunSessionCoordinator.cs:440-454`). An unpaired device or a server error means no SimConnect. | On window `Loaded` (Real mode) | Generic connect: FSGAP. The "after bootstrap" gating is app policy. |
| MSFS not running | `SimConnectException` → `WaitingForMsfs`, retry every 5 s forever. The failed client is not disposed on this path. | same (5 s) | FSGAP (fix the leak) |
| MSFS starts after the client | Next retry connects; subscriptions and polls start | same | FSGAP |
| MSFS closes / drops | Library `ConnectionStatusChanged(false)` → cancel → retry | same | FSGAP |
| Drop during a flight | 3-minute reconnect window, then a pilot decision; the elapsed clock stays monotonic | no window; the clock survives reconnects; the WIP launch resets it | Window and decision: app. Monotonic clock: FSGAP. |
| Aircraft change | Identity polled every 2 s. **After verification the registration is never recomputed.** | Key `Title|LiveryFolder|AtcId` change → re-identify; mismatch while airborne aborts the session. WIP re-arms the cockpit monitor. | "Aircraft changed" event → FSGAP. Reaction → app. |
| Flight reload / back to menu | No `SimStart`/`SimStop`/`FlightLoaded`/`AircraftLoaded` subscribed | `FlightLoaded` only inside the WIP loader | FSGAP should expose sim-state events |
| Pause | `Pause_EX1` gates recording | subscribed, unused | FSGAP event, app policy |
| Crash | `Crashed` → CrashDetector | subscribed, unused | FSGAP event |
| Stale telemetry | **No watchdog.** A silent connection leaves the flow awaiting indefinitely. | **No watchdog.** Merged values never expire. | FSGAP: staleness → `Unknown` (gap G-T12) |
| SimConnect exceptions | Connect-level → state, then retry; per-tick errors are logged and the tick is dropped. **Consumer exceptions thrown from sample handlers are logged as "SimConnect error" and skip later subscribers.** | same | FSGAP must isolate consumer exceptions |

---

## 12. Performance (behaviour to preserve)

| Stream | Cadence | Batch | Notes |
|---|---|---|---|
| FAST | native `PERIOD_SECOND` | 1 struct (FSH 19, FLP 22) | FLP needs the speed warnings at 1 Hz |
| SYSTEMS | 5 s poll | 1 struct (FSH 41, FLP 38) | — |
| ENVIRONMENT | 10 s poll | 1 struct (7) | Route progress in FLP moves at most every 10 s |
| Identity | 2 s poll, forever | 1 struct (4 strings) | Could become event-driven plus a slow poll |
| Fenix LVARs | **10 Hz** poll | 1 struct (39) | Runs for non-Fenix aircraft and on FSH's diagnostic connection. FLP pays 10 UI dispatches per second for an inert result. |
| Facility lookup | per call, on a new connection | — | FSH: departure (re-run after a move of ≥ 0.005°) and arrival |
| Fenix HTTP | per command | — | 3 s timeout |

No place reads dozens of variables one by one: everything is already batched. What an extraction can improve:

- one shared connection instead of up to three (FSH);
- LVAR polling only while a Fenix is attached;
- identity polling reduced after verification.

Whether SimConnect.NET re-registers the data definition on every `GetAsync<T>` is UNKNOWN.

Per-sample work on the SimConnect callback thread in FSH includes a reflection-based JSONL trace (on by default), a
new SQLite connection with DDL and an upsert, and an outbox transaction with three JSON round-trips. That work must
not remain on the SDK's dispatch thread.

---

## 13. Threading, and defects found in passing

Threading model today (both apps):

- The FAST callback runs on SimConnect.NET's dispatch thread.
- The other groups run on thread-pool `PeriodicTimer` continuations, so handlers can run **concurrently**.
- Cancellation uses a lifetime CTS plus a linked per-connection CTS.
- Events are raised on background threads.
- FSH subscribers use synchronous `Dispatcher.Invoke`, which blocks the SimConnect thread until the UI has
  rendered.
- FLP marshals with `BeginInvoke`.
- Only the loggers, `TelemetryCollector._sampleCount`, the upload recorder and the diagnostic exporter are
  synchronized.

**None of these defects was fixed here (read-only).** They are recorded so that the extraction does not carry them
over.

| # | Defect | Where | Severity |
|---|---|---|---|
| D1 | **Shared sample mutated in place.** `RunSessionViewModel.OnLiveSample` calls `MergeWithPrevious(LastLiveSample, sample)`, which writes into `sample`. The handler is subscribed before the coordinator, so the coordinator, crash detector, SQLite buffer and outbox receive back-filled values. This contradicts `TelemetryOutboxStore.cs:135` ("Never mutate the caller's own sample object"). **Verified in code.** Impact on uploaded data: UNKNOWN. | FSH `Wpf/RunSessionViewModel.cs:180,339`; `Core/SimConnectTelemetrySample.cs:181-190` | High (data integrity) |
| D2 | `FenixCockpitMonitor` has no locking. The identity thread calls `Reset()` while the sample thread iterates the same lists and dictionaries. | FSH `Simulator/FenixCockpitMonitor.cs`; FLP `MainWindow.xaml.cs:336` vs `:346` | Medium (crash on aircraft change) |
| D3 | Coordinator sample fields and `TaskCompletionSource`s (no `RunContinuationsAsynchronously`) are touched from several threads. Flow continuations can run inline on the SimConnect thread. | FSH `RunSessionCoordinator.cs` | Medium |
| D4 | Two `FenixFailureInjector` instances with no dedup; the `AppState` instance is never disposed | FSH `AppState.cs:49`, `RunSessionViewModel.cs:207` | Low |
| D5 | FSH never clears injected Fenix failures (only the diagnostic Reset does) | FSH | Medium (a failure outlives its flight) |
| D6 | The registration is never recomputed after an aircraft change following verification | FSH `RunSessionCoordinator.cs:759,1734-1738` | Medium |
| D7 | Identity-change dedup plus `RequiredConsecutiveIdentityMatches = 2` may never be satisfied. DARE primes the session twice in a row. | FLP `MainWindow.xaml.cs:334-342,803-804` | Medium (UNCERTAIN, needs a test) |
| D8 | The failed `SimConnectClient` is not disposed in the retry loop while MSFS is down | both, `SimConnectDataSource.cs:158-168` | Low |
| D9 | The heartbeat's `msfs_connected` comes from a manual "legacy" checkbox, not from SimConnect | FSH `AppState.cs:163-164,732` | Low |
| D10 | The WIP launch does not release the flight loader, parking CTS or launch timer on window close | FLP (WIP) | Low (still WIP) |

---

## 14. Legacy and dead paths — DO NOT MIGRATE

| Item | Where | Reason |
|---|---|---|
| **STKP (SimToolkitPro) tracking** | **fenixhangarweb only** (not in either client): `src/lib/fh/tracking/{provider,merger,cloud,parser,simulator,service}.ts`, `stkp.functions.ts`, `stkp.server.ts`, `components/fh/stkp-api-bits.tsx`, `tracking-bits.tsx`, the `/flight-tracking-stkp` superadmin route, the `STKP` alias in `flight-source.tsx`. It reads the SimToolkitPro local streaming overlay from the browser (1 Hz) and the STKP cloud API (`https://api.simtoolkitpro.co.uk/api/v1/live-flight`, about 0.1 Hz). | Deprecated third-party tracker, superseded by native SimConnect telemetry. **DO NOT MIGRATE and do not reintroduce into FSGAP.** Its removal is a fenixhangarweb task, outside FSGAP. |
| `origin/FenixHangarClientPC` branch | fenixhangarclient | GPLv3 Flight-Recorder fork, rejected (licence) |
| `FenixHangarWebConnect` | local folder, unversioned | Early prototype (P/Invoke SimConnect, MobiFlight), never shipped |
| FLIPPP's vendored `FenixHangar.Client.*` tree | FLIPPP | Stale fork at `5b4a8f8`. Never extract from it; the source of truth is fenixhangarclient. |
| `CsvReplayDataSource`, `SimulatorSourceMode.Replay` | FSH (and the FLP enum) | Dead |
| AppState diagnostic `SimConnectDataSource` | FSH | Duplicate connection; replaced by FSGAP connection state |
| Manual heartbeat inputs | FSH | Legacy; replace with real FSGAP connection state (app task) |
| `LIVERY NAME` poll | both | Read, never used |
| 42 never-subscribed registry rows | both | Keep as **documentation** of what is known to be wrong or missing on Fenix. Do not create fields for them. |
| About 45 SimVars read but unused in FLP | FLP | FSGAP may expose them; FLP simply stops reading them |
| `LocalThumbnailResolver`, `ExportCatalogToFile`, `SetManualPackageRoot` (writer), FLP `CrashDetector`/`FindNearestIcao`/`FenixLocalApiProbe`, FLP `Crashed`/`Pause` subscriptions without subscriber | both | Dead in production |
| Stale comments and docs | FSH `SettingsView.xaml:331`, `docs/FENIX_FAILURE_INJECTION.md`, `FenixHangarFailureScenario.cs:37-41,539`, `SimConnectVariableRegistry.cs:6,119-122,342-375,406`, `FenixLiveryScanner.cs:14-16`, `docs/FENIX_LIVERY_SCANNER.md`, `FenixCockpitMonitor.cs:15`; FLP doc comments naming FSHANGAR types | Rewrite on extraction; do not copy |

Searches for `[Obsolete]` and "deprecated" returned nothing in either client. "legacy" only appears in unrelated UI,
URL and comment text.

---

## 15. Open questions (to settle before or during extraction)

1. Impact of defect D1 on uploaded telemetry.
2. SimConnect.NET 0.2.2 internals:
   - continuation thread of `GetAsync`;
   - data-definition caching;
   - whether `ConnectionStatusChanged` fires on MSFS quit;
   - the default of `AutoReconnectEnabled`;
   - the internal `GetAsync` timeout.
3. The reflection dependency on `SimConnectNative` and the reconstructed facility layouts. Should they be pinned,
   upstreamed, or replaced by a direct P/Invoke in the FSGAP transport?
4. `Pause_EX1` bit semantics; the sign of `PLANE TOUCHDOWN NORMAL VELOCITY`; `LIVERY FOLDER` on non-Fenix aircraft.
5. Persistence of Fenix manual failures across an aircraft reload.
6. Whether engine and APU fire *lights* can ever feed `FireDetected` (they also light during a test). Until this is
   proven, `FireDetected` stays Unavailable for Fenix.
7. How fenixhangarweb will migrate from `fenix_failure_id` to normalized failure keys (§7.5).
8. How the Fenix launch feature (presets, `.flt` template, parking, `FlightLoad`) settles once FLIPPP's WIP is
   committed.
