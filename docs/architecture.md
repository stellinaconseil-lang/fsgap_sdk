# FSGAP_SDK architecture

This document records the principles FSGAP_SDK is built on, the design decisions of version 0.1.0, and targets
that are planned but not implemented.

## Assemblies and dependencies

| Assembly             | Role                                                                                      | Depends on                  |
|----------------------|-------------------------------------------------------------------------------------------|-----------------------------|
| `FSGAP.Abstractions` | Public, vendor-neutral contracts and models.                                              | .NET base class library     |
| `FSGAP.Core`         | Vendor-independent mechanisms: provider registry and resolution, session, shared helpers. | Abstractions                |
| `FSGAP.Fenix`        | Provider for the Fenix A319/A320/A321.                                                    | Abstractions, Core          |

Compile-time dependencies always point towards `FSGAP.Abstractions`. Two tests enforce this: one checks that
`FSGAP.Abstractions` references only the base class library, the other that `FSGAP.Core` references only the base
class library and `FSGAP.Abstractions`. No FSGAP assembly references FSHANGAR or FLIPPP.

## Core concepts

```text
AircraftDescriptor --(IAircraftProvider.Match)--> AircraftMatch { Specificity, AircraftIdentity }
                   --(IAircraftProvider.AttachAsync)--> IAircraftSession
                                                          |- Identity      : AircraftIdentity
                                                          |- Capabilities  : AircraftCapabilities
                                                          |- Telemetry     : ITelemetryProvider
                                                          '- Failures      : IFailureProvider
```

- **`AircraftDescriptor`**: raw facts reported by aircraft detection (title, ATC data, ICAO type, package path).
  Every field is optional.
- **`IAircraftProvider`**: an aircraft integration. It is registered once, answers `Match`/`CanHandle`, and opens
  sessions.
- **`AircraftMatch`**: whether a provider supports an aircraft, how specifically, and the normalized
  `AircraftIdentity` it establishes.
- **`IAircraftSession`**: a provider attached to one specific aircraft. It carries the identity, the capabilities,
  the telemetry and the failures.
- **`AircraftProviderRegistry`** (Core): selects the provider for a descriptor.

## Principles

### Principle 1: Applications must never depend on aircraft vendor APIs

FSHANGAR, FLIPPP and future applications reference `FSGAP.Abstractions` and `FSGAP.Core`, then register the
provider packages they ship. They never read an LVAR, call a vendor SDK or know a vendor event name.

### Principle 2: Aircraft-specific implementations depend on FSGAP abstractions

A provider implements `IAircraftProvider`, `ITelemetryProvider` and `IFailureProvider`. The dependency never goes
the other way: `FSGAP.Abstractions` knows no provider. It knows nothing about Fenix, PMDG, SimConnect or Airbus
system architecture.

### Principle 3: Unsupported data is different from false/zero

Every scalar reading is a `TelemetryValue<T>` with three states:

| State         | Meaning                                                                    |
|---------------|----------------------------------------------------------------------------|
| `Unavailable` | The provider cannot supply this value for this aircraft. Default state.    |
| `Unknown`     | The provider supports the value but has no valid reading right now.        |
| `Known`       | The value is valid and readable.                                           |

`default(TelemetryValue<T>)` is `Unavailable`. A provider that does not set a property therefore reports "cannot
supply", never `false` or `0`. `Value` throws unless the value is known; consumers use `TryGetValue`,
`GetValueOrDefault(fallback)` or `IsKnown`.

Failures follow the same rule. `GetActiveFailuresAsync` throws `NotSupportedException` when the provider cannot
read failures, so an empty result always means "no active failure" and never "cannot tell".

### Principle 4: Capabilities must be discoverable at runtime

`IAircraftSession.Capabilities` declares what the provider can do for this aircraft:

