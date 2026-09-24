# Fenix / MSFS extraction plan

BLOCK 1 of FSGAP_SDK, 2026-09-23. This is the recommended migration sequence, derived from
[fenix-existing-code-audit.md](fenix-existing-code-audit.md) and [fenix-fsgap-mapping.md](fenix-fsgap-mapping.md).
Nothing in it has been started.

## Guiding rules

1. **Source of truth for extraction: `fenixhangarclient` (FSHANGAR).** Where FLIPPP's fork improved a brick
   (speed warnings at 1 Hz, livery scan progress, Fenix-to-Fenix monitor re-arm), merge that improvement in. Never
   extract from FLIPPP's vendored `FenixHangar.Client.*` tree.
2. **The apps stay untouched until BLOCK 9.** BLOCKs 2–8 only add code to fsgap_sdk. Each of them is reverted with
   a plain `git revert` in fsgap_sdk and has no effect on the apps.
3. **Parity before switch-over.** Every migrated reading keeps its SimVar name, unit, conversion and cadence, unless
   a block explicitly decides otherwise. Mapper-level golden tests prove the parity: the same raw struct values
   must produce the same normalized values as the legacy `LiveTelemetryMapper`.
4. **Do not carry the defects over** (audit §13). Snapshots are immutable (fixes D1). The monitor and the
   SimConnect transport are thread-safe (D2, D3). Consumer exceptions are isolated. Failed clients are disposed
   (D8).
5. **No speculative features.** APU/ADIRS alignment/pump fault LVAR discovery, crew-action events (G-C2), bus
   telemetry, conditional failure arming and PMDG are out of scope until a consumer needs them.
6. **Live validation** requires the MSFS 2024 + Fenix test PC. Each block that touches the simulator ends with a
   manual live checklist. Automated tests never require MSFS.

## Decisions (validated at the start of BLOCK 2)

| # | Decision | Record |
|---|---|---|
| DEC-1 | Failures are identified by an open, normalized `FailureKey` and published per provider in a `FailureCatalog`. There is no giant enum, and no Fenix id in the public API. The `FailureType` enum is removed. | [ADR 0001](../decisions/0001-failure-key-catalog.md), implemented in BLOCK 2 |
| DEC-2 | Target: servers and applications exchange `failure_key`, never a Fenix id. The temporary legacy-id translation lives outside the FSGAP public API (fenixhangarweb or an app adapter). | [ADR 0002](../decisions/0002-server-failure-key.md), migration in BLOCK 9 |
| DEC-3 | Distribution as versioned NuGet packages with pinned versions. GitHub Packages is the planned feed. | [ADR 0003](../decisions/0003-nuget-distribution.md), packaging ready since BLOCK 2 |
| DEC-4 | Reflection into SimConnect.NET internals is tolerated temporarily, only inside one internal class of `FSGAP.SimConnect`, pinned and self-checked. It never appears in public contracts or app code. | [ADR 0004](../decisions/0004-simconnect-layer-and-reflection.md) |
| DEC-5 | **Position at 1 Hz by default** (not 10 s). FSGAP provides fresh data; consumers downsample. This supersedes the "keep 10 s" recommendation of BLOCK 1. | [ADR 0005](../decisions/0005-position-update-rate.md), implemented in BLOCKs 3/5 |

## Block sequence

```text
BLOCK 2  Contract extensions (P1) + simulator abstractions      fsgap_sdk only
BLOCK 3  SimConnect transport & connection lifecycle            fsgap_sdk only
BLOCK 4  Aircraft detection, identity & Fenix installed catalog  fsgap_sdk only
BLOCK 5  Generic flight telemetry provider                       fsgap_sdk only
BLOCK 6  Fenix systems telemetry (LVARs)                         fsgap_sdk only
BLOCK 7  Fenix failure provider                                  fsgap_sdk only (after DEC-1/DEC-2)
BLOCK 8  Simulator services (nearest airport; later parking/FlightLoad)   fsgap_sdk only
BLOCK 9  FSHANGAR migration to FSGAP                             fenixhangarclient (+ fenixhangarweb for DEC-2)
BLOCK 10 FLIPPP migration to FSGAP                               FLIPPP (after its launch WIP is committed)
BLOCK 11 Remove duplicated legacy integrations                   both apps (+ STKP removal in fenixhangarweb, separate)
```

The suggested outline has been adjusted in three ways:

- Contract work comes first (BLOCK 2), because P1 gaps block the transport.
- Simulator services become their own block (BLOCK 8), since they are not aircraft data.
- The block numbers shift by one.

---

## BLOCK 2 — Contract extensions (P1) and simulator abstractions — DONE (version 0.2.0)

What was delivered:

