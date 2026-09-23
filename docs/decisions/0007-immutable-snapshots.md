# 0007 — Immutable snapshots and models

- **Status:** Accepted (BLOCK 2)

## Context

In FSHANGAR one telemetry sample object is shared by every subscriber, and the first subscriber back-fills it in
place. Later subscribers (coordinator, crash detector, SQLite buffer, upload outbox) therefore receive a modified
object (audit §13, defect D1).

## Decision

- Every public model is immutable once built:
  - telemetry snapshots and sections;
  - failure definitions, catalogs and commands;
  - descriptors, identities and installed-aircraft entries;
  - options and states.
- Models are records with **init-only** properties; `TelemetryValue<T>` is a readonly struct.
- Every public collection property **copies** what is assigned into an immutable array:
  - the caller cannot change a model afterwards by mutating its source list;
  - a consumer casting the property to `IList<T>` gets `NotSupportedException` on any mutation.
- A new snapshot is derived with `with`, which never alters the original. Helpers such as `TelemetryFreshness`
  return new instances.
- `FailureCatalog` is an immutable class, indexed with a frozen dictionary.

## Consequences

- A snapshot can be shared across threads and consumers without copying or locking.
- There is a small allocation cost per snapshot, negligible at the cadences involved (≤ 10 Hz).
