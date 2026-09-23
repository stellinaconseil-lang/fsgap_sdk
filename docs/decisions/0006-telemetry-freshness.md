# 0006 — Telemetry freshness

- **Status:** Accepted (BLOCK 2)

## Context

Both applications merge partial samples and keep the last value of every field forever. There is no stale-data
watchdog, so a value whose feed stopped keeps being used as if current (audit §11, gap G-T12).

## Decision

- `ValueState` keeps three states: `Unavailable`, `Unknown`, `Known`. There is no fourth "stale" state.
- `TelemetryValue<T>` carries **`ObservedAt`**, the time the reading was taken at its source.
  - `Known(value, observedAt)` requires it, so every known value has a reliable age (`GetAge(now)`).
  - The implicit conversion from a raw value was removed, because it created known values without an
    observation time.
- **Expiry:** `ExpireIfOlderThan(now, maxAge)` turns a known value older than `maxAge` into `Unknown`.
  - The expired value **keeps `ObservedAt`**, so its age remains visible while its content is no longer
    readable.
  - A value exactly `maxAge` old is still fresh.
  - Unavailable and Unknown values are never changed.
- `FSGAP.Core.Telemetry.TelemetryFreshness.ExpireStaleValues(snapshot, maxAge)` applies the rule to a whole
  snapshot, relative to its `Timestamp`, and returns a new snapshot.
  - Providers call it every time they build a snapshot.
  - A reflection-based test fails if a telemetry value is added to the model but not covered.
- The limit is configured by `FsgapOptions.Telemetry.StaleAfter`, 15 s by default: it tolerates two missed 5 s
  polls. It must exceed the slowest sampling interval a provider uses.

## Consequences

- Consumers never see an old value presented as current. They can still display its age.
- Providers must record the observation time of every reading, which their SimConnect callbacks provide.
