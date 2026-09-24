# FSGAP_SDK architecture

This document records the principles FSGAP_SDK is built on, the current design (version 0.6.0) and targets that are
planned but not implemented. Individual decisions are recorded as ADRs in [decisions/](decisions/README.md). The
BLOCK 1 audit of the existing integrations is in [audits/](audits/).

## Assemblies and dependencies

| Assembly | Role | Depends on | Status |
|---|---|---|---|
| `FSGAP.Abstractions` | Public, vendor-neutral contracts and models | .NET base class library | 0.6.0 |
| `FSGAP.Core` | Vendor-independent mechanisms: provider registry and resolution, session, observation helper, telemetry helpers | Abstractions | 0.6.0 |
| `FSGAP.Fenix` | Provider for the Fenix A319/A320/A321: recognition, normalized identity, installed livery catalog ([details](fenix-identity-and-catalog.md)), generic-telemetry policy and system telemetry ([details](fenix-system-telemetry.md)) | Abstractions, Core, M.E.Logging.Abstractions | 0.6.0 |
| `FSGAP.SimConnect` | Generic MSFS transport: connection lifecycle, simulation state, aircraft detection ([details](simconnect-lifecycle.md)), generic telemetry ([details](generic-telemetry.md)), batched variable reader | Abstractions, Core, SimConnect.NET 0.2.2, M.E.Logging.Abstractions | 0.6.0 ([ADR 0004](decisions/0004-simconnect-layer-and-reflection.md)) |

```text
FSGAP.Abstractions  <-  FSGAP.Core  <-  FSGAP.Fenix
                                    <-  FSGAP.SimConnect   (-> SimConnect.NET)
```

Compile-time dependencies always point towards `FSGAP.Abstractions`. `FSGAP.SimConnect` never depends on
`FSGAP.Fenix`. Tests enforce the rule:

- `FSGAP.Abstractions` references only the base class library;
- its public surface names no vendor, simulator library or application;
- `FSGAP.Core` references only the base class library and `FSGAP.Abstractions`;
- `FSGAP.SimConnect` references no provider, and its public API exposes no SimConnect.NET type.

No FSGAP assembly references FSHANGAR or FLIPPP.

## Core concepts

```text
Simulator side                                  Aircraft side
--------------                                  -------------
ISimulatorConnection    status, session clock   IAircraftProvider  --Match-->  AircraftMatch { Specificity, Identity }
ISimulatorStateProvider pause, crashes                             --AttachAsync-->  IAircraftSession
IAircraftDetector  ---- AircraftDescriptor ---->                                       |- Identity      : AircraftIdentity
                                                                                        |- Capabilities  : AircraftCapabilities
IInstalledAircraftCatalog  installed liveries,                                          |- Telemetry     : ITelemetryProvider
                           registration, variant                                        '- Failures      : IFailureProvider
```

- **`AircraftDescriptor`**: raw facts reported by aircraft detection. It holds title, ATC data, ICAO type,
  registration, livery display name, **livery folder** and package path. Every field is optional.
- **`IAircraftProvider`**: an aircraft integration. It is registered once, answers `Match`/`CanHandle`, and opens
  sessions.
- **`AircraftMatch`**: whether a provider supports an aircraft, how specifically, and the normalized
  `AircraftIdentity` it establishes.
- **`IAircraftSession`**: a provider attached to one specific aircraft. It carries the identity, the capabilities,
  the telemetry and the failures.
- **`AircraftProviderRegistry`** (Core): selects the provider for a descriptor.
- **Simulator contracts** (`FSGAP.Abstractions.Simulator`): the connection, the simulation state and the aircraft
  detector ([ADR 0008](decisions/0008-simulator-abstractions.md)).
- **`IInstalledAircraftCatalog`**: installed aircraft read from local files. It holds registration, variant and
  engine, found by livery folder or by registration.
