# FSGAP.Synaptic failures (0.12.0-preview.1, catalog 384 keys since 0.12.0-preview.4, + 55 circuit breakers since 0.13.0-preview.4)

> **0.13.0-preview.4:** the catalog also lists the A220's **55 cockpit circuit breakers** (`electrical.circuit-breaker.*`),
> executable only through the breaker activation described in [Circuit breakers](#circuit-breakers-0130-preview4). The
> 384 historical keys, their recipes, their parity with Fenix and the 17 executable recipes are unchanged.

## Purpose and status

A Synaptic A220 session given a variable writer exposes a real `IFailureProvider` with **exactly the normalized
failure keys of the Fenix provider: all 384 since 0.12.0-preview.4** (same keys, display names, categories and
targets). A consumer such as FSHANGAR
uses the same `FailureKey` whichever aircraft is loaded, and checks `FailureCapabilities` per key.

The A220 documents no failure interface. Each executable key is realized by forcing documented A22X cockpit controls into
a degraded configuration (the same mechanism as the controlled degradations, on the same per-session control board). An
active failure therefore means **"the recipe's degraded configuration is in place"**, not "the aircraft diagnosed a
hardware fault". No native simulator failure event (`TOGGLE_*_FAILURE`) and no engine master event is used.

**The provider is intentionally executable before full live qualification**, so that failures can be tested from
FSHANGAR. **Catalog parity does not imply that every effect has been live-qualified**: only the controls marked
"live-validated" below were written and observed in the cockpit (BLOCK 10C.3 / 11.1). Provenance never gates execution.

| Status | Count | Meaning |
|---|---|---|
| Validated | 2 | live-validated control, recipe matches the key |
| Assumed | 5 | documented control matching the key, not yet exercised live |
| Approximation | 10 | closest coherent documented degradation; the effect approximates the failure |
| Unmapped | 367 | no documented Synaptic control: listed for parity, `Operations = None`, `TriggerAsync` answers `NotSupported` (23 of the original 40 keys, and all 344 keys added in 0.12.0-preview.4) |

**Catalogue parity is not execution parity.** A consumer sees the same 384 keys on the Fenix and on the Synaptic
session, and reads per key whether it can act (`FailureCapabilities.CanTrigger(key)`, `Operations`): 384 executable
on the Fenix, 17 on the Synaptic. The 344 keys added in 0.12.0-preview.4 have no Synaptic recipe: none is inferred
from a name, no A22X control is guessed, no native simulator failure is used.

## Semantics

- **Trigger** reads the recipe's controls; refuses a value that is neither normal nor degraded, and a control held by
  another active failure or degradation (conflict); leaves controls already degraded outside FSGAP alone (not claimed,
  never restored); writes the degraded values (explicit set, never a toggle); succeeds once they are read back. A write
  cut halfway is `Unconfirmed` and what was written stays owned so `ClearAsync` can restore it.
- **Clear** writes back what this session's trigger changed (the qualified normal value, or the value found before the
  trigger for continuous controls) and succeeds once read back. Clearing a failure that is not active succeeds without
  writing; clearing a configuration set outside FSGAP is `Rejected`.
- **Read active** lists the failures this session triggered whose controls still hold their degraded values. If the pilot
  sets a control back, the failure is no longer active and nothing is rewritten.
- **Several failures** may be active together when their recipes touch different controls; a recipe needing a control
  held by another failure or by a controlled degradation is rejected. (Controlled degradations keep `MaxActive = 1`.)
- **Ownership** is in memory, per session: dropped on disconnection or aircraft change, never persisted. Disposing the
  session clears what it triggered, best effort and bounded, only while the same aircraft is loaded.
- Without a variable writer, a Synaptic session keeps `FailureCapabilities.None`.

## Matrix

The 40 keys that predate 0.12.0-preview.4 (the 17 executable recipes are all among them). The 344 later keys are all
Unmapped; they are listed in [fenix-failure-mapping.md](fenix-failure-mapping.md).

