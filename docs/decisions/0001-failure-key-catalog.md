# 0001 — Failures are identified by `FailureKey`

- **Status:** Accepted (BLOCK 2, DEC-1)
- **Supersedes:** the BLOCK 0 `FailureType` enum, which has been removed

## Context

BLOCK 0 identified failures with a closed 8-value `FailureType` enum. The BLOCK 1 audit found about 30 distinct
Fenix failures already in use across FSHANGAR and FLIPPP, covering navigation, FMGC, displays, ice protection,
pneumatics, tyres, vibration, smoke and hydraulics. The Fenix catalog has 384 entries. A closed enum would either
stay too coarse or grow into a copy of one vendor's catalog.

## Decision

- A failure is identified by a **`FailureKey`**: an open, normalized, lower-case dotted string such as
  `navigation.adf.1` or `hydraulic.pump.blue`.
  - The format is validated: 2 to 8 segments, 128 characters at most, letters and digits with single hyphens,
    first segment starting with a letter.
  - The format rejects vendor-style identifiers such as `F_FIRE_FDU1` or `123` by construction.
- Each provider publishes the failures it supports as a **`FailureCatalog`** of **`FailureDefinition`**s. A
  definition holds:
  - a key and a display name;
  - an optional coarse `FailureCategory`;
  - the supported `FailureTarget`s;
  - the supported `FailureOperations` (trigger, clear).
- `FailureCapabilities` holds the catalog. It answers `CanTrigger(key)`, `CanClear(key)`, and the stricter
  `CanTrigger(command)` / `CanClear(command)`, which also check the target.
- `FailureCommand` targets a key. `AircraftFailure` exposes a key; the key is `null` for an active failure the
  provider cannot map, so that such a failure is still reported but never with its vendor id.
- `FailureCategory` is only a grouping hint. Two failures of the same category always have different keys.
- FSGAP.Abstractions defines **no** keys. A test forbids public static `FailureKey` members there.
- The mapping from key to vendor id lives **only** inside the provider (for Fenix: `FSGAP.Fenix`, BLOCK 7). No
  public model carries a vendor id, EFB id or endpoint. A reflection test forbids vendor names on the public
  surface.

## Consequences

- New failures are added by a provider, with no change to FSGAP.Abstractions.
- Consumers discover supported failures at runtime (for example, a diagnostic list built from the catalog).
- The Fenix key set is designed in BLOCK 7, from the ids actually used (audit §7.3). The keys used in tests are
  illustrative only.
- Two providers supporting the same semantic failure should use the same key. Key naming conventions will be
  documented with the first real catalog.

## Follow-up (BLOCK 7, 0.7.0)

- The first real catalog is the Fenix one: 40 keys, exactly the failures used today. Its naming conventions are
  documented in [../fenix-failures.md](../fenix-failures.md#catalogue-and-key-policy):
  - first segment = the system domain of the ATA chapter;
  - then the component and the instance;
  - the target is typed only where the contract has an exact kind.
- The key ↔ Fenix id table for the DEC-2 migration is [../fenix-failure-mapping.md](../fenix-failure-mapping.md).
- `FailureCommandStatus` gained `Unavailable` and `Unconfirmed`, and `FailuresUnavailableException` was added: a
  caller must be able to tell "nothing applied" from "maybe applied", and "no failure" from "cannot tell".
