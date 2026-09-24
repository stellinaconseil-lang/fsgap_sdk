# Fenix system telemetry (FSGAP.Fenix, 0.6.0)

A Fenix session combines the simulator's generic telemetry ([generic-telemetry.md](generic-telemetry.md)) with the
Fenix-specific systems that the audited applications had already proven live: ADIRS mode selectors, fuel pump
switches, the fire panel, and green/blue hydraulic pressure.

```text
generic AircraftTelemetry (FSGAP.SimConnect)
      │  FenixGenericTelemetryPolicy: mask what is wrong on Fenix (speed brake)
      ▼
masked generic snapshot  ◄── Fenix overlay (FenixSystemState, polled by the session)
      │  FenixTelemetryComposer + TelemetryFreshness
      ▼
Fenix session AircraftTelemetry
```

This is the BLOCK 5 composition point extended, not a second chain. The session's provider is still a Core
`TransformedTelemetryProvider` over the generic telemetry. Its transformation now also overlays the Fenix state, and
it owns the Fenix polling, which it stops when the session is disposed.

The telemetry is read-only: it never writes an LVAR, an HVAR or an event. Failures (0.7.0) are a separate transport,
the EFB, documented in [fenix-failures.md](fenix-failures.md).

## Transport: one connection, batched

- **Audit of the legacy mechanism** (FSHANGAR `SimConnectDataSource`, FLIPPP identical):
  - **API.** The 39 LVARs are fields of one struct, `FenixCockpitVars`, declared with SimConnect.NET attributes
    (`[SimConnect("L:…", "number")]`).
  - **Request.** They are read with a one-shot `SimVars.GetAsync<FenixCockpitVars>(0)`: one data definition, one
    native request.
  - **Cadence.** Every 100 ms, from a `PeriodicTimer` on the main connection, whatever aircraft is loaded (a
    C172 was read too). The FSHANGAR diagnostic connection read them a second time.
  - **Threading.** The sample was raised on the timer thread and dispatched 10 times per second to the UI thread.
  - **Failures and reconnection.** Handled by the connection's generic retry; no per-group isolation.
- **FSGAP.**
  - **Contract.** The vendor-neutral `ISimulatorVariableReader` (Abstractions) reads a list of
    `SimulatorVariable(name, unit)` values as one batched request.
  - **Implementation.** `SimConnectSimulator` implements it on **its single native connection**:
    - FSGAP.SimConnect cannot declare a struct holding Fenix names, so it emits one at runtime per distinct list:
      a public struct with one `[SimConnect(name, unit, FloatDouble)]` double per variable;
    - it reads that struct through SimConnect.NET's public `GetAsync<T>` (see `VariableSetStructs`). No private
      member of the library is touched;
    - the struct is cached, and the library registers the definition again after a reconnection.
  - **Ownership.**
    - FSGAP.Fenix owns the names and their meaning, and never references SimConnect.
    - FSGAP.SimConnect never sees a Fenix name; a binary scan in the architecture tests guarantees it.
  - **Result.** SimConnect native connections: **1**. No client, lifecycle or reconnect loop exists in
    FSGAP.Fenix.

## Groups, cadence and load

| Group | Variables | Cadence | Why |
|---|---|---|---|
| COCKPIT | 14 LVARs: IR1–3 mode, 6 pump switches, ENG1/ENG2/APU fire handles, ENG1/ENG2 fire pushbutton lights | 1 s | Legacy used 10 Hz only to catch sub-second FIRE TEST presses on counters, which FSGAP does not expose. A selector, a pump switch, a latched handle or a lit fire warning stays in its state for seconds. |
| HYDRAULICS | 2 stock SimVars: `HYDRAULIC PRESSURE:1` (green), `:2` (blue), Psi | 5 s | The audited applications' systems cadence; a slow quantity. |

- **Added native activity.** 1 + 0.2 = **1.2 batched reads/s**, carrying 16 variables. These reads exist only while
  a Fenix session lives and the attached Fenix is loaded.
- **Total with BLOCK 5.** About 1.7 generic group reads/s, plus 0.2–0.5 identity reads/s, plus 1.2 Fenix reads/s,
  so **about 3.1–3.4 native reads per second**.
- **Legacy comparison.** 10 reads/s of 39 LVARs for every aircraft, plus the identity, systems and position
  groups.

## Semantic mapping

