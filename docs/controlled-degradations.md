# Controlled degradations (0.11.0-rc.1, unchanged in 0.12.0-preview.1)

## Failures vs controlled degradations

FSGAP exposes two different things, on two different session members, with two independent capabilities:

| | Failure (`session.Failures`) | Controlled degradation (`session.Degradations`) |
|---|---|---|
| Meaning | The aircraft itself represents a component as **failed** (the vendor's failure system). | FSGAP **deliberately forces a documented aircraft control** into a degraded configuration. The component is healthy; the aircraft reacts to the configuration. |
| Example | Fenix `electrical.generator.1`: the generator fails. | A220 `electrical.generator.1.forced-off`: the generator switch is set to OFF. |
| Key type | `FailureKey` | `DegradationKey` (same text format, distinct type; keys end with the action, `forced-off`, never `failed`) |
| Capabilities | `AircraftCapabilities.Failures` (`FailureCapabilities`) | `AircraftCapabilities.Degradations` (`DegradationCapabilities`: catalog + `MaxActive`) |
| Provider | `IFailureProvider` (trigger, clear, read active) | `IDegradationProvider` (apply, restore, read state) |

A generator switch forced off is **not** a generator failure, a pack commanded off is **not** a pack failure, a PFCC
switched off is **not** a PFCC internal fault. A consumer that needs the distinction (for example a maintenance
application recording what happened to an aircraft) must keep it: never record a degradation as a failure.

Consumers check capabilities, never the aircraft vendor:

| Provider | Failures | Degradations |
|---|---|---|
| Fenix A319/A320/A321 | supported (40 keys: trigger, clear, read active) | none (`DegradationCapabilities.None`) |
| Synaptic A220-300 | since 0.12: the 40 Fenix keys, 17 executable through A22X control recipes ([synaptic-failures.md](synaptic-failures.md)); up to 0.11: none | 4 (below), when the provider is given a variable writer |

## Contract

- `GetStateAsync(key)` reads the **control** and returns `Normal`, `Applied` (degraded, and this session put it there),
  `PreExisting` (degraded, but not by this session), `Unknown` (any other value) or `Unavailable` (cannot read now). It
  describes the control configuration, never the health of the component.
- `ApplyAsync(key)` reads the control first. It writes only from the qualified normal value, writes the exact
  degraded value (an explicit set, never a toggle) and returns `Succeeded` only once the value is read back.
- `RestoreAsync(key)` restores only what **this session** applied: it writes the exact qualified normal value and returns
  `Succeeded` only once read back.
- Outcomes: `Succeeded`, `NotSupported` (not in the catalog; nothing contacted), `Rejected` (refused in the current
  state; nothing written), `Unavailable` (control unreachable; nothing written, retry later), `Unconfirmed` (a write was
  sent but not confirmed; read the state before deciding, never retry blindly). Every result carries the observed state.

### Ownership

FSGAP never assumes the aircraft starts in its nominal configuration. A control already degraded when FSGAP looks at it
(set by the pilot, another tool, or before a reconnection) is `PreExisting`: FSGAP does not apply over it, does not claim
it, and never restores it. Ownership is in memory, per session:

- dropped when the simulator disconnects or another aircraft is loaded, never carried over to a new session and never
  persisted across application restarts; states are then read again;
- kept when only aircraft metadata changes (ATC ID churn on the same loaded aircraft);
- released when the control is found back at its normal value (for example the pilot switched it back).

### One active degradation at a time (0.11 preview)

`DegradationCapabilities.MaxActive` is 1: each mechanism was qualified alone, and interactions between several
forced-off systems have not been qualified. A second `ApplyAsync` while one degradation is applied by the session is
`Rejected`; restore the first one. This can be relaxed after combination testing.

### Disposal

Disposing the session (degradations are disposed first) restores the degradation the session applied, best effort and
bounded (5 s), only while the attached aircraft is still the loaded one. It never writes to a replaced aircraft, never
restores a pre-existing degradation, never throws, and logs (never pretends) when it could not restore.

### Writes

Writes go through `ISimulatorVariableWriter`, implemented explicitly by `SimConnectSimulator` on its single native
connection: one local (`L:`) variable, a finite value, a 5 s limit; simulation variables and events are refused. It is
provider plumbing: an aircraft provider writes only the controls it has qualified (the A22X names live only in
`FSGAP.Synaptic`). The interface is public because providers cannot depend on the transport; a consumer holding the
simulator could call it, so it must not be used by applications.

## Synaptic A220-300 catalog

Qualified live on 2026-10-01 (BLOCK 10C.3), **on the ground only**, parked, both engines running, one at a time. Each was
applied by writing the control, observed in the cockpit by the pilot, restored by writing the normal value, and the
recovery was verified. Flight behaviour and the symmetric counterparts (generator 2, pack 2, PFCC 2/3...) are not
qualified and not offered.

| Key | Control (internal) | Normal → degraded → restore | System consequence observed | Confidence |
|---|---|---|---|---|
| `electrical.generator.1.forced-off` | `L:A22X L Gen Off` | 0 → 1 → 0 | OFF light ~0.7 s after the write, EICAS L GEN OFF, electrical synoptic reconfigured; engine 1 unaffected | High |
| `hydraulic.system-3.electric-pump-a.forced-off` | `L:A22X ACMP 3A` (0 Off, 1 Auto, 2 On) | 1 → 0 → 1 | pump 3A off, pump 3B starts automatically, EICAS HYD 3 LO PRESS, master caution ~5 s after the write that cleared by itself after ~23 s. **Loss of one hydraulic source, not a failure of hydraulic system 3.** | Medium/high |
| `air-conditioning.pack.1.forced-off` | `L:A22X L Pack Off` | 0 → 1 → 0 | OFF light, AIR synoptic: left pack inactive and its valve closed, right pack still supplied, EICAS L PACK OFF | High |
| `flight-controls.pfcc.1.forced-off` | `L:A22X PFCC 1 Off` | 0 → 1 → 0 | OFF light ~0.4 s after the write, flight-control synoptic PFCC 1 OFF, EICAS PFCC1 OFF; controls respond after restore (their response while off was not measured). **Not a simulated PFCC hardware failure.** | Medium/high |

Not offered, on purpose: `Hyd 1 SOV` (documented "selected on" but the normal configuration reads 0: value semantics
unresolved), circuit breakers (no documented system mapping or reset), probe heat (a ground-test pulse, not a switch),
engine fire pushbuttons (latched until the aircraft is reloaded), native MSFS failure events (brakes work but engines are
ignored on the A220; kept as research evidence on `research/synaptic-native-failures`).

These degradations are visible in the cockpit (switch OFF light, EICAS message): a pilot sees a switch configuration, not
a hidden failure.

## Live validation of the production path (BLOCK 11.1, 0.11.0-preview.1, accepted)

2026-10-01, LFKJ, A220-300 (No Cabin) Air France, on the ground, engines idle, one native connection. Everything went
through the public API (`session.Degradations`: catalog, `GetStateAsync`, `ApplyAsync`, `RestoreAsync`, session
disposal); the sample `--qualify --degradation-console` only wires the simulator as the provider's writer and logs each
write.

| Check | Result |
|---|---|
| Session | provider `synaptic`; `FailureCapabilities.None`; degradations supported, catalog 4, `MaxActive` 1; no raw variable name in the public catalog |
| Baseline | the four states `Normal` |
| Generator 1, ACMP 3A, pack 1, PFCC 1 | each: `ApplyAsync` `Succeeded` (45-76 ms) → `Applied`; cockpit effect confirmed by the pilot (OFF light, EICAS message, synoptic); `RestoreAsync` `Succeeded` → `Normal`; cockpit back to normal |
| One active at a time | a second `ApplyAsync` while generator 1 was applied: `Rejected`, nothing written |
| Ownership: pre-existing | L PACK set OFF by the pilot: `PreExisting`; apply and restore `Rejected`; clean disposal wrote nothing and the pack stayed OFF |
| Manual pilot restore | after an FSGAP apply the pilot switched L GEN back on: `Normal`, ownership released, no further write (not even on disposal) |
| Restore on disposal | generator 1 applied, session disposed cleanly: one restore write, no exception, cockpit back to normal, next session reads `Normal` |
| Fenix (same build) | failures unchanged (40 keys, read active); degradations `None` |

Not observed live: an ATC ID change while a degradation was applied (covered by automated tests), disconnection (by
design no restore is expected without a connection; ownership drop covered by automated tests).
