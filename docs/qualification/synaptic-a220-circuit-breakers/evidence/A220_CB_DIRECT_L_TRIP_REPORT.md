# A220 — direct L: circuit-breaker trip test (LIGHTS_DOME)

2026-10-23 14:43 UTC. One GO-gated experiment: does a **direct L: write (`=1`)** trip a Synaptic A220 breaker, with
**no B: event at all**? Tool `lab/A220BreakerProbe` mode `ltrip-test`. Raw:
[`A220_CB_DIRECT_L_TRIP_RESULTS.json`](A220_CB_DIRECT_L_TRIP_RESULTS.json) /
`raw/breaker-probe/20261023-164335-ltrip.json`.

One non-critical breaker (dome, known cell `R B4`). At most two L: writes (open `=1`, restore `=0`). No B: write — the
B: allowlist hash stayed 0; only the gated L: writer, locked to `L:A22X Circuit Breaker R B4`, was used.

Written for: FSGAP + FSHANGAR engineers deciding the CB execution path.

## Answer

**YES — the L: writer already in FSGAP can, on its own, both TRIP and RE-ARM a Synaptic A220 circuit breaker.**
Writing the breaker's `L:` grid cell to `1` tripped it; writing it back to `0` re-armed it. No `B:` input event was
used for either.

## Sequence and results (the six distinctions asked for)

| Precondition | Value |
|---|---|
| Synaptic A220-300 (no-cabin), on ground, stationary | ✅ |
| Both engines stopped, Synaptic parking brake set | ✅ |
| No caution / no warning (`L:A22X Caution/Warning PBA` = 0) | ✅ |
| 55/55 breakers in (all L: = 0) | ✅ |
| No other injection client active | ✅ |

| # | Distinction | Result |
|---|---|---|
| 1 | **Write accepted** | YES — `SetDataOnSimObject(L: R B4 = 1)` → HRESULT `0x00000000` |
| 2 | **L: position modified** | YES — `R B4` 0 → 1; **only that cell** changed (`other_cells_changed = []`); stable across two read cycles (`position_stable = true`) |
| 3 | **Physical open (position truth)** | YES per the L: grid variable (the established position truth; the var is documented "Pulls cockpit circuit breaker when set"). Pilot visual remains the ultimate confirmation. |
| 4 | **Functional effect** | **Not observed in telemetry** — `Caution/Warning PBA` unchanged (0→0). Expected for the non-critical dome light; functional effect UNVERIFIED (a dome-light observation is the pilot's). |
| 5 | **Restoration** | SUCCESS — `L: R B4 = 0` → HRESULT `0x00000000`; `R B4` 1 → 0; all 55 L: = 0; indicators back to baseline. Aircraft left in the initial state. |
| 6 | **FSGAP compatibility** | YES (see below). |

Note: the baseline `B:` echo of the dome event read `1` while its `L:` cell was `0` — once more confirming the `B:`
echo is a stale command value, never the position. The experiment never used `B:`.

## What this simplifies — and what still remains

**Simplifies:** the earlier model required a `B:` input-event sender (`SetInputEvent`) that **does not exist** in the
FSGAP write surface. This experiment shows the whole breaker cycle — **trip `L:=1`, read `L:`, re-arm `L:=0`** — is three
operations the **existing** `ISimulatorVariableWriter`/reader already perform (the L: writer re-arm was proven earlier;
this adds the L: **trip**). So:
- **No new FSGAP transport (no input-event API) is needed.** The "new primitive" blocker from the implementation spec is
  removed.
- A circuit-breaker failure can be modelled as an ordinary **FSGAP L: recipe**: apply = write the cell to 1, restore =
  write it to 0, state = read the cell — exactly the shape FSGAP's existing L:-only degradation/failure recipes use
  ("force a documented cockpit control").
- One SimConnect connection, as today. No bypass client needed in production (the lab tool is still a separate client;
  production would use the runtime's connection).

**Still required (not removed by this result):**
1. **FSGAP SOURCE.** The recipe catalog (`SynapticFailureCatalog`) is inside the compiled `FSGAP.Synaptic.dll`; adding 55
   CB L: recipes (or a parametrised CB recipe keyed by grid cell) needs the FSGAP source + a DLL republish — on the main
   PC. The L: write *mechanism* exists; the *recipe entries* do not.
2. **Position truth stays the L: cell.** The recipe must verify on the L: read-back, never a B: echo; re-arm is `L:=0`;
   reload is the fallback.
3. **Per-Synaptic-version mapping** of CB → L: grid cell (the 55-map), re-resolved per version.
4. **Functional qualification** of effect is unchanged: 5 breakers show a master-caution system response; the dome and
   the other 49 show no telemetry-visible effect — a pilot pass is still needed to name operational effects.
5. **Server/client wiring** as in the reconciliation report: the 55 as `failure_key`s under `INJECT_FAILURE` (data-only,
   no new command type), with the catalog/eligibility rows applied only once FSGAP executes them and the A220 add-on is
   enabled after live qualification.

## Scope / safety honoured

One non-critical breaker; no B: write (ever); two L: writes (open, restore), both HRESULT S_OK; aircraft returned to the
exact initial state (55/55 in, no caution/warning). No FSHANGAR server, no Loader/EFB/FSGAP change, no Synaptic patch, no
commit/push/merge, no other failure test. STOP after the experiment.
