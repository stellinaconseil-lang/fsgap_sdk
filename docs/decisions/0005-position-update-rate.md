# 0005 — Aircraft position at 1 Hz by default

- **Status:** Accepted (BLOCK 2, DEC-5). Implemented by the SimConnect transport and generic telemetry blocks.

## Context

Historically FSHANGAR read latitude and longitude every 10 s (ENVIRONMENT group), and FLIPPP inherited that
cadence, so its route animation moves at most every 10 s.

## Decision

- FSGAP samples the aircraft position at **1 Hz by default**, like the other flight-state values.
- **Principle:** FSGAP provides data that is fresh enough, and each consumer downsamples if it wants.
- FSHANGAR may keep uploading positions less often. That is an application choice, not an SDK limitation imposed
  on FLIPPP.
- Weather and other slow-changing values may keep a slower cadence. Every cadence stays below the freshness limit
  (ADR 0006).

## Consequences

- FLIPPP's route progress can become smoother. Its guardrails were tuned for 10 s, so the change must be
  validated when FLIPPP migrates.
- There is a small SimConnect cost: position joins the 1 Hz batched data definition.