- `Telemetry`: one flag per telemetry section (`FlightState`, `Engines`, `Apu`, `InertialReferences`,
  `FuelPumps`, `Electrical`, `Hydraulics`, `Fire`, `FlightControls`).
- `Failures`: `CanReadActiveFailures`, plus the sets of triggerable and clearable `FailureType`s, with
  `CanTrigger(type)` and `CanClear(type)`.

Every capability defaults to unsupported. A provider declares only what it implements. Capabilities are
section-level. Inside a supported section, a value can still be `Unavailable` (for example, engines are supported
but EGT is not exposed), and the value's own state is authoritative.

Capabilities belong to the session, not to the provider. The same provider can offer different capabilities for
different variants of an aircraft.

### Principle 5: Adding an aircraft family must not require modifying FSHANGAR or FLIPPP business logic

Supporting a new aircraft means writing a new provider (for example `FSGAP.PMDG`) and registering it. Applications
keep working with the same normalized telemetry, capabilities and failure types. They adapt automatically to
capabilities the new provider does not offer.

### Principle 6: Provider-specific identifiers must not leak through the public normalized API

Vendor identifiers such as LVAR names, event ids or failure ids like `FENIX_FAILURE_ID_1234` stay `internal` to
their provider assembly. The public API uses only:

- normalized enums (`FailureType`, `FailureTargetKind`, `InertialReferenceMode`...);
- 1-based indexes for numbered systems (engines, inertial references);
- stable, provider-assigned normalized keys for named systems (`FuelPumpTelemetry.Id`, `HydraulicSystemTelemetry.Id`,
  `ElectricalBusTelemetry.Id`, such as `left-1` or `green`). These keys are the same in telemetry and in
  `FailureTarget`, so a failure can be correlated with telemetry.

A failure that a provider cannot map is reported as `FailureType.Unclassified` with a display `Description`. Its
vendor id is never exposed.

## Design decisions (0.1.0)

### Provider and session are separate

The specification sketched a single `IAircraftProvider` that exposed `Identity`, `Capabilities`, `Telemetry` and
`Failures`. These properties describe one loaded aircraft, not the integration. One Fenix provider must identify
an A319 and an A321 differently. It must also give each attached aircraft its own telemetry state. The concepts are
therefore split:

- `IAircraftProvider` holds `ProviderId`, `Match` (`CanHandle` is a default shortcut) and `AttachAsync`.
- `IAircraftSession` holds `Identity`, `Capabilities`, `Telemetry` and `Failures`, and implements
  `IAsyncDisposable`.

The session is also where future connection resources (a SimConnect handle, subscriptions) will be owned and
released.

### Provider resolution

`AircraftProviderRegistry.Resolve` asks every provider for a match and applies these rules:

1. Providers that do not support the aircraft are ignored.
2. The highest `MatchSpecificity` wins. `Dedicated` (for example Fenix for a Fenix A320) beats `Generic` (for
   example a future generic SimConnect provider). The Generic level lets a catch-all provider coexist with
   dedicated ones.
3. If several providers share the highest specificity, the result is `Ambiguous`. The registry lists the candidates
   and picks none. Registration order never silently decides. The application reports the conflict or chooses
   explicitly.
4. If no provider supports the aircraft, the result is `NotSupported`.

Provider ids are unique, case-insensitively. Registering a duplicate id throws.

### Collections for multiple systems

Engines, inertial references, fuel pumps, electrical buses, hydraulic systems and fire zones are read-only
collections. The model does not use fixed properties such as `Adirs1Aligned`/`Adirs2Aligned`/`Adirs3Aligned`.
It therefore describes a twin-engine Airbus with three ADIRUs, a 737, an ATR, a business jet or a single-engine GA
aircraft equally well. APU and flight state are single objects, because an aircraft has at most one of each.

Units are part of property names (`AltitudeFeet`, `FuelFlowKilogramsPerHour`...) so that no conversion is
ambiguous.