- **`FsgapOptions`**: host settings. It holds the application name, the data directory, the connection retry
  delay and the telemetry staleness limit. Vendor settings belong to their provider.

## Principles

### Principle 1: Applications must never depend on aircraft vendor APIs

FSHANGAR, FLIPPP and future applications reference the FSGAP packages and register the providers they ship. They
never read an LVAR, call a vendor SDK or endpoint, or know a vendor event name or failure id.

### Principle 2: Aircraft-specific implementations depend on FSGAP abstractions

A provider implements `IAircraftProvider`, `ITelemetryProvider` and `IFailureProvider`. The dependency never goes
the other way. `FSGAP.Abstractions` knows no provider and nothing about Fenix, PMDG, SimConnect or Airbus system
architecture.

### Principle 3: Unsupported data is different from false/zero, and old data is not current data

Every scalar reading is a `TelemetryValue<T>` with three states:

| State | Meaning |
|---|---|
| `Unavailable` | The provider cannot supply this value for this aircraft. This is the default state. |
| `Unknown` | The provider supports the value but has no valid reading right now: never received, invalid, or **stale**. |
| `Known` | The value is valid, fresh and readable. |

- `default(TelemetryValue<T>)` is `Unavailable`. A provider that does not set a property therefore reports
  "cannot supply", never `false` or `0`.
- `Value` throws unless the value is known. Consumers use `TryGetValue`, `GetValueOrDefault(fallback)` or
  `IsKnown`.

**Freshness** ([ADR 0006](decisions/0006-telemetry-freshness.md)):

- Every known value carries `ObservedAt`, the time it was read at its source.
- `ExpireIfOlderThan(now, maxAge)` turns a known value older than `maxAge` into `Unknown` and keeps `ObservedAt`,
  so its age stays visible.
- `TelemetryFreshness.ExpireStaleValues` applies the rule to a whole snapshot. The limit is
  `FsgapOptions.Telemetry.StaleAfter`, 15 s by default.

Failures follow the same rule. `GetActiveFailuresAsync` throws `NotSupportedException` when the provider cannot
read failures, so an empty result always means "no active failure" and never "cannot tell".

### Principle 4: Capabilities must be discoverable at runtime

`IAircraftSession.Capabilities` declares what the provider can do for this aircraft.

- `Telemetry` has one flag per section: `FlightState`, `Warnings`, `Engines`, `Apu`, `InertialReferences`,
  `FuelPumps`, `Electrical`, `Hydraulics`, `Fire`, `LandingGear`, `FlightControls`.
- `Failures` has `CanReadActiveFailures` and the provider's **`FailureCatalog`**. Each definition has a
  `FailureKey`, a display name, a coarse `FailureCategory`, supported targets and supported operations.
  - `CanTrigger(key)` and `CanClear(key)` answer per failure.
  - `CanTrigger(command)` and `CanClear(command)` also check the target.

Every capability defaults to unsupported, and a provider declares only what it implements. Capabilities are
section-level: inside a supported section, a value can still be `Unavailable`, and the value's own state is
authoritative. Capabilities belong to the session, not to the provider.

### Principle 5: Adding an aircraft family must not require modifying FSHANGAR or FLIPPP business logic

Supporting a new aircraft means writing a new provider (for example `FSGAP.PMDG`) and registering it. Applications
keep working with the same normalized telemetry, capabilities and failure keys. They adapt automatically to
capabilities the new provider does not offer.

### Principle 6: Provider-specific identifiers must not leak through the public normalized API

Vendor identifiers never appear in the public API. This covers LVAR names, event ids, failure ids such as
`FENIX_FAILURE_ID_1234` or `F_FIRE_FDU1`, and EFB endpoints. The public API uses only:

- **`FailureKey`**: an open, normalized, lower-case dotted key such as `navigation.adf.1`
  ([ADR 0001](decisions/0001-failure-key-catalog.md)).
  - Its format rejects vendor-style ids.
  - FSGAP.Abstractions defines no key; each provider publishes its keys in its catalog.
  - The key-to-vendor-id mapping is internal to the provider.
