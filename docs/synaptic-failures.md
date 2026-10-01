# FSGAP.Synaptic failures (0.12.0-preview.1)

## Purpose and status

A Synaptic A220 session given a variable writer exposes a real `IFailureProvider` with **exactly the 40 normalized
failure keys of the Fenix provider** (same keys, display names, categories and targets). A consumer such as FSHANGAR
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
| Unmapped | 23 | no documented Synaptic control: listed for parity, `Operations = None`, `TriggerAsync` answers `NotSupported` |

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

- The Fenix catalog has no left/right/total brake key and no engine-failure key: the brake key is
  `landing-gear.brake.wheel-1` (alternate brake mode, an approximation: no side-specific brake isolation is documented)
  and the engine keys are `engine.1.surge`, `engine.1.vibration.n1` (unmapped) and `engine.1.eiu` (engine 1 services cut).
- Hydraulic colours map A320 to A220 systems by analogy (blue ~ system 3 electric pumps, yellow ~ system 2, green ~
  system 1). `Hyd 1 SOV` reads 0 in the normal configuration although documented as "selected on"; its degraded value 1
  is assumed.
- The 23 unmapped keys need a mechanism before they can act (for example a documented circuit-breaker map).
