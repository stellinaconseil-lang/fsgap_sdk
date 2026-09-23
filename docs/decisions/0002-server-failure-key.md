# 0002 — Servers and applications exchange `failure_key`

- **Status:** Accepted as the target (BLOCK 2, DEC-2). Not implemented yet; the server is untouched.

## Context

fenixhangarweb stores `fenix_failure_id` values and sends them to the FSHANGAR client in the flight-session
prepare, failure-scenario and command payloads. FLIPPP hard-codes 24 Fenix ids in its scenario catalog. Moving the
Fenix injector into FSGAP is therefore not enough: the vendor id is business data outside FSGAP.

## Decision

Target flow:

```text
FSHANGAR server            stores and sends failure_key (e.g. "hydraulic.pump.blue")
      |
      | failure_key
      v
FSHANGAR client            new FailureCommand(FailureKey.Parse(key), target)
      |
      v
FSGAP                      IFailureProvider.TriggerAsync / ClearAsync
      |
      v
FSGAP.Fenix                internal mapping: FailureKey -> Fenix EFB failure id
      |
      v
Fenix EFB                  F_... id, title
```

- Applications and servers never exchange a vendor failure id once migrated.
- During the migration, a **temporary translation** from legacy Fenix ids to keys will exist, but **outside the
  FSGAP public API**: in fenixhangarweb (a data migration plus a transitional lookup) or in an application-side
  adapter. FSGAP never accepts a vendor id in `FailureCommand`.

## Consequences

- The server migration (SQL, API payloads) is planned in the FSHANGAR migration block. It needs the Fenix key
  catalog from the Fenix failure block first.
- FLIPPP's scenario catalog switches to keys in its own migration block.
