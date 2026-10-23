# Live qualification of the direct trip of the 54 remaining Synaptic A220 circuit breakers

Status on 0.13.0-preview.4: 55 mappings and 55 resets (cell = 0) validated live; direct trip (cell = 1) validated on
`R B4` (`CB_LIGHTS_DOME`) only. This procedure qualifies the other 54. **Run it in the lab (Shadow) only, never on the
main PC.** It writes nothing but breaker cells, one breaker at a time, and always ends with every breaker in.

## 1. Bench

- FSGAP **0.13.0-preview.4** built from the commit under test, through its own production path: an `FsgapRuntime` with
  `EnableAircraftCommands = true`, `SynapticAircraftVersion` set to the installed version (diagnostic, recorded in the
  logs) and `SynapticQualifiedCircuitBreakers` set to the breaker under test only. Trip = `session.Failures.TriggerAsync(electrical.circuit-breaker.<id>)`, reset =
  `ClearAsync`, read = `GetActiveFailuresAsync` plus a direct read of all 55 cells. This qualifies the exact
  write/read-back path FSHANGAR will use (one connection, the session's variable writer, the control board timings:
  read-back within 3 s every 100 ms).
- No input event (`B:`), no `SetInputEvent`, no calculator code, no second SimConnect client.
- Record the identity of the run: FSGAP commit, MSFS build, Synaptic A220 version, aircraft title. The version is part of
  the evidence (the manifest records the version each result was obtained on) but is not a condition: a run on a newer
  Synaptic version qualifies the breakers on that version, and a breaker that fails is reported, not assumed.
- Before the campaign on a new Synaptic version, check that the 55 documented cells are still in the Synaptic
  documentation (`L:A22X Circuit Breaker <L|R> <A-I><1-5>`): reading a renamed cell would create an unused variable.

## 2. Initial state (checked by the bench, the run stops otherwise)

- Synaptic A220-300 on the ground, stationary, parking brake set, engines stopped, stable electrical power (GPU or APU).
- No master caution or warning (`L:A22X Caution PBA` = 0, `L:A22X Warning PBA` = 0).
- All 55 breaker cells read 0. Positive control first: `CB_LIGHTS_DOME` trips and resets (criteria A to E below).

## 3. Per breaker (54, one at a time, in manifest order)

1. Read the 55 cells (reference) and the indicators.
2. `TriggerAsync`. **A** — `Succeeded` (cell read 1 within 3 s). `Unconfirmed` or `Rejected` fails the breaker.
3. Wait 5 s, read again. **B** — the cell still reads 1 and `GetActiveFailuresAsync` lists the breaker (not a pulse).
4. Read the 54 other cells. **C** — unchanged (no cross effect).
5. Record the indicators (caution / warning, any telemetry change). Informative, not a pass criterion.
6. `ClearAsync`. **D** — `Succeeded` (cell read 0 within 3 s).
7. Wait 5 s, read the 55 cells and the indicators. **E** — every cell back to its reference value; indicators back to
   their reference values.
8. Repeat steps 1–7 for **3 cycles**. The breaker is `QUALIFIED` when A to E hold on the 3 cycles.

## 4. Stop conditions (stop the campaign, then restore)

A cell reading neither 0 nor 1; a breaker that does not reset (D); a cross effect (C); a lost connection or an aircraft
change; a new caution that does not clear after the reset. Then reset every breaker the bench tripped (`ClearAsync`,
read back), verify the 55 cells read 0, and report the breaker and step.

## 5. Persistence checks (once, on `CB_LIGHTS_DOME` and one breaker with a master caution)

- Trip, then reload the aircraft: record whether the cell is still 1. Reset if needed.
- Trip, then restart MSFS: record the cell after the restart. Reset if needed.
- Trip, then dispose the FSGAP session (end of flight in FSHANGAR terms): since ADR 0009 the breaker stays out (nothing
  is written on disposal); record the cell, then reset it explicitly.

## 6. Outputs

- `A220_CB_DIRECT_L_TRIP_RESULTS_54.json`: per breaker and cycle, timestamps, cell values read, criteria A–E, indicators.
- `SHA256SUMS.txt` over the outputs, and the run identity (section 1).
- Breakers that passed are enabled in FSHANGAR by adding their ids to `SynapticQualifiedCircuitBreakers` (no SDK
  change). To make them qualified in a later FSGAP version, copy the results into `evidence/` and extend
  `tools/synaptic-cb-manifest/generate.py` to read them; never edit the manifest by hand.