- **Failures:** `FailureKey`, `FailureDefinition`, `FailureCatalog`, `FailureCategory`, `FailureOperations` and
  key-based `FailureCapabilities`; `FailureType` removed.
- **Simulator contracts:** `ISimulatorConnection` (states, session clock), `ISimulatorStateProvider` (pause,
  crashes) and `IAircraftDetector`.
- **Aircraft:** `IInstalledAircraftCatalog` and its models; `AircraftDescriptor.LiveryFolder`.
- **Telemetry:** height above ground, touchdown vertical speed, the `Warnings` section, `LandingGear` (handle and
  units), and flap handle vs `FlapSurfaces`.
- **Freshness:** `ObservedAt` on every value, expiry to `Unknown`, and `TelemetryFreshness` for snapshots.
- **Immutability:** every public collection is copied on assignment.
- **Options:** `FsgapOptions`.
- **Core:** `ObservableState<T>` and the wait helpers.
- **Docs:** ADRs 0001–0008.
- **Packaging:** version 0.2.0 and packing to `artifacts/packages/`.

Original plan:

- **Goal:** extend the BLOCK 0 contracts with what the audit proved necessary, so that the implementation blocks
  have a stable target.
- **Scope:**
  - Apply these gaps: G-I1 `LiveryFolder`; G-T1 AGL; G-T2 touchdown VS; G-T3 warnings section; G-T7 gear section;
    the flaps part of G-T8; G-T12 staleness rules (documented); G-X1 `FsgapOptions`.
  - Add the `ISimulatorConnection` state and event contract (G-S1) and the `IAircraftDetector` contract (G-S2).
  - Apply DEC-1 to the failure contract (catalog shape only, no implementation).
  - Update `docs/architecture.md`.
  - P2 and P3 extensions are deferred unless DEC decisions pull them in.
- **Files and components:** `src/FSGAP.Abstractions/**`, `docs/architecture.md`, `tests/FSGAP.Abstractions.Tests`.
- **Dependencies:** DEC-1.
- **Tests required:**
  - Every new value defaults to `Unavailable`.
  - Collections of gear legs.
  - Warning semantics.
  - Failure-catalog key validation.
  - The dependency rule test (Abstractions → BCL only) still passes.
- **Risk:** low (additive, no implementation). The main risk is over-design, mitigated by adding only the P1
  rows.
- **Rollback:** `git revert` in fsgap_sdk.
- **Definition of done:** P1 gaps are representable. 0 warnings. All BLOCK 0 tests still pass. The mapping
  document is updated with the new targets.

## BLOCK 3 — SimConnect transport and connection lifecycle — DONE (version 0.3.0)

**Delivered:**

- `FSGAP.SimConnect` with a single public type, `SimConnectSimulator`. It implements `ISimulatorConnection`,
  `ISimulatorStateProvider` (`.State`) and `IAircraftDetector` (`.AircraftDetector`).
- An internal native seam (`ISimConnectSession`), so the lifecycle is tested without MSFS.
- The live sample `samples/FSGAP.SimConnect.Console`.
- Documentation in [../simconnect-lifecycle.md](../simconnect-lifecycle.md).
- No reflection, no telemetry, nothing Fenix-specific.

**Live validation**

Performed on 2026-09-23, with MSFS 2024 already running and a Fenix A321 loaded:

| Scenario | Result |
|---|---|
| A (connect to a running MSFS, see the aircraft) | **LIVE: PASS.** `Connecting → Connected` in about 30 ms. Descriptor: title `FenixA321 IAE WF SC`, ATC ID `SX-DNH`, livery folder `AEE-SX-DNH-7F2F`, livery `Aegean 'Early Modern' SX-DNH (2025)`. Clean stop. |
| A+ (3 minutes connected) | **LIVE: PASS.** Stable connection. No spurious notification or log line (identity polled about 30 times, a single "Loaded aircraft" line). Clean stop after 2 min 59 s. |
| B, C (MSFS start/close) | NOT TESTED live: it would have required closing or relaunching the user's running MSFS session. Covered by automated tests with the fake native layer. |
| D (pause), E (crash) | NOT TESTED live: they need in-sim user actions. Covered by automated tests. |
| F (10 minutes waiting without MSFS) | NOT TESTED live: MSFS was running. Covered automatically: 100 retries, at most 2 informative log lines, one connection alive at most. |

**Findings:**

- **SimConnect.NET needs access to request structs.** SimConnect.NET 0.2.2 reads request structs through `dynamic`,
  and fails on `internal` structs (*"'object' does not contain a definition for 'OffsetBytes'"*).
  - It was found live; the unit tests could not see it.
  - Fix: `InternalsVisibleTo("SimConnect.NET")`, guarded by a test.
  - FSHANGAR/FLIPPP avoid the problem because their structs are public.