- Normalized enums (`FailureCategory`, `FailureTargetKind`, `InertialReferenceMode`...).
- 1-based indexes for numbered systems (engines, inertial references).
- Stable, provider-assigned normalized keys for named systems and parts (`left-1`, `green`, `nose`, `left-main`),
  shared between telemetry and `FailureTarget`.

An active failure the provider cannot map is reported with a `null` key and a display `Description`, never with
its vendor id. The same rule extends beyond the SDK: servers and applications exchange `failure_key`, never a
vendor failure id ([ADR 0002](decisions/0002-server-failure-key.md)).

### Principle 7: Published data is immutable

Snapshots, sections, definitions, catalogs, commands, descriptors and options are immutable once built:

- records use init-only properties;
- every collection is copied into an immutable array on assignment;
- a new snapshot is derived with `with`.

A snapshot can be shared across threads and consumers safely ([ADR 0007](decisions/0007-immutable-snapshots.md)).

## Design decisions

### Provider and session are separate

- `IAircraftProvider` holds `ProviderId`, `Match` (with `CanHandle` as a default shortcut) and `AttachAsync`.
- `IAircraftSession` holds `Identity`, `Capabilities`, `Telemetry` and `Failures`, and implements
  `IAsyncDisposable`.

The session also owns per-aircraft resources.

### Provider resolution

`AircraftProviderRegistry.Resolve` asks every provider for a match:

1. Providers that do not support the aircraft are ignored.
2. The highest `MatchSpecificity` wins: `Dedicated` beats `Generic`.
3. If several providers share the highest specificity, the result is `Ambiguous`. The candidates are listed and
   none is picked silently.
4. If no provider supports the aircraft, the result is `NotSupported`.

Provider ids are unique, case-insensitively.

### Collections for multiple systems

Engines, inertial references, fuel pumps, electrical buses, hydraulic systems, fire zones, **gear units** and
**flap surfaces** are read-only collections. The model contains no fixed "left/right/nose" or "ADIRS 1/2/3"
properties. Units are part of property names.

### Telemetry model (0.2.0)

`AircraftTelemetry` contains:

- `Flight`: position, altitudes (MSL, **height above ground**, radio altitude), speeds, **touchdown vertical
  speed**, attitude, G.