| FailureKey | Synaptic effect | Mechanism | Controls touched | Trigger value | Clear value | Read active | Status |
|---|---|---|---|---|---|---|---|
| `air-conditioning.cpc.1` | automatic pressurization replaced by manual mode (cabin pressure controller out of the loop) | A22X control state | `L:A22X Man Press` | 1 | 0 | owned controls still degraded | Approximation |
| `air-conditioning.pack.1.overheat` | left pack shut down, as after an overheat (live-validated control) | Controlled degradation | `L:A22X L Pack Off` | 1 | 0 | owned controls still degraded | Approximation |
| `air-conditioning.pack.1.regulator-fault` | trim air off: zone temperature regulation lost | A22X control state | `L:A22X Trim Air Off` | 1 | 0 | owned controls still degraded | Approximation |
| `electrical.static-inverter` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `electrical.generator.1` | left generator off line (live-validated control) | Controlled degradation | `L:A22X L Gen Off` | 1 | 0 | owned controls still degraded | Validated |
| `electrical.generator.2` | right generator off line | A22X control state | `L:A22X R Gen Off` | 1 | 0 | owned controls still degraded | Assumed |
| `electrical.bus.ac-1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `electrical.bus.ac-ess` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `electrical.bus.dc-1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `electrical.bus.dc-2` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `electrical.bus.dc-bat` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `fire.lavatory.smoke` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `fire.engine.1.loop-a` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `fire.fdu.1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `fuel.fqi.channel-2` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `fuel.pump.left-1` | left boost pump selector off | A22X control state | `L:A22X L Boost Pump` | 0 | 1 | owned controls still degraded | Assumed |
| `fuel.pump.right-1` | right boost pump selector off | A22X control state | `L:A22X R Boost Pump` | 0 | 1 | owned controls still degraded | Assumed |
| `hydraulic.blue.electric-pump` | system 3 pump 3A off, 3B takes over (A320 blue ~ A220 system 3; live-validated control) | Controlled degradation | `L:A22X ACMP 3A` | 0 | 1 | owned controls still degraded | Validated |
| `hydraulic.yellow.electric-pump` | system 2 AC motor pump 2B off (A320 yellow ~ A220 system 2) | A22X control state | `L:A22X ACMP 2B` | 0 | 1 | owned controls still degraded | Assumed |
| `hydraulic.blue.low-level` | both system 3 pumps off: system 3 unpressurized | Composite | `L:A22X ACMP 3A` + `L:A22X ACMP 3B` | 0 / 0 | 1 / 1 | owned controls still degraded | Approximation |
| `hydraulic.green.low-level` | system 1 shutoff valve switch to its non-normal position (A320 green ~ A220 system 1) | A22X control state | `L:A22X Hyd 1 SOV` | 1 | 0 | owned controls still degraded | Approximation |
| `hydraulic.blue.leak` | both system 3 pumps off: system 3 unpressurized | Composite | `L:A22X ACMP 3A` + `L:A22X ACMP 3B` | 0 / 0 | 1 / 1 | owned controls still degraded | Approximation |
| `hydraulic.green.leak` | system 1 shutoff valve switch to its non-normal position and PTU off | Composite | `L:A22X Hyd 1 SOV` + `L:A22X PTU` | 1 / 0 | 0 / 1 | owned controls still degraded | Approximation |
| `ice-rain.aoa-heat.standby` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `ice-rain.pitot-heat.fo` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `indicating.display.ecam-lower` | lower display brightness to zero (display dark) | A22X control state | `L:A22X Lower Brightness` | 0 | value found before | owned controls still degraded | Approximation |
| `landing-gear.brake.wheel-1` | alternate brake mode selected: normal brake control degraded (no side-specific brake isolation is documented) | A22X control state | `L:A22X Alternate Brake` | 1 | 0 | owned controls still degraded | Approximation |
| `landing-gear.tyre-pressure.main-1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `landing-gear.tyre-pressure.right-1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `navigation.fmgc.1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `navigation.mcdu.1.recoverable-fault` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `navigation.adf.1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `navigation.gps.1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `navigation.ils.1.localizer` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `pneumatic.bleed-valve.1` | left engine bleed off | A22X control state | `L:A22X L Bleed Off` | 1 | 0 | owned controls still degraded | Assumed |
| `doors.entry.forward-left` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `doors.entry.aft-left` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `engine.1.surge` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `engine.1.vibration.n1` | no documented Synaptic control: listed, not executable | none | — | — | — | — | Unmapped |
| `engine.1.eiu` | engine 1 services to the aircraft cut: left generator and left bleed off | Composite | `L:A22X L Gen Off` + `L:A22X L Bleed Off` | 1 / 1 | 0 / 0 | owned controls still degraded | Approximation |