| FSGAP field | Source | Raw → normalized | Not provided |
|---|---|---|---|
| `InertialReferences[i]` (Index 1–3) `.Mode` | `L:S_OH_NAV_IR{1,2,3}_MODE` | 0 → `Off`, 1 → `Navigation`, 2 → `Attitude`; anything else → Unknown | `Aligned`, `Fault`: Unavailable (no validated source; never inferred from the selector) |
| `FuelPumps[id].IsOn`, ids `left-1`, `left-2`, `center-1`, `center-2`, `right-1`, `right-2` | `L:S_OH_FUEL_{LEFT,CENTER,RIGHT}_{1,2}` | 0 → false, 1 → true; other → Unknown. **Switch position only**, not pump pressure | `Fault` (low pressure): Unavailable |
| `Engines[n].FireHandlePulled` | `L:S_OH_FIRE_ENG{1,2}_BUTTON` | 0 STOWED → false, 1 PULLED → true | — |
| `Engines[n].FireWarningLit` | `L:I_OH_FIRE_ENG{1,2}_BUTTON` | 0/1 → false/true. **Lit on a real fire *and* during a FIRE TEST** | — |
| `Engines[n].FireDetected`, `Apu.FireDetected` | — | — | **Unavailable.** No source distinguishes a fire from a test. |
| `Apu.FireHandlePulled` | `L:S_OH_FIRE_APU_BUTTON` | 0 → false, 1 → true | — |
| `HydraulicSystems["green"/"blue"].PressurePsi` | `HYDRAULIC PRESSURE:1/2` (Psi) | as read (cross-checked against the Fenix ECAM) | `Pressurized`: Unavailable (no validated threshold) |
| `HydraulicSystems["yellow"]` | — | present, all values Unavailable | Index 3 reads 0 psi while the ECAM shows 3000 |

**Contract extension (minimal).** `EngineTelemetry.FireHandlePulled`, `EngineTelemetry.FireWarningLit` and
`ApuTelemetry.FireHandlePulled` are new. They are neutral fire panel states: the only honest way to expose what the
fire panel proves without calling a test a fire. `TelemetryCapabilities.Fire` now covers fire detection and
fire panel state.

## Composition, freshness, lifecycle

- **Overlay.**
  - Sections only Fenix feeds (IRs, pumps, hydraulics) replace the generic sections, which are empty.
  - Fire panel fields merge into `Engines` by index, adding an entry if the generic engines have not arrived yet.
  - Where both sources could exist, a Fenix value that is not Unavailable wins; otherwise the generic value stays.
  - The generic speed brake stays masked; no Fenix source replaces it.
- **Partial updates.** A group read replaces only its own sections of an immutable `FenixSystemState`.
- **ObservedAt.** Every Fenix value carries its own receive time.
- **Freshness.**
  - The composed snapshot goes through Core `TelemetryFreshness` with `StaleAfter`.
  - A Fenix value that stops arriving turns Unknown, exactly like a generic one.
  - A Fenix value that is not supported is Unavailable.

| Event | Fenix system telemetry |
|---|---|
| No Fenix session (no aircraft, PMDG, Asobo, iniBuilds, C172, a title that merely contains "A320") | **zero Fenix reads**: polling exists only inside a session created by `AttachAsync`, which accepts only aircraft the Fenix recognizer matches (`Dedicated`) |
| Nothing loaded (menu, connection lost) | no read; held values age into Unknown after `StaleAfter` |
| Another aircraft loaded (A319 → A321, Fenix → C172) | no read; state discarded under a new generation so an in-flight read cannot land; the old session publishes Unavailable. The new aircraft's session starts from nothing |
| Reconnection with the same aircraft | the session's one loop per group resumes. No second loop and no second connection |
| A group read fails | only that group's values expire; one warning on the first failure, one information line on recovery |
| Session disposed (aircraft change handled by the app, `Stop`, application exit) | loops cancelled (an in-flight read included) and awaited |

The session is attached with the descriptor the detector published. `FenixAircraftProvider` takes the detector as
`aircraftDetector` to know when that aircraft is no longer the loaded one.

## Capabilities of a Fenix session (0.6.0)

With generic telemetry and a variable reader:
- `FlightState`, `Warnings`, `Engines`, `LandingGear`, `FlightControls`;
- `InertialReferences`, `FuelPumps`, `Hydraulics`, `Fire`.

