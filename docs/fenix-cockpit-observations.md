# Fenix cockpit observations (normalized)

FSGAP 0.12.0-preview.5. A small, **read-only** cockpit-observation capability so a consumer can watch the Fenix
overhead/glareshield controls the Windows client used to poll directly, while holding exactly one `FsgapRuntime`
connection.

## Why it exists

Before FSGAP, the Windows client (`FenixHangarClient`) kept its own raw SimConnect connection just to poll a set of
Fenix cockpit L:Vars at ~100 ms (`FenixCockpitMonitor`, `EngFireTestProbe`). That monitoring is **diagnostic only**: it
never reaches the server, a flight, or reputation. When the client moves to a single `FsgapRuntime` connection, that
second raw connection has to go — but the cockpit readings must survive.

This capability is that bridge. It exposes the same readings through the one connection the runtime already owns, in
normalized terms, with **no raw L:Var name crossing the public API** and **no connection of its own**.

## Shape of the contract

- `IAircraftSession.CockpitObservations` → `ICockpitObservationProvider` with a single
  `GetSnapshotAsync(ct)` that performs **one batched read** of every supported key on the shared connection. The
  consumer owns the cadence (call it at 100 ms if it wants); the provider runs no loop of its own.
- `IAircraftSession.Capabilities.CockpitObservations` → `CockpitObservationCapabilities` (`CanObserve`, the catalog
  `Keys`, `Supports(key)`). Unsupported aircraft (and any aircraft when no simulator-variable reader is wired) declare
  `None` and serve an empty snapshot.
- `CockpitObservationKey` — a normalized, vendor-neutral dotted identifier (`fire-test.engine-1`, `adirs.ir-1.mode`).
  Its grammar deliberately **rejects** vendor identifiers like `L:S_OH_FIRE_ENG1_TEST`.
- `CockpitObservationValue` — a `Boolean` (on/off control) or an `Integer` (a multi-position selector, or a monotonic
  step counter that never returns to zero), each with a `Known` / `Unknown` / `Unavailable` state. A zero-initialized
  value is `Unavailable`, never a spurious "known false". Raw floating-point noise never leaks: booleans are `raw != 0`,
  integers are `Math.Round(raw, AwayFromZero)`.

When another aircraft is loaded (the continuity edge) or the simulator is away / a read fails, every supported key comes
back `Unknown` — never a stale value — and `GetSnapshotAsync` does not throw for those ordinary cases (only cancellation
propagates).

## Where the L:Var names live

The L:Var names exist **only inside `FSGAP.Fenix`** (`FenixCockpitObservations`, `internal`). They are not in
`FSGAP.Abstractions`, not in any public type, and not in the top-level `FSGAP` surface. A guard test
(`CockpitObservationContractTests`) scans the Abstractions assembly and asserts no `L:`-prefixed variable string is
embedded; `FenixArchitectureTests` keeps the Fenix public surface to its three exported types.

## Legacy Windows signal → normalized FSGAP observation (parity, reference only)

Each row is a raw L:Var the Windows `FenixCockpitMonitor` polled, and the normalized key + value kind FSGAP now
publishes for it. This table documents parity; it does **not** change the Windows repository. The catalog is pinned by
`FenixCockpitObservationTests.The_catalog_is_exactly_the_intended_keys_in_order` — 39 controls, in read-batch order.