Controls: `L:A22X L Gen Off` (live-validated), `L:A22X ACMP 3A` (live-validated), `L:A22X L Pack Off` (live-validated), `L:A22X PFCC 1 Off` (live-validated), `L:A22X R Gen Off` (documented, normal value read live), `L:A22X ACMP 3B` (documented, normal value read live), `L:A22X ACMP 2B` (documented, normal value read live), `L:A22X PTU` (documented, normal value read live), `L:A22X Hyd 1 SOV` (documented, normal value read live), `L:A22X L Boost Pump` (documented, normal value read live), `L:A22X R Boost Pump` (documented, normal value read live), `L:A22X L Bleed Off` (documented, normal value read live), `L:A22X Alternate Brake` (documented, normal value read live), `L:A22X Man Press` (documented), `L:A22X Trim Air Off` (documented), `L:A22X Lower Brightness` (documented).

## Limits to qualify from FSHANGAR

- The executable brake key is `landing-gear.brake.wheel-1` (alternate brake mode, an approximation: no side-specific
  brake isolation is documented); the executable engine key is `engine.1.eiu` (engine 1 services cut). The other wheel
  brake keys and the engine failure keys added in 0.12.0-preview.4 (`engine.1.failure`...) are unmapped on the A220.
- Hydraulic colours map A320 to A220 systems by analogy (blue ~ system 3 electric pumps, yellow ~ system 2, green ~
  system 1). `Hyd 1 SOV` reads 0 in the normal configuration although documented as "selected on"; its degraded value 1
  is assumed.
- The 367 unmapped keys need a mechanism before they can act (for example a documented circuit-breaker map).

## Circuit breakers (0.13.0-preview.4)

The A220's 55 cockpit circuit breakers are listed as `electrical.circuit-breaker.<id>` (category Electrical, target
Aircraft), one recipe per breaker on its documented cell `L:A22X Circuit Breaker <L|R> <A-I><1-5>` (1 = tripped / out,
0 = in). They come from the manifest embedded in FSGAP.Synaptic
(`src/FSGAP.Synaptic/CircuitBreakers/Resources/synaptic-a220-circuit-breakers.json`), generated from the Shadow lab
evidence (`docs/qualification/synaptic-a220-circuit-breakers/`) by `tools/synaptic-cb-manifest/generate.py`; no breaker,
cell or qualification is typed by hand, and tests compare the manifest with the evidence.

- **Trip** reads the cell; writes nothing if the breaker is already out (`Rejected`, FSGAP never resets what it did not
  trip); otherwise writes 1 and succeeds once it reads 1. **Read**: the breaker is active while its cell reads 1.
  **Reset** (`ClearAsync`) writes 0 and succeeds once it reads 0. Same board as every other recipe (read before write,
  read back, ownership, conflicts, disposal); no input event, no second connection, no native simulator failure. An
  unconfirmed write is never repeated automatically.