Not declared:
- `Apu`: no proven source for master, running, available or bleed;
- `Electrical`: no contract field for the one reliable battery reading;
- no failures in the telemetry sections; failures are a separate capability since 0.7.0 ([fenix-failures.md](fenix-failures.md)).

## Inventory of the 39 legacy cockpit LVARs

Source: FSHANGAR `SimConnectVariableRegistry.FenixCockpitVars` and `FenixCockpitControlRegistry` (FLIPPP identical),
read-only.

| Technical source | Semantic meaning | Migrated? | FSGAP field | Cadence | Status |
|---|---|---|---|---|---|
| `L:S_OH_NAV_IR1_MODE` | IR1 mode selector 0/1/2 | yes | `InertialReferences[1].Mode` | 1 s | PRODUCTION |
| `L:S_OH_NAV_IR2_MODE` | IR2 mode selector | yes | `InertialReferences[2].Mode` | 1 s | PRODUCTION |
| `L:S_OH_NAV_IR3_MODE` | IR3 mode selector | yes | `InertialReferences[3].Mode` | 1 s | PRODUCTION |
| `L:S_OH_FUEL_LEFT_1` | left tank pump 1 switch | yes | `FuelPumps[left-1].IsOn` | 1 s | PRODUCTION |
| `L:S_OH_FUEL_LEFT_2` | left tank pump 2 switch | yes | `FuelPumps[left-2].IsOn` | 1 s | PRODUCTION |
| `L:S_OH_FUEL_CENTER_1` | centre tank pump 1 switch | yes | `FuelPumps[center-1].IsOn` | 1 s | PRODUCTION |
| `L:S_OH_FUEL_CENTER_2` | centre tank pump 2 switch | yes | `FuelPumps[center-2].IsOn` | 1 s | PRODUCTION |
| `L:S_OH_FUEL_RIGHT_1` | right tank pump 1 switch | yes | `FuelPumps[right-1].IsOn` | 1 s | PRODUCTION |
| `L:S_OH_FUEL_RIGHT_2` | right tank pump 2 switch | yes | `FuelPumps[right-2].IsOn` | 1 s | PRODUCTION |
| `L:S_OH_FIRE_ENG1_BUTTON` | ENG1 fire handle, 0 STOWED / 1 PULLED (latch) | yes | `Engines[1].FireHandlePulled` | 1 s | PRODUCTION |
| `L:S_OH_FIRE_ENG2_BUTTON` | ENG2 fire handle | yes | `Engines[2].FireHandlePulled` | 1 s | PRODUCTION |
| `L:S_OH_FIRE_APU_BUTTON` | APU fire handle | yes | `Apu.FireHandlePulled` | 1 s | PRODUCTION |
| `L:I_OH_FIRE_ENG1_BUTTON` | ENG1 fire pushbutton light (fire or test) | yes | `Engines[1].FireWarningLit` | 1 s | PRODUCTION |
| `L:I_OH_FIRE_ENG2_BUTTON` | ENG2 fire pushbutton light | yes | `Engines[2].FireWarningLit` | 1 s | PRODUCTION |
| `L:S_OH_FIRE_ENG1_TEST` | ENG1 FIRE TEST button, monotonic counter | no | — | — | DEFERRED (crew-action event, G-C2) |
| `L:S_OH_FIRE_ENG2_TEST` | ENG2 FIRE TEST counter | no | — | — | DEFERRED (G-C2) |
| `L:S_OH_FIRE_APU_TEST` | APU FIRE TEST counter | no | — | — | DEFERRED (G-C2) |
| `L:S_OH_CARGO_SMOKE_TEST` | cargo smoke TEST counter | no | — | — | DEFERRED (G-C2) |
| `L:S_OH_FIRE_ENG1_AGENT1` | ENG1 agent 1 discharge button, cumulative counter | no | — | — | DEFERRED (event; absolute value meaningless) |
| `L:S_OH_FIRE_ENG1_AGENT2` | ENG1 agent 2 counter | no | — | — | DEFERRED |
| `L:S_OH_FIRE_ENG2_AGENT1` | ENG2 agent 1 counter | no | — | — | DEFERRED |
| `L:S_OH_FIRE_APU_AGENT` | APU agent counter | no | — | — | DEFERRED |
| `L:S_OH_FIRE_ENG2_AGENT2` | ENG2 agent 2 (assumed counter) | no | — | — | UNKNOWN (never proven live) |
| `L:I_ENG_FIRE_1` | ENG1 fire annunciator on the engine panel (fire or test) | no | — | — | DEFERRED (same signal as the fire pushbutton light) |
| `L:I_ENG_FIRE_2` | ENG2 fire annunciator | no | — | — | DEFERRED |
| `L:I_OH_FIRE_ENG1_AGENT1_L` | agent light, lower segment (L/U split inferred) | no | — | — | DISCOVERY ONLY (fire-probe input) |
| `L:I_OH_FIRE_ENG1_AGENT1_U` | agent light, upper segment ("squib armed" with the handle) | no | — | — | DISCOVERY ONLY |
| `L:I_OH_FIRE_ENG1_AGENT2_L` | agent light | no | — | — | DISCOVERY ONLY |
| `L:I_OH_FIRE_ENG1_AGENT2_U` | agent light | no | — | — | DISCOVERY ONLY |
| `L:I_OH_FIRE_ENG2_AGENT1_L` | agent light | no | — | — | DISCOVERY ONLY |
| `L:I_OH_FIRE_ENG2_AGENT1_U` | agent light | no | — | — | DISCOVERY ONLY |
| `L:I_OH_FIRE_ENG2_AGENT2_L` | agent light | no | — | — | DISCOVERY ONLY |
| `L:I_OH_FIRE_ENG2_AGENT2_U` | agent light | no | — | — | DISCOVERY ONLY |
| `L:S_MIP_MASTER_WARNING_CAPT` | MASTER WARNING pushbutton (captain), counter | no | — | — | DEFERRED (event; per-press increment not established) |
| `L:S_MIP_MASTER_WARNING_FO` | MASTER WARNING pushbutton (F/O) | no | — | — | UNKNOWN (never proven live) |
| `L:I_MIP_MASTER_WARNING_CAPT` | MASTER WARNING light (captain) | no | — | — | DEFERRED (no contract field; no consumer) |
| `L:I_MIP_MASTER_WARNING_CAPT_L` | MASTER WARNING light, second segment | no | — | — | DEFERRED |
| `L:I_MIP_MASTER_WARNING_FO` | MASTER WARNING light (F/O) | no | — | — | DEFERRED |
| `L:I_MIP_MASTER_WARNING_FO_L` | MASTER WARNING light, second segment | no | — | — | DEFERRED |