| # | Normalized key | Kind | Legacy Fenix L:Var |
|---|----------------|------|--------------------|
| 1 | `fire-test.engine-1` | Integer | `L:S_OH_FIRE_ENG1_TEST` |
| 2 | `fire-test.engine-2` | Integer | `L:S_OH_FIRE_ENG2_TEST` |
| 3 | `fire-test.apu` | Integer | `L:S_OH_FIRE_APU_TEST` |
| 4 | `smoke-test.cargo` | Integer | `L:S_OH_CARGO_SMOKE_TEST` |
| 5 | `fire.engine-1.agent-1.discharge` | Integer | `L:S_OH_FIRE_ENG1_AGENT1` |
| 6 | `fire.engine-1.agent-2.discharge` | Integer | `L:S_OH_FIRE_ENG1_AGENT2` |
| 7 | `fire.engine-2.agent-1.discharge` | Integer | `L:S_OH_FIRE_ENG2_AGENT1` |
| 8 | `fire.apu.agent.discharge` | Integer | `L:S_OH_FIRE_APU_AGENT` |
| 9 | `master-warning.captain.input` | Integer | `L:S_MIP_MASTER_WARNING_CAPT` |
| 10 | `adirs.ir-1.mode` | Integer | `L:S_OH_NAV_IR1_MODE` |
| 11 | `adirs.ir-2.mode` | Integer | `L:S_OH_NAV_IR2_MODE` |
| 12 | `adirs.ir-3.mode` | Integer | `L:S_OH_NAV_IR3_MODE` |
| 13 | `fuel-pump.left-1` | Boolean | `L:S_OH_FUEL_LEFT_1` |
| 14 | `fuel-pump.left-2` | Boolean | `L:S_OH_FUEL_LEFT_2` |
| 15 | `fuel-pump.center-1` | Boolean | `L:S_OH_FUEL_CENTER_1` |
| 16 | `fuel-pump.center-2` | Boolean | `L:S_OH_FUEL_CENTER_2` |
| 17 | `fuel-pump.right-1` | Boolean | `L:S_OH_FUEL_RIGHT_1` |
| 18 | `fuel-pump.right-2` | Boolean | `L:S_OH_FUEL_RIGHT_2` |
| 19 | `fire.engine-1.indication` | Boolean | `L:I_ENG_FIRE_1` |
| 20 | `fire.engine-2.indication` | Boolean | `L:I_ENG_FIRE_2` |
| 21 | `fire.engine-1.button.input` | Boolean | `L:S_OH_FIRE_ENG1_BUTTON` |
| 22 | `fire.engine-1.button.light` | Boolean | `L:I_OH_FIRE_ENG1_BUTTON` |
| 23 | `fire.engine-2.button.input` | Boolean | `L:S_OH_FIRE_ENG2_BUTTON` |
| 24 | `fire.engine-2.button.light` | Boolean | `L:I_OH_FIRE_ENG2_BUTTON` |
| 25 | `fire.engine-1.agent-1.light-lower` | Boolean | `L:I_OH_FIRE_ENG1_AGENT1_L` |
| 26 | `fire.engine-1.agent-1.light-upper` | Boolean | `L:I_OH_FIRE_ENG1_AGENT1_U` |
| 27 | `fire.engine-1.agent-2.light-lower` | Boolean | `L:I_OH_FIRE_ENG1_AGENT2_L` |
| 28 | `fire.engine-1.agent-2.light-upper` | Boolean | `L:I_OH_FIRE_ENG1_AGENT2_U` |
| 29 | `fire.engine-2.agent-1.light-lower` | Boolean | `L:I_OH_FIRE_ENG2_AGENT1_L` |
| 30 | `fire.engine-2.agent-1.light-upper` | Boolean | `L:I_OH_FIRE_ENG2_AGENT1_U` |
| 31 | `fire.engine-2.agent-2.discharge` | Boolean | `L:S_OH_FIRE_ENG2_AGENT2` |
| 32 | `fire.engine-2.agent-2.light-lower` | Boolean | `L:I_OH_FIRE_ENG2_AGENT2_L` |
| 33 | `fire.engine-2.agent-2.light-upper` | Boolean | `L:I_OH_FIRE_ENG2_AGENT2_U` |
| 34 | `fire.apu.button.input` | Boolean | `L:S_OH_FIRE_APU_BUTTON` |
| 35 | `master-warning.captain.light` | Boolean | `L:I_MIP_MASTER_WARNING_CAPT` |
| 36 | `master-warning.captain.light-l` | Boolean | `L:I_MIP_MASTER_WARNING_CAPT_L` |
| 37 | `master-warning.first-officer.input` | Boolean | `L:S_MIP_MASTER_WARNING_FO` |
| 38 | `master-warning.first-officer.light` | Boolean | `L:I_MIP_MASTER_WARNING_FO` |
| 39 | `master-warning.first-officer.light-l` | Boolean | `L:I_MIP_MASTER_WARNING_FO_L` |

### Notes on the normalization

- **Fire/smoke tests and agent discharges** (rows 1–8) are monotonic step counters on the real panel; they are
  `Integer` so a consumer can watch them advance (this is what `EngFireTestProbe` keyed on). Row 31
  (`fire.engine-2.agent-2.discharge`) mirrors the legacy registry, where that one signal was a boolean indication
  rather than a counter; the catalog preserves the legacy kind rather than inventing a new one.
- **ADIRS IR mode** (rows 10–12) is a multi-position selector, `Integer`. FSGAP does **not** label the positions
  (`OFF`/`NAV`/`ATT`); the consumer owns that semantics, exactly as the Windows monitor did.
- **Master warning** input vs. light (rows 9, 35–39) are kept as separate observations, matching the legacy split
  between the pushbutton input signal and the annunciator lights.
- The A220 (Synaptic) provider publishes **no** cockpit observations (`CockpitObservationCapabilities.None`); no A220
  mapping is invented here.