- **The Fenix `ATC ID` was not empty.** With this Fenix A321 livery, `ATC ID` returned `SX-DNH`, although the
  BLOCK 1 audit recorded it as empty for Fenix. It may depend on the livery or the airframe. BLOCK 4 must keep
  `LIVERY FOLDER` as the primary key and treat `ATC ID` as optional.
- **Title format.** A live `TITLE` of `FenixA321 IAE WF SC` confirms the `Fenix<model> <engine> <wingtip> <cabin>`
  format seen in FLIPPP's preset work. It matches the BLOCK 1 regex `Fenix\D{0,4}(319|320|321)`.

Original plan:

- **Goal:** one reusable, thread-safe SimConnect connection. It replaces `SimConnectDataSource` in both apps, but
  is not wired to them yet.
- **Contracts to implement (fixed in BLOCK 2):** `ISimulatorConnection`, `ISimulatorStateProvider` and
  `IAircraftDetector`, configured by `FsgapOptions`. `ObservableState<T>` backs the watch streams.
- **Position cadence:** position joins the 1 Hz group (ADR 0005). Speed warnings are sampled at 1 Hz.
- **Freshness:** every reading records its observation time, and snapshots go through
  `TelemetryFreshness.ExpireStaleValues`.
- **Reflection:** follow ADR 0004. Any reflection into SimConnect.NET goes into one internal, self-checked
  class.
- **Scope:**
  - New assembly `FSGAP.SimConnect`, which depends on Abstractions, Core and `SimConnect.NET 0.2.2`.
  - Connection with a 5 s retry and a configurable client name; states and `Paused`/`Crashed` events.
  - A monotonic elapsed clock across reconnects.
  - Batched data-definition groups (struct-per-group, as today) with configurable cadences.
  - Consumer-exception isolation; disposal of failed clients.
  - A single dispatch model: events raised off the SimConnect thread and never block it.
  - The SimVar registry, including its live-test notes, moves here as internal metadata.
  - Every reflection call is isolated in one class (DEC-4).
- **Files and components (source):** FSH `Simulator/SimConnectDataSource.cs`, `SimConnectVariableRegistry.cs`
  (generic part), `SimulatorConnectionState.cs`, `SimConnectSampleTracer.cs`; FLP `Sim/SimConnectDataSource.cs`
  (logger gating).
- **Dependencies:** BLOCK 2.
- **Tests required:**
  - The connect-without-MSFS settle test (ported from `SimulatorDataSourceTests`).
  - Retry and state transitions with a fake native layer.
  - The clock across reconnects.
  - A throwing consumer does not break the loop or later subscribers.
  - No UI or thread-affinity assumptions.
  - Registry ↔ struct consistency (ported `SimConnectMappingTests`).
- **Risk:** medium. Threading and library behaviour are uncertain (audit §15 Q2); resolve the questions with
  targeted spikes against the real library.
- **Rollback:** revert the assembly; the apps are unaffected.
- **Definition of done:**
  - Builds and tests pass without MSFS.
  - Live checklist: connect before and after MSFS start, MSFS quit, pause, crash, with no leak over 10 minutes of
    MSFS down.

## BLOCK 4 — Aircraft detection, identity and Fenix installed-aircraft catalog — DONE (version 0.4.0)

**Delivered:**

- Real Fenix recognition (`Fenix\D{0,4}(319|320|321)(?!\d)` on TITLE, then LIVERY FOLDER), with `Dedicated`
  specificity. The BLOCK 0 placeholder is removed.
- Normalized identity, with the new open fields `AircraftIdentity.WingtipConfiguration` and `OperatorIcao`.
- `FenixInstalledAircraftCatalog`:
  - MSFS 2024 discovery via `UserCfg.opt`, then Fenix package discovery;
  - read-only livery scan, pruning `presets`/`attachments` and skipping `Disabled` liveries;
  - immutable indexed snapshot swapped atomically;
  - JSON cache in `DataDirectory/fenix/`.
- Registration resolution: LIVERY FOLDER → installed livery, then ATC ID, otherwise unknown.
- The live sample shows the whole chain.
- Details: [../fenix-identity-and-catalog.md](../fenix-identity-and-catalog.md).

**Live and semi-live validation (2026-09-24):**