**Totals: 14 PRODUCTION, 15 DEFERRED, 8 DISCOVERY ONLY, 2 UNKNOWN, 0 DEAD.** Every legacy LVAR had a reader: the
cockpit monitor's edge logs, or the fire-test probe.

Stock SimVars with Fenix semantics, also migrated:
- `HYDRAULIC PRESSURE:1/2` (PRODUCTION, 5 s).

Stock SimVars with Fenix semantics, not migrated:
- `HYDRAULIC PRESSURE:3`: DEAD on Fenix, wrong;
- `ELECTRICAL BATTERY VOLTAGE:1`: DEFERRED, no contract field;
- `ELECTRICAL BATTERY VOLTAGE:2`: DEAD, wrong;
- `HYDRAULIC RESERVOIR PERCENT:1/2`: DEFERRED, G-T9;
- `PNEUMATICS APU BLEED AIR`: DEFERRED, never verified on Fenix.

## ENG FIRE TEST probe: DIAGNOSTIC ONLY

FSHANGAR's `EngFireTestProbe` checks, after an ENG FIRE TEST press, that the eight expected reactions (lights,
MASTER WARNING) occurred.
- Its output is log lines and a JSONL trace; `ProbeCompleted` has no production subscriber.
- Its correlation with real failures was never achieved (`F_OH_FIRE_ENG1_LOOP_A` and `F_ENG1_EIU` gave no usable
  signature; the `F_FIRE_FDU1` experiment is paused).

It is a discovery tool, not operational data, so it is **not** ported and has no public API. The FIRE TEST counters
and agent lights it needs stay out of the poll.

## Known limitations

- A FIRE TEST shorter than one second may not appear in `FireWarningLit`. Tests are held for several seconds in
  practice, and no consumer uses test events.
- `FireDetected` stays Unavailable for engines and the APU, and `FireZones` stays empty (no cargo or lavatory
  detection source).
- The APU operating state, IR alignment and fault, pump low pressure, electrical buses and batteries have no source.
- `Pressurized` has no validated threshold.
- **Stream cadence.** The session stream is driven by the generic stream: 1 s cadence while the generic FAST group
  flows. With no generic telemetry, the stream falls back to Core's polling stream at the requested interval.