The electrical, hydraulic, fire-zone and flight-control models are deliberately minimal. They grow when a consumer
needs a new field and a provider can supply it.

### Telemetry: snapshot and stream

`ITelemetryProvider` offers `GetSnapshotAsync` and `StreamAsync`, which returns `IAsyncEnumerable<AircraftTelemetry>`.
Streaming uses only the base class library: no Rx and no event bus. Each enumeration is an independent
subscription, ended by its cancellation token.

`FSGAP.Core.Telemetry.PollingTelemetryStream` lets a snapshot-only provider implement streaming in one line. It
takes a `TimeProvider`, so tests control time with `FakeTimeProvider` and never sleep. Push-based providers
(SimConnect data subscriptions) can implement `StreamAsync` natively later without any contract change.

### Failure model

- `FailureType`: a short normalized enum, extended on demand.
- `FailureTarget`: a system kind plus a 1-based index or a normalized id, built through validating factories
  (`FailureTarget.Engine(1)`, `FailureTarget.HydraulicSystem("green")`, `FailureTarget.Apu`...).
- `FailureCommand`: a type plus a target.
- `FailureCommandResult`: `Succeeded`, `NotSupported`, `Rejected` (supported but refused in the current state) or
  `Failed` (error while applying).
- `AircraftFailure`: an active failure with a type, target, severity and display description.

An unsupported trigger or clear returns `NotSupported` instead of throwing. Applications usually present that
outcome to the user rather than treat it as a bug.

### Null-object building blocks

`UnavailableTelemetryProvider` and `UnsupportedFailureProvider` (Core) let a provider open an honest session before
its telemetry or failures are implemented. `FSGAP.Fenix` uses them in 0.1.0.

### Plugin loading

In 0.1.0, providers are registered explicitly with `registry.Register(provider)`. There is no reflection, MEF or
assembly scanning. Dynamic loading can be added later without touching the contracts. A loader would discover
`IAircraftProvider` implementations in provider assemblies (for example in an isolated `AssemblyLoadContext`) and
call `Register`. Stable `ProviderId`s and the dependency rule (providers depend on Abstractions and Core only) are
what make this possible.

### Packaging

All projects target `net8.0`, with nullable reference types and implicit usings enabled. Warnings are treated as
errors. XML documentation is generated and required for every public member of the libraries. The version is
`0.1.0`, defined once in `Directory.Build.props`. NuGet package versions are pinned in `Directory.Packages.props`
(central package management). The three libraries can be packed as NuGet packages; none is published.

## Future targets (documented, not implemented)

### More providers

| Assembly                  | Scope                                                                                 |
|---------------------------|---------------------------------------------------------------------------------------|
| `FSGAP.PMDG`              | PMDG aircraft (737, 777...) through the PMDG SDK / ClientData.                        |
| `FSGAP.iniBuilds`         | iniBuilds aircraft.                                                                   |
| `FSGAP.GenericSimConnect` | Any aircraft through standard SimConnect variables, with `Generic` match specificity. |

Each one is a new assembly that depends on `FSGAP.Abstractions` (and optionally `FSGAP.Core`), and none of them
requires a change in consuming applications.

### Shared simulator connection

Providers will need a SimConnect connection and aircraft detection that produces `AircraftDescriptor`s. These
belong to a separate simulator-access assembly, so that `FSGAP.Abstractions` stays free of SimConnect.

### FSGAP host process

```text
FSGAP Host process   (owns the simulator connection and the providers)
        |
        +-- FSHANGAR
        |
        +-- FLIPPP
```

Several applications may need the simulator at the same time. A single host process would then own the SimConnect
connection and the providers, and serve normalized telemetry and failure commands over a local transport (named
pipes or local sockets). Applications would talk to the host through a client library that implements the same
`FSGAP.Abstractions` contracts, so business logic would not change. The SDK starts as in-process .NET libraries.
The host is an evolution, not a prerequisite.