| Aircraft | Kind | Result |
|---|---|---|
| A319 | **LIVE** (MSFS + sample) | PASS. `FenixA319 CFM WF SD`, ATC ID `C-GBIA`, folder `ACA-C-GBIA-E270` → A319 / CFM / WingtipFence / C-GBIA / ACA. Catalog match `fnx-aircraft-319-liveries/aca-c-gbia-e270`. |
| A320 | Real catalog + descriptor recorded live in BLOCK 1 (empty ATC ID) | PASS. Registration C-FDRP resolved through the catalog only. |
| A321 | Real catalog + descriptor recorded live in BLOCK 3 | PASS. A321 / IAE / WingtipFence / SX-DNH / AEE. |
| Real catalog scan | LIVE (filesystem) | 129 liveries in about 4 s on a cold disk; 4 folders skipped (3 `Disabled` placeholders, 1 non-livery `aircraft.cfg`); 1 error (a broken third-party symlink) |

**Findings:**

- **Case of livery folders.** MSFS reports folders in upper case (`AEE-SX-DNH-7F2F`), while the disk has
  `aee-sx-dnh-7f2f`, so the lookup must ignore case.
- **Disabled placeholder liveries.** Fenix ships three `FNX_3xx_Fenix` placeholder liveries with
  `required_tags = "Disabled"`. They are not selectable.
- **Duplicate registrations.** Five registrations appear on two liveries each (for example SX-DNG). No duplicate
  livery folder was found.
- **False-positive package name.** A third-party FS2Crew package, `q-dsn-aicopilot-fnx-A320`, is a symbolic link to
  a missing folder. It matches the "fnx" marker and is reported as unreadable, without blocking the scan.
- **Dropped heuristics.** The audited scanner's folder-name registration heuristic and its package- or path-based
  model heuristic are deliberately **not** ported (they would invent data; `fnx-aircraft-319-321` would give a
  wrong model). Liveries without a declared registration now have none in FSGAP, whereas FSHANGAR's hangar discovery
  used a heuristic one. This is a behaviour change to handle when FSHANGAR migrates.

> **Starting point after BLOCK 3.** Generic detection is done: `SimConnectSimulator.AircraftDetector` publishes
> `AircraftDescriptor` (Title, Registration = ATC ID, LiveryFolder, Livery). BLOCK 4 therefore only covers:
>
> - the Fenix interpretation (`FenixAircraftProvider.Match`);
> - the Fenix installed catalog and registration resolution, with `LIVERY FOLDER` first and `ATC ID` as the
>   optional fallback (seen non-empty live).

- **Goal:** turn the BLOCK 0 placeholder into the real, proven identification.
- **Scope:**
  - `IAircraftDetector` over SimConnect (TITLE, ATC ID, LIVERY FOLDER, LIVERY NAME; change events; a slower poll
    after the aircraft is stable).
  - `FenixAircraftProvider.Match` with the live-proven regex `Fenix\D{0,4}(319|320|321)` (TITLE, then LIVERY
    FOLDER).
  - Engine and wingtip from TITLE tokens, then catalog `required_tags`.
  - Registration through LIVERY FOLDER → catalog, with `ATC ID` as fallback.
  - `IInstalledAircraftCatalog` (G-I3), with the MSFS install locator, Fenix package locator, livery scanner (FLP's
    progress-reporting variant), SQLite cache and registration extractor. Its data directory comes from
    `FsgapOptions`.
  - Adds G-I2 (operator ICAO and wingtip) if DEC confirms it.
- **Files and components (source):** FSH `Liveries/*`, `Run/ILiveryRegistrationResolver.cs`,
  `Simulator/FenixCockpitAirframeMatcher.cs`, `Liveries/EngineWingtipDetector.cs`; FLP `Fenix/Liveries/*` (diff
  merge), `Fenix/LoadedAircraftIdentifier.cs`.
- **Dependencies:** BLOCK 2, and BLOCK 3 for the detector.
- **Tests required:**
  - Port `FenixLiveryScannerTests` (28), `LiverySelectionTests`, `AirlineIcaoNormalizerTests` and the matcher
    tests.
  - Regression cases: `FenixA320 CFM WF` is supported, a FlyByWire A320 title is not, an empty ATC ID resolves
    through the livery folder, an unknown folder yields no registration.
  - Replace the BLOCK 0 placeholder tests with the real rule. Keep the negative cases (PMDG, C172).
- **Risk:** medium. There are two airframe regexes by design (live vs scan); name them clearly. Cache files move
  from the app folder to the FSGAP data directory, so the first run triggers a re-scan (acceptable) or a one-time
  copy.
- **Rollback:** revert. The apps keep their own copies until BLOCK 9/10.
- **Definition of done:**
  - Identity parity with the FSH coordinator on the same inputs (golden tests).
  - Live checklist: A319, A320 and A321 recognized, with correct registration on a community livery.

## BLOCK 5 — Generic flight telemetry provider — DONE (version 0.5.0)

**Delivered** (details in [../generic-telemetry.md](../generic-telemetry.md)):