- `Warnings`: overspeed, flap speed, gear speed, stall. They must be sampled at ≥ 1 Hz.
- `Engines[]`.
- `Apu`.
- `InertialReferences[]`.
- `FuelPumps[]`.
- `ElectricalBuses[]`.
- `HydraulicSystems[]`.
- `FireZones[]`.
- `LandingGear`: the **handle** (the pilot's command) and `Units[]` (the actual extension of each gear unit).
- `FlightControls`: the **flap handle** (the command) and `FlapSurfaces[]` (the actual positions), plus speed
  brake.

Only the needs observed in FSHANGAR and FLIPPP are modelled. The P2 and P3 gaps of the audit mapping are
deliberately left out.

### Telemetry: snapshot and stream

`ITelemetryProvider` offers `GetSnapshotAsync` and `StreamAsync` (`IAsyncEnumerable<AircraftTelemetry>`).
`PollingTelemetryStream` (Core) implements streaming for snapshot-only providers and takes a `TimeProvider` for
tests.

**Cadences of the SimConnect generic telemetry (0.5.0):**

- FAST, 1 s: flight state **including position** ([ADR 0005](decisions/0005-position-update-rate.md)), attitude,
  speeds and the flight-envelope warnings;
- NORMAL, 2 s: gear and flight controls;
- SLOW, 5 s: engines;
- all of them below the staleness limit (15 s by default).

Consumers downsample if they need less. `StreamAsync` honours `TelemetryStreamOptions.Interval`: at most one
snapshot per interval, always the latest.

### Generic telemetry and aircraft policies (0.5.0)

- `SimConnectSimulator.Telemetry` reads the generic MSFS SimVars in three batched groups **on the transport's
  single native connection**: no second SimConnect client, one native request per group read.
- Every group read replaces its own sections of one immutable snapshot. A failing group is isolated: its values
  expire, the others keep flowing, the connection stays up.
- An aircraft change or a stop resets the snapshot to Unavailable. A read issued for the previous aircraft is
  dropped. After a connection loss, the values turn Unknown once `StaleAfter` has passed, streams included.
- The transport knows no aircraft. A provider composes on it with `TransformedTelemetryProvider` (Core):
  `FSGAP.Fenix` masks the generic values known to be wrong on Fenix (the speed brake), then lays its own values over
  the masked snapshot (0.6.0, below).
- Details, SimVar list, conversions and the Fenix policy table: [generic-telemetry.md](generic-telemetry.md).

### Aircraft-specific variables and the Fenix system overlay (0.6.0)

- **Reader.** `ISimulatorVariableReader` (Abstractions, read-only) lets a provider read named simulator
  variables without referencing the simulator library.
  - `SimConnectSimulator` implements it on its single connection.
  - Each list is one batched request: a struct is emitted at runtime and read through SimConnect.NET's public
    `GetAsync<T>`.
  - Native connections: still **1**.
- **Fenix session.**
  - It polls 14 proven cockpit LVARs every second and 2 hydraulic SimVars every 5 s: ADIRS modes, fuel pump
    switches, fire handles and fire warning lights, green/blue pressure.
  - It polls only while its own aircraft is loaded, and discards everything when another aircraft appears.
  - The polling stops with the session. Without a Fenix session, there is no Fenix read.
- **Composition.** Generic snapshot, then the Fenix mask, then the Fenix overlay, then freshness, in one
  `TransformedTelemetryProvider`, which now owns the polling and stops it on dispose.
- **Contract extension.** `EngineTelemetry.FireHandlePulled` and `FireWarningLit`, and `ApuTelemetry.FireHandlePulled`.
  They are neutral fire panel states; a lit warning also means "test", so `FireDetected` stays Unavailable.
- Details and the inventory of the 39 legacy LVARs: [fenix-system-telemetry.md](fenix-system-telemetry.md).

### Simulator observation

Connection status, simulation state and the detected aircraft are exposed as **latest-value streams**:

- each stream yields the current value, then each change;
- a slow observer skips intermediate values but never blocks the producer;
- cancellation throws, and disposing the source completes the stream.

`FSGAP.Core.Observation.ObservableState<T>` implements this pattern. `WaitForStateAsync` and
`WaitForAircraftAsync` wrap common waits. Pause may be `Unavailable`. Crashes are observed through a monotonic
`CrashCount`. `ISimulatorConnection.SessionElapsed` is monotonic across reconnects and reset by `StopAsync`.

### Simulator transport (FSGAP.SimConnect)

`SimConnectSimulator` implements the three simulator contracts on top of SimConnect.NET 0.2.2:

- one background loop owns the native connection;
- it retries every `RetryDelay`, reconnects automatically, and disposes every connection before opening the next;
- native callbacks only update immutable state;
- consumer code never runs on the native thread;
- `Faulted` is reserved for an unusable native library.

Aircraft identity comes from `TITLE`, `ATC ID`, `LIVERY FOLDER` and `LIVERY NAME`, polled every 2 s and then every
5 s once the aircraft is stable. Details, fault semantics and the live validation procedure are in
[simconnect-lifecycle.md](simconnect-lifecycle.md). A live sample is in `samples/FSGAP.SimConnect.Console`.

### Fenix identity and installed catalog (FSGAP.Fenix)

- `FenixAircraftProvider.Match` recognizes a Fenix with the rule `Fenix\D{0,4}(319|320|321)` on `TITLE`, then on
  `LIVERY FOLDER`. The "Fenix" marker is mandatory, so a generic "A320" never matches.
- `AttachAsync` resolves the identity:
  - model, engine and wingtip from the loaded title;
  - registration from the installed livery matched by `LIVERY FOLDER`, then from the ATC id, otherwise unknown;
  - operator ICAO from the livery.
- `FenixInstalledAircraftCatalog` scans the Fenix packages read-only, from `UserCfg.opt`. It indexes them in an
  immutable snapshot swapped atomically, and caches the result as JSON under `DataDirectory/fenix/`.
- `AircraftIdentity` gained two open fields, `WingtipConfiguration` and `OperatorIcao`.
- Details: [fenix-identity-and-catalog.md](fenix-identity-and-catalog.md).

### Failure model

- `FailureKey`: the identity.
- `FailureDefinition` / `FailureCatalog`: what a provider supports.
- `FailureCategory`: grouping only, never an identity.
- `FailureTarget`: which instance.
- `FailureCommand(key, target)`: target defaults to the aircraft.
- `AircraftFailure`: active failure with key, target, category, severity and description.
- `FailureCommandResult`: `Succeeded`, `NotSupported`, `Rejected` or `Failed`.

A trigger or clear outside the catalog returns `NotSupported` without contacting the aircraft. The BLOCK 0
`FailureType` enum has been removed.

### Null-object building blocks

`UnavailableTelemetryProvider` and `UnsupportedFailureProvider` (Core) let a provider open an honest session before
its telemetry or failures are implemented. `FSGAP.Fenix` uses `UnsupportedFailureProvider`, and
`UnavailableTelemetryProvider` when it is given neither generic telemetry nor a variable reader.

### Plugin loading

Providers are registered explicitly with `registry.Register(provider)`. Dynamic loading can be added later without
contract changes.

### Reflection into SimConnect.NET

Some required MSFS features (airport list, parking data, `FlightLoad`) are reachable only through internal
SimConnect.NET functions. Reflection into them is tolerated temporarily, **only** inside one internal class of
`FSGAP.SimConnect`, pinned to the tested library version and checked at startup. It never appears in a public
contract, in FSGAP.Abstractions or in application code ([ADR 0004](decisions/0004-simconnect-layer-and-reflection.md)).

### Packaging and distribution

- All projects target `net8.0`, with nullable reference types and implicit usings.
- Warnings are treated as errors.
- XML documentation is generated and required for every public member.
- The single version, **0.4.0**, is defined in `Directory.Build.props`.
- NuGet versions are pinned centrally in `Directory.Packages.props`.
- `dotnet pack` writes versioned packages to `artifacts/packages/`.
- Applications will consume them from a feed (GitHub Packages planned) with an explicitly pinned version. They
  never copy DLLs or sources ([ADR 0003](decisions/0003-nuget-distribution.md)).
- Nothing is published yet.

## Future targets (documented, not implemented)

### More providers

| Assembly | Scope |
|---|---|
| `FSGAP.PMDG` | PMDG aircraft (737, 777...) through the PMDG SDK / ClientData |
| `FSGAP.iniBuilds` | iniBuilds aircraft |
| `FSGAP.GenericSimConnect` | Any aircraft through standard SimConnect variables, with `Generic` match specificity |

### FSGAP host process

```text
FSGAP Host process   (owns the simulator connection and the providers)
        |
        +-- FSHANGAR
        |
        +-- FLIPPP
```

Several applications may need the simulator at the same time. A single host process could own the connection and
the providers, and serve normalized data over a local transport, through a client library that implements the
same `FSGAP.Abstractions` contracts. The SDK starts as in-process .NET libraries, and the host is an evolution,
not a prerequisite. Immutable snapshots and latest-value streams make this evolution straightforward.
