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

## Decisions needed from you before the blocks that depend on them

| # | Decision | Needed by | Recommendation |
|---|---|---|---|
| DEC-1 | Failure vocabulary (G-F1): extend the closed `FailureType` enum, or replace it with a normalized, string-keyed failure catalog? | BLOCK 2 (contract), BLOCK 7 | A normalized catalog: keys like `nav.adf.1` or `hyd.pump.blue`, each with a `FailureTarget` and an ATA chapter. FSGAP.Fenix maps the keys to Fenix ids internally. |
| DEC-2 | How fenixhangarweb stops storing `fenix_failure_id`: a server migration to normalized keys, or a transitional period where FSGAP.Fenix also accepts legacy Fenix ids at the app boundary. Either way, FSGAP's public API never accepts them. | BLOCK 7, BLOCK 9 | Server migration, with a temporary translation table kept in fenixhangarweb (not in FSGAP) |
| DEC-3 | Distribution: how do the apps consume FSGAP? Options: a local NuGet feed built with `dotnet pack`, git submodule plus ProjectReference, or a private package feed. | BLOCK 9 | A local folder NuGet feed first. Versioned and reproducible, with no source copying. |
| DEC-4 | SimConnect.NET internals: keep reflection on `SimConnectNative` (pinned to 0.2.2), or use a small direct P/Invoke for the facility and FlightLoad calls? | BLOCK 3 | Pin 0.2.2 and isolate every reflection call behind one internal class with a startup self-check. Revisit later. |
| DEC-5 | Lat/lon cadence. Today it is 10 s, and FLIPPP's route animation is tuned for it. Faster is possible. | BLOCK 5 | Keep 10 s for parity; change it later as a deliberate product decision |

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

## BLOCK 2 — Contract extensions (P1) and simulator abstractions

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

## BLOCK 3 — SimConnect transport and connection lifecycle

- **Goal:** one reusable, thread-safe SimConnect connection. It replaces `SimConnectDataSource` in both apps, but
  is not wired to them yet.
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

## BLOCK 4 — Aircraft detection, identity and Fenix installed-aircraft catalog

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

## BLOCK 5 — Generic flight telemetry provider

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

## BLOCK 6 — Fenix systems telemetry (LVARs)

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

## BLOCK 7 — Fenix failure provider

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