- `SimConnectSimulator.Telemetry` (`ITelemetryProvider`):
  - 36 stock SimVars in three batched groups on the transport's **single** native connection: FAST 1 s, NORMAL
    2 s, SLOW 5 s;
  - one immutable merged snapshot, Core freshness, and streams that honour `TelemetryStreamOptions.Interval`;
  - reset on aircraft change and on stop, with generation-guarded reads;
  - failing groups are isolated;
  - values expire to Unknown after a disconnection.
- `TransformedTelemetryProvider` (Core): an aircraft integration composes on the generic telemetry without the
  transport knowing it.
- `FenixGenericTelemetryPolicy` (FSGAP.Fenix, internal): masks the speed brake (wrong scale on Fenix).
  `FenixAircraftProvider(genericTelemetry:)` exposes the masked telemetry and declares the five generic sections.
- Tests:
  - conversions, the mapper, the source (freshness, streams, reset), the transport (single session, isolation,
    aircraft change, disconnection);
  - golden parity fixtures (10 situations);
  - the Fenix policy;
  - architecture (single connection).

**Deviations from the plan below, and why:**

- **No separate `FSGAP.GenericSimConnect` provider assembly.** The BLOCK 5 specification puts the generic telemetry
  in `FSGAP.SimConnect` on the existing connection (one native session). A separate provider would need its own
  connection or a shared one exposed publicly.
- **Scope limited to the P1 contract sections.** Not ported, and still in the contract backlog:
  - the audited SYSTEMS and ENVIRONMENT extras: G-T4 (AoA, weight), G-T5 (body accelerations), G-T6 (oil,
    starter, throttle, reverser);
  - brakes, steering and surface deflections (G-T8 extras);
  - APU bleed;
  - generic hydraulics and electrical.

  Brake fraction ×100 therefore has no field yet.
- **Fenix overrides:** only the speed brake needed masking. The other "confirmed wrong on Fenix" SimVars (APU
  RPM/GEN, yellow hydraulics, BAT2, slats, engine anti-ice) are not read generically, so their sections stay
  Unavailable. The Fenix index mappings (green/blue hydraulics, BAT1) belong to BLOCK 6.
- **Speed warnings at 1 Hz** for every consumer. FLIPPP already reads them at that rate. The change for FSHANGAR (5 s
  before) must be accepted at its migration (BLOCK 9).

**Live validation (2026-09-24):** LIVE TEST, MSFS 2024, Fenix A319 parked with engines running.

- All groups were read without error, and the three SimVars absent from the audit (magnetic heading, left and
  right gear) returned plausible values.
- The Fenix masking was observed.
- **Still open:** the "full taxi / takeoff / landing" checklist of the definition of done, the pitch and bank signs,
  and the warnings set to true were not flown (the aircraft was static). They are AUTOMATED ONLY (golden fixtures).

**Original plan:**

- **Goal:** normalized telemetry from stock SimVars, usable by any aircraft and composed by the Fenix session.
- **Scope:**
  - `FSGAP.GenericSimConnect` provider (`MatchSpecificity.Generic`).
  - Maps FAST, SYSTEMS and ENVIRONMENT into `AircraftTelemetry`, with the unit fixes (VS ×60, brake fraction ×100,
    fuel flow lb/h → kg/h).
  - Speed warnings at ≥ 1 Hz (FLP behaviour).
  - Staleness → `Unknown` (G-T12).
  - Immutable full snapshots replace the partial-sample merge (fixes D1).
  - The Fenix session composes this provider and overrides it with a list of "known wrong on Fenix" values forced
    to `Unavailable`: APU RPM/GEN, yellow hydraulics, BAT2, slats, spoilers, engine anti-ice. Fenix-specific
    index mapping: green/blue hydraulics, BAT1.
- **Files and components (source):** FSH `Simulator/LiveTelemetryMapper.cs`, `SimConnectTelemetrySample.cs`,
  registry notes; FLP registry cadence change.
- **Dependencies:** BLOCKs 2–4.
- **Tests required:**
  - Golden mapper tests: the same raw struct gives the same values as the legacy mapper, for every field listed
    in the mapping document.
  - Units, `Unavailable` overrides, staleness timing (FakeTimeProvider), per-section cadence guarantees.
- **Risk:** medium. The cadence change of the speed warnings for FSH must be accepted explicitly (FLP already
  does it).
- **Rollback:** revert.
- **Definition of done:**
  - Every mapping-document row marked "Generic provider" is implemented or explicitly deferred (P3).
  - Live checklist: a full taxi / takeoff / landing with values compared against the legacy CSV diagnostic export.

## BLOCK 6 — Fenix systems telemetry (LVARs) — DONE (version 0.6.0)