- **Listed is not executable.** Unlike the historical recipes, a breaker's qualification gates execution. All 55 have
  their mapping and reset validated live; only `CB_LIGHTS_DOME` (`R B4`) has its direct trip (cell = 1) validated live,
  and it is the only breaker executable by default. A breaker qualified later is enabled with
  `FsgapRuntimeOptions.SynapticQualifiedCircuitBreakers` (its id), without a new SDK; a faulty one is disabled with
  `SynapticDisabledCircuitBreakers`. A breaker listed but not enabled answers `NotSupported` with the reason, without
  reading or writing anything. A readable cell never promotes an unqualified breaker.

### Compatibility policy: capabilities, not the Synaptic version

The Synaptic version is **never** a condition. `FsgapRuntimeOptions.SynapticAircraftVersion` is logged next to the
version the manifest was qualified on (1.0.11), as a diagnostic. After a Synaptic update, every breaker whose interface
still works keeps working without a new FSGAP; an incompatibility only disables the breakers it actually affects.

- **Session start** reads no breaker cell: the provider is identified (the strict A220 recognizer), the manifest and the
  session catalog are checked against each other, and every cell starts *unverified*. Reading an unknown `L:` can create
  it in the simulator, so cells are never probed speculatively.
- **Compatibility safety gate.** Before a breaker's cell is read or written, the breaker's input event (`B:`, mapped in
  the lab, for example `AIRLINER_CB_LIGHTS_DOME`) must be listed by the loaded aircraft. The list comes from
  `ISimulatorInputEventInventory` (`SimConnect_EnumerateInputEvents`, read only, the same connection; nothing is ever
  set, subscribed or mapped, and listing creates nothing). It is taken once per session, when a breaker is first used.
  Event absent: `NotSupported` with `COMPATIBILITY_UNVERIFIED`, the cell is neither read nor written, the breaker is
  disabled for the session. List unavailable or empty: `Unavailable` (`COMPATIBILITY_UNVERIFIED`), nothing done, retried
  on the next use. Same name, another hash: presence established (the hash is a build artifact; the name is the
  contract), the change is logged and recorded. Without an inventory, breakers are listed but not executable.
- **First use** of a breaker (after the gate) reads its documented cell before anything is written. The breaker is then disabled for the
  rest of the session, alone, when its cell cannot be read while the simulator answers and the aircraft is attached
  (`Rejected`), holds a value that is neither 0 nor 1 (`Rejected`), or does not take a write (`Unconfirmed`, never
  retried). A lost connection or a replaced aircraft is `Unavailable` and disables nothing.
- A tripped breaker whose cell becomes unreadable stays reported active (its reset was never confirmed); it never hides
  the other active failures. Breaker failures are returned as results, never thrown; the historical recipes, the
  degradations and the other breakers are not affected.
- Each breaker's status keeps its dimensions apart: listed in the catalog, independent evidence (input event), interface
  (unverified / accessible / unavailable / incoherent value / write ineffective), lab qualification of the direct trip,
  last execution, observed functional effect (only the 5 master cautions seen in the lab; never inferred from a write).
- A successful trip answers `Succeeded` with **`POSITION_CONFIRMED`** (the cell read back 1) and
  **`AIRCRAFT_EFFECT_UNVERIFIED`** (FSGAP does not observe the aircraft's reaction): a confirmed position is never
  reported as an observed effect.

**Residual limit.** The gate proves the breaker's input event belongs to the loaded aircraft; it does not prove the
breaker's *cell* name is unchanged. If a Synaptic update renamed the cell but kept the input event, writing the old cell
name would create an unused variable that reads back 1 (`POSITION_CONFIRMED`, `AIRCRAFT_EFFECT_UNVERIFIED`). Only a
live check (the lab's automap, or an effect visible in telemetry) can close that gap; the functional effect of 50
breakers is not observable from telemetry anyway. The direct trip of 54 breakers is still to be qualified live: see
`docs/qualification/synaptic-a220-circuit-breakers/LIVE_QUALIFICATION_PROCEDURE.md`.

- **Ownership** is the same as for every recipe: FSGAP never resets a breaker it did not trip (a breaker already pulled
  is `Rejected`), and a breaker the pilot pushed back in is no longer active.
