# 0009 — A tripped Synaptic circuit breaker is a persistent condition

- **Status:** Accepted (0.13.0-preview.4)

## Context

Every Synaptic recipe used to share one lifecycle: disposing the session restores what it triggered while the aircraft is
still loaded, and a trigger refuses (`Rejected`) a configuration it did not set. For the 55 circuit breakers this made
two consumer-visible defects:

- **B1.** Closing the application (runtime disposal, aircraft still loaded) wrote every breaker the session had tripped
  back to 0. An application that models a tripped breaker as a defect to be repaired in maintenance saw it silently
  re-armed.
- **B2.** After a reconnection or a restart, a breaker still out was "pulled outside FSGAP": a redelivered trigger
  answered `Rejected`, although the aircraft was exactly in the requested state.

Ownership lives in memory only: a new session cannot tell a breaker an earlier session tripped from one the pilot pulled.

## Decision

Each recipe declares a `SynapticFailureLifecycle`:

| Lifecycle | Recipes | Session end (dispose, lost connection, other aircraft) | Trigger on a state already applied | Explicit clear |
|---|---|---|---|---|
| `RestoredOnSessionEnd` | the 384 historical recipes; the degradations follow the same rule | restores what this session applied, best effort, bounded, aircraft still loaded | `Rejected`: never claims what it did not set | only what this session triggered |
| `Persistent` | the 55 circuit breakers | **nothing written**; ownership released | **idempotent success**, nothing written, after the compatibility gate and a read of the cell | resets the breaker (writes 0, read back), whichever session pulled it |

Unchanged for the breakers: the input-event compatibility gate runs before any cell is read; an unreadable cell, a value
other than 0 or 1, or an unconfirmed write is never reported as a success; a breaker not enabled for the session answers
`NotSupported` without reading anything.

## Consequences

- FSGAP keeps **no** state across sessions. A breaker found back in after the aircraft was reloaded, or after MSFS was
  restarted, is in: FSGAP does not re-apply it on its own. Persistence beyond a reload belongs to the consumer, which keeps
  the business record (a defect) and re-triggers it. The trigger is then idempotent if the breaker is still out.
- A successful trigger means the cell reads 1 (`POSITION_CONFIRMED`). It never means that the aircraft system failed,
  and an explicit clear never means that a defect was repaired. Repair is a business decision of the consumer.
- **Future seam (not built here).** Re-arming a breaker remains `IFailureProvider.ClearAsync` on its key, with no new
  contract. A consumer calls it only on an explicit, authorized request: a maintenance action closing the defect, or a
  controlled test procedure. FSHANGAR's Loader never calls `ClearAsync` today. A maintenance-driven reset would reuse the
  existing command channel with a business authorization defined server-side; that protocol is out of scope of FSGAP.
- Fenix is not affected (separate provider, no change). The historical Synaptic recipes and the degradations keep their
  behaviour, proven by tests.