**Delivered** (details in [../fenix-system-telemetry.md](../fenix-system-telemetry.md)):

- `ISimulatorVariableReader` (Abstractions, read-only), implemented by `SimConnectSimulator` on its single connection.
  - Each list is one batched request, through a struct emitted at runtime and SimConnect.NET's public `GetAsync<T>`.
  - The Fenix names never enter FSGAP.SimConnect.
- A Fenix session polls:
  - 14 proven LVARs every 1 s: IR1–3 modes, 6 pump switches, 3 fire handles, 2 engine fire lights;
  - `HYDRAULIC PRESSURE:1/2` every 5 s.
- Polling rules:
  - it happens only inside a session, so only for an aircraft the recognizer accepts;
  - it is gated on the detector: no read with nothing loaded; everything discarded, with a new generation, when
    another aircraft appears;
  - it stops with the session.
- The overlay goes through the BLOCK 5 composition point (`TransformedTelemetryProvider`, which now owns the
  polling), with Core freshness.
- Contract: `EngineTelemetry.FireHandlePulled` and `FireWarningLit`, `ApuTelemetry.FireHandlePulled`.
- Capabilities: + `InertialReferences`, `FuelPumps`, `Hydraulics`, `Fire`.
- 76 tests added: mapping, composition, lifecycle, legacy parity fixtures, reader, architecture and failure
  boundary.

**Deviations from the plan below, and why:**

- **Not all 39 LVARs.** 14 are migrated, 15 deferred, 8 discovery-only and 2 unknown. The full classification is in
  the inventory.
  - The FIRE TEST, agent and MASTER WARNING pushbuttons are counters, so crew-action events (G-C2, still deferred).
  - The agent lights only fed the probe.
- **Cockpit monitor and `EngFireTestProbe` not ported.**
  - Both produce logs only, and the probe never achieved failure correlation.
  - The BLOCK 6 specification requires diagnostics not to reach the business API, and no consumer subscribes to
    them.
  - The probe is classified DIAGNOSTIC ONLY; D2 disappears with it.
- **1 s instead of 10 Hz.** 10 Hz only served sub-second FIRE TEST counter presses, which are not exposed.
- **Fire panel instead of fire detection.** Handles and lights are exposed as neutral fields (new contract fields).
  `FireDetected` stays Unavailable.
- **Green/blue hydraulics added.** These are stock SimVars whose index meaning is Fenix knowledge, ECAM-verified in
  the audit. Yellow stays Unavailable.
- **APU and electrical deferred.** There is no validated source for the APU operating state, and no contract field
  for the one reliable battery voltage.

**Live validation (2026-09-24):** LIVE TEST, MSFS 2024, Fenix A319 parked with both engines running, sample read-only.

- Both groups were read without error on the single connection, through the emitted struct.
- IR1–3 NAV, the six pumps ON, three handles stowed, lights off.
- Green 2805 psi, blue 2812 psi.
- **Not tested live:** changes of the switches and selectors, a pulled handle, a FIRE TEST (no manual cockpit
  interaction during this block). These are AUTOMATED ONLY (legacy parity fixtures).

**Original plan:**

- **Goal:** expose the proven Fenix cockpit states through normalized telemetry.
- **Scope:**
  - The 39-LVAR group lives in FSGAP.Fenix and is polled **only while a Fenix session is attached** (today it
    runs for every aircraft).
  - ADIRS modes → `InertialReferences[1..3].Mode`.
  - Pump switches → `FuelPumps[left-1..right-2].IsOn`.
  - Capabilities: `InertialReferences` and `FuelPumps` set to true.
  - The cockpit monitor and `EngFireTestProbe` move as **internal, thread-safe** diagnostics (fixes D2), with the
    Fenix-to-Fenix re-arm from FLP's WIP once committed.
  - `FireDetected` stays `Unavailable` (lights are ambiguous during tests).
  - No crew-action public API (G-C2 deferred).
- **Files and components (source):** FSH `Simulator/FenixCockpit*`, `CockpitActionEvent.cs`,
  `EngFireTestProbe.cs`, the Fenix part of `SimConnectVariableRegistry.cs`.
- **Dependencies:** BLOCKs 3–5.
- **Tests required:**
  - Port the detector (24), monitor (9), snapshot (8) and probe (13) tests.
  - New: concurrent `Reset` vs sample, and no LVAR poll for a non-Fenix aircraft.
- **Risk:** low to medium. The 10 Hz cost is unchanged but scoped.
- **Rollback:** revert.
- **Definition of done:**
  - Normalized ADIRS and pump values are live-verified.
  - The probe's PASS on ENG1 and ENG2 is reproduced through FSGAP.

## BLOCK 7 — Fenix failure provider — DONE (version 0.7.0)

**Delivered** (details in [../fenix-failures.md](../fenix-failures.md), table in
[../fenix-failure-mapping.md](../fenix-failure-mapping.md)):

- `FenixFailureProvider` (internal):
  - trigger, clear and read of the active failures over the EFB (`saveManual`, `manual`);
  - echo confirmation, then a list read-back (3 × 400 ms);
  - 3 s per request; never a command retry; commands serialized per session (fixes D4: one injector per session).
- `FenixOptions` (public): EFB address and timeout, default `http://127.0.0.1:8083/`.
- Catalog:
  - the 384-entry EFB catalog embedded verbatim;
  - **40 normalized keys**: every id used by FLIPPP (24), FSHANGAR (live, tests, fire probe) and fenixhangarweb;
  - the other ids reported unkeyed when active.
- Contract, minimal: `FailureCommandStatus.Unavailable` and `Unconfirmed` (G-F2), and `FailuresUnavailableException`.
- 107 tests added:
  - catalog;
  - trigger, clear and read against a fake EFB;
  - lifecycle, non-Fenix aircraft, telemetry regression;
  - no raw-id leakage.

**Deviations from the plan below, and why:**

- **No probe or availability API (G-F3).** Availability is reported by every call (`Unavailable`,
  `FailuresUnavailableException`); no consumer needed a separate probe.
- **No "clear everything this session triggered" helper (D5).**
  - It would rebuild the aircraft state from the commands sent, which the BLOCK 7 specification forbids ("no lying
    cache").
  - A consumer can clear what `GetActiveFailuresAsync` reports.
- **The ported legacy tests are replaced by equivalent FSGAP tests.** They cover the echo anomaly, a missing title
  refused before HTTP, timeouts and connection errors.
- **Live checklist reduced to one failure.** Only `air-conditioning.cpc.1` was written live, the failure FSHANGAR
  itself used for its first round trip. Triggering one id per FLIPPP tier in the user's session was judged
  unnecessary risk: the mapping of those ids is covered by the catalog tests.

**Live validation (2026-09-24):** LIVE TEST.

- **Read-only.** EFB reachable; live catalog identical to the embedded one (384 ids).
- **Write.** `air-conditioning.cpc.1` triggered, read back, cleared and read back, **twice**.
  - **First run (clear 30 ms after the trigger):** the clear was confirmed, yet the failure was active again moments
    later (asynchronous application by Fenix). It was cleared again with the same payload and stayed clear.
  - **Second run (5 s hold):** clean.
- **Failures left active: 0.**

**Original plan:**

- **Goal:** trigger, clear and read Fenix failures through `IFailureProvider`, with no Fenix id in the public API.
- **Scope:**
  - The EFB HTTP client moves to FSGAP.Fenix: base URL from options, default `127.0.0.1:8083`; 3 s timeout;
    echo-then-manual-list confirmation.
  - The catalog (384 entries) is embedded internally.
  - Normalized failure catalog per DEC-1, covering at least the ids used today (audit §7.3).
  - `GetActiveFailuresAsync` from `/fenix/failures/manual`.
  - Availability (G-F3) and `Unavailable`/`TimedOut` results (G-F2).
  - A single injector per session (fixes D4).
  - An optional helper to clear everything this session triggered (both apps need it: FLP already does this, FSH
    lacks it — D5).
- **Files and components (source):** FSH `Run/FenixFailureInjector.cs`, `IFenixFailureInjector.cs`,
  `FenixFailureCatalog.cs` + JSON, `FenixLocalApiProbe.cs`; research docs.
- **Dependencies:** DEC-1, DEC-2 (at least decided), BLOCK 2.
- **Tests required:**
  - Port the injector (13) and probe (6) tests.
  - Every normalized key maps to exactly one catalog id.
  - An unknown key gives `NotSupported`, with no HTTP call.
  - Readback parsing.
  - Principle 6 guard: no public type or member exposes a Fenix id (reflection test).
- **Risk:** medium to high. The EFB API is unofficial and can change with a Fenix update. The server migration
  (DEC-2) crosses repositories.
- **Rollback:** revert. The apps keep their injector until BLOCK 9/10.
- **Definition of done:**
  - Live checklist: trigger, read back, clear for `F_PNEUMATIC_CPC_1` and one id per FLP tier, through normalized
    keys only.

## BLOCK 8 — Simulator services

- **Goal:** move the non-aircraft SimConnect services behind FSGAP.
- **Scope:**
  - Now: the nearest-airport lookup (FSH PRODUCTION), on the shared connection or an isolated one as today, with
    the reflection isolated.
  - Later, once FLIPPP's launch WIP is committed and stable: airport parking facility data, `FlightLoad`, and the
    Fenix preset resolver and `.flt` template (these three are Fenix-specific and go to FSGAP.Fenix).
- **Files and components (source):** FSH `SimConnectDataSource.FindNearestIcaoAsync`, `Run/IAirportLocator.cs`;
  later FLP `Sim/Parking/*`, `Sim/SimConnectFlightLoader.cs`, `Launch/*`.
- **Dependencies:** BLOCK 3; FLIPPP WIP committed (for the second half).
- **Tests required:**
  - Nearest-airport math (ported).
  - Payload-parser tests with captured byte fixtures.
  - Timeout behaviour.
- **Risk:** medium (reconstructed native layouts).
- **Rollback:** revert.
- **Definition of done:** the nearest-airport result matches the legacy result live at two airports.

## BLOCK 9 — FSHANGAR migration

- **Goal:** FSHANGAR consumes only FSGAP APIs for simulator and aircraft access.
- **Scope:**
  - Replace `SimConnectDataSource`, `LiveTelemetryMapper`, the merge, the identity and registration resolution,
    the cockpit monitor wiring and the injector, with FSGAP registry, session, telemetry, failures and catalog.
  - Remove the diagnostic duplicate connection.
  - Replace the manual heartbeat `msfs_connected` with the real connection state (D9).
  - Keep the app's flight lifecycle logic; adapt the upload DTO from `AircraftTelemetry`.
  - Server side (DEC-2): switch the failure payloads to normalized keys.
- **Files and components:** FSH `Core/Simulator/*`, `Core/Run/*` (adapters only), `Core/Liveries/*`,
  `Wpf/RunSessionViewModel.cs`, `Core/AppState.cs`, Settings diagnostics; fenixhangarweb failure endpoints.
- **Dependencies:** BLOCKs 2–8 (8 for the nearest airport), DEC-2, DEC-3.
- **Tests required:**
  - The existing 49 coordinator tests and 19 resilience tests must pass against an FSGAP fake.
  - Upload payload parity (golden JSON) for a recorded flight.
  - A no-mutation test for samples.
- **Risk:** high (production app, server contract).
- **Rollback:** a feature switch that keeps the legacy data source for one release, plus a git revert of the
  migration branch. The server keeps accepting legacy ids during the transition (DEC-2).
- **Definition of done:**
  - No reference to SimConnect.NET, LVARs or the Fenix EFB URL remains in FSHANGAR.
  - Live flight uploaded with parity.
  - Initial INOP, scheduled and command failures work through normalized keys.

## BLOCK 10 — FLIPPP migration

- **Goal:** FLIPPP consumes only FSGAP APIs.
- **Scope:**
  - Replace `Flippp.Core/Sim/*`, `Fenix/*` and the failure injector.
  - The scenario catalog uses normalized keys instead of 24 Fenix ids.
  - Stop reading the about 45 SimVars FLIPPP never uses (the FSGAP snapshot carries them anyway).
  - Honour the `Paused` event, or document why not.
  - Keep `FlightSession`, `FlightAnalyzer`, scoring and route logic unchanged.
- **Dependencies:** BLOCKs 2–8; the FLIPPP launch WIP committed; DEC-3.
- **Tests required:**
  - Existing Flippp.Core tests pass against FSGAP fakes.
  - `FlightAnalyzer` results are identical on replayed samples.
  - Identity-match tests covering D7.
- **Risk:** medium to high (the WIP code in flux at audit time).
- **Rollback:** feature switch plus revert.
- **Definition of done:**
  - No SimConnect.NET or Fenix reference remains in Flippp.*.
  - A full challenge round played live with an injected failure.

## BLOCK 11 — Remove duplicated legacy integrations

- **Goal:** one implementation left.
- **Scope:**
  - Delete the now-unused FSH and FLP copies of the migrated bricks and the dead code listed in audit §14.
  - Delete FLIPPP's vendored `FenixHangar.Client.*` tree and its solution and workflow, if you confirm they are
    not needed.
  - Rewrite the stale docs.
  - Separately, in fenixhangarweb: remove the STKP tracking code and route (never moved into FSGAP).
- **Dependencies:** BLOCKs 9 and 10 in production for at least one release.
- **Tests required:** full test suites of both apps and of FSGAP; a build with no SimConnect.NET reference in
  either app.
- **Risk:** low (deletion after parity).
- **Rollback:** revert the deletion commits.
- **Definition of done:**
  - A search for SimConnect, LVAR names, `8083` and `F_` failure ids finds them only inside FSGAP.Fenix and
    FSGAP.SimConnect.
  - No STKP code remains in fenixhangarweb.
