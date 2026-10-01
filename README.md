# FSGAP_SDK

**FSGAP is a multi-aircraft SDK** for Microsoft Flight Simulator. Since 0.12.0-preview.2 an application references one
package, `FSGAP`, and uses one `FsgapRuntime`: the runtime owns the single simulator connection and composes the
built-in aircraft providers internally (see [Usage](#usage) and `docs/architecture.md`).

Version 0.10.0, the first multi-provider release: Fenix A319/A320/A321 (`FSGAP.Fenix`) and Synaptic A220-300
(`FSGAP.Synaptic`), side by side on one simulator connection. It provides:

- contracts for aircraft providers, telemetry and failures;
- normalized failure keys;
- simulator connection, state and aircraft-detection contracts;
- an installed-aircraft catalog contract;
- provider resolution;
- **Fenix A319/A320/A321 recognition and normalized identity**, and a catalog of the installed Fenix liveries
  that resolves registrations (`FSGAP.Fenix`);
- **a real MSFS SimConnect transport** (`FSGAP.SimConnect`): automatic connection and reconnection, pause and
  crash state, detection of the loaded aircraft, and **generic flight telemetry** (flight state, position at 1 Hz,
  attitude, speeds, angle of attack, weight, body accelerations, engines with oil, starter, thrust lever and reverser,
  gear, brakes, steering, flaps, control surface deflections, flight-envelope warnings, APU bleed, cabin
  pressurization, weather) read on that same single connection, plus a
  read-only, batched reader of named simulator variables on that connection (`ISimulatorVariableReader`);
- **Fenix telemetry**: a Fenix session exposes the generic telemetry with the values known to be wrong on Fenix
  masked, plus the proven Fenix systems: ADIRS modes, fuel pump switches, fire panel (handles, fire warning lights)
  green/blue hydraulic pressure and reservoir, and the BAT1 voltage;
- **Fenix failures** through the local Fenix EFB: a normalized catalog of 40 failure keys (the failures the
  applications use today), trigger, clear and read of the active failures, never exposing a Fenix id;
- **a simulator airport service** (`IAirportService`): the nearest airports to any coordinate, from the simulator's own
  facility list, on the same single connection;
- **installed aircraft liveries** (`IInstalledLiveryService`, 0.10.0): every (aircraft title, livery name) pair
  MSFS 2024 can load, including streamed marketplace content, on the same single connection;
- **fuel pump mode** (0.10.0): `FuelPumpTelemetry.Mode` (`Off` / `Auto` / `On`) next to the binary `IsOn`;
- **Synaptic A220-300 provider** (`FSGAP.Synaptic`, 0.10.0): strict recognition, normalized identity with a
  conservative registration and its source, the generic telemetry with the values known to be wrong masked plus boost
  pump modes, APU switch and bleed selection and engine fire pushbuttons, a catalog of the installed A220 liveries from
  the simulator enumeration. It is registered next to the Fenix provider; the registry picks the provider per loaded
  aircraft, and a session survives ATC ID changes (see `docs/synaptic-a220.md`). Known limitations in 0.10.0:
  failures are not supported (`FailureCapabilities.None`; 0.12.0-preview.1 adds the Fenix failure key set through A22X
  control recipes, see `docs/synaptic-failures.md`); `ReverserEngaged` is deliberately Unavailable (the generic
  MSFS value was proven false); antiskid is not qualified; master warning/caution are not exposed; an MSFS
  disconnect/reconnect was not qualified live.

Parking search, flight loading, the APU operating state and the electrical buses are not implemented yet. 0.9.0 closes
the generic telemetry gaps that blocked moving FSHANGAR onto FSGAP; that migration is the next step. See
`docs/generic-telemetry.md`, `docs/fenix-system-telemetry.md`, `docs/fenix-failures.md` and
`docs/simulator-airport-service.md`.

## What is FSGAP?

FSGAP is the abstraction layer between flight simulation applications and aircraft-specific integrations.

Add-on aircraft for Microsoft Flight Simulator each expose their systems differently: LVARs, HVARs, SimConnect
ClientData, custom events, vendor SDKs. FSGAP_SDK hides these differences behind one vendor-neutral API. An
application asks for normalized telemetry ("is the APU running?") or a normalized action ("trigger an engine 1
fire"). An aircraft provider translates the request into the technology of the loaded aircraft.

## Goals

- Isolate aircraft-specific technology inside dedicated providers.
- Normalized telemetry: one model for every aircraft, with explicit "unknown" and "unavailable" states.
- Normalized failure control: trigger, clear and read failures by normalized `FailureKey`, never by vendor
  failure id.
- Simulator awareness: connection state, pause and crash, loaded-aircraft detection.
- Fresh, immutable data: every reading carries its observation time and expires when stale, and snapshots cannot
  be mutated.
- Capability discovery: every provider declares at runtime what it can do.
- Support multiple aircraft ecosystems (Fenix, PMDG, iniBuilds, generic SimConnect aircraft, and others).
- Keep consuming applications vendor-independent.

## Consumers

Planned consumers:

- **FSHANGAR**
- **FLIPPP**

Neither is referenced by this repository. They consume the SDK; the SDK never depends on them.

## Architecture

```text
                    MSFS
                     |
          +----------+----------+
          |                     |
       Fenix APIs            PMDG APIs          (LVAR / HVAR / ClientData / events / SDKs)
          |                     |
     FSGAP.Fenix           FSGAP.PMDG           aircraft providers
          \                     /
           \                   /
            FSGAP.Abstractions                  normalized contracts
                    |
               FSGAP.Core                       registry, resolution, shared building blocks
                    |
            +-------+-------+
            |               |
        FSHANGAR          FLIPPP                consuming applications
```

**How to read this diagram.** It shows how data flows at runtime, from the simulator up to the applications. It
does not show compile-time dependencies. At compile time every arrow points towards `FSGAP.Abstractions`:

```text
FSGAP.Core          --> FSGAP.Abstractions
FSGAP.Fenix         --> FSGAP.Abstractions, FSGAP.Core
FSHANGAR / FLIPPP   --> FSGAP.Abstractions, FSGAP.Core   (plus the provider packages they choose to ship)
FSGAP.Abstractions  --> nothing but the .NET base class library
```

`FSGAP.PMDG` is shown as a future provider and does not exist yet.

## Repository layout

```text
src/
  FSGAP/                FsgapRuntime: the application entry point (one package, one runtime, one connection);
                        composes the built-in providers internally (Composition/)
  FSGAP.Abstractions/   contracts: providers and sessions, telemetry, capabilities, failures (FailureKey,
                        FailureCatalog), simulator (connection, state, aircraft detector), installed-aircraft
                        catalog, FsgapOptions
  FSGAP.Core/           AircraftProviderRegistry, AircraftSession, ObservableState, TelemetryFreshness,
                        TransformedTelemetryProvider,
                        PollingTelemetryStream, null-object providers
  FSGAP.Fenix/          FenixAircraftProvider (recognition, identity, telemetry, failures), FenixOptions and
                        FenixInstalledAircraftCatalog (installed liveries, registration resolution)
  FSGAP.Synaptic/       SynapticAircraftProvider (A220-300 recognition, identity, telemetry, degradations, failures) and
                        SynapticInstalledAircraftCatalog (liveries from the simulator enumeration, registrations)
  FSGAP.SimConnect/     SimConnectSimulator: MSFS connection lifecycle, simulation state, aircraft detection,
                        generic telemetry, variable reader, airport service
                        (the only assembly referencing SimConnect.NET)
tests/                  xUnit tests, one project per library (none needs MSFS)
samples/                FSGAP.Runtime.Console: the application sample (FSGAP runtime only);
                        FSGAP.SimConnect.Console: internal research and qualification tooling (not an example)
docs/architecture.md    principles, design and future targets
docs/generic-telemetry.md  the generic telemetry: SimVars, groups, cadences, conversions, Fenix policy
docs/fenix-system-telemetry.md  the Fenix system telemetry: variables, transport, overlay, LVAR inventory
docs/fenix-failures.md    the Fenix failure provider: EFB transport, catalogue, key policy, results, lifecycle
docs/fenix-failure-mapping.md  FailureKey ↔ Fenix id table (reference for the server migration)
docs/simulator-airport-service.md  the airport service: native mechanism, reflection boundary, search, limits
docs/simulator-installed-liveries.md  the installed-livery enumeration: contract, native mechanism, packet layout, limits
docs/synaptic-a220.md   the Synaptic A220-300 provider: detection, identity, registration, policy, overlay, catalog
docs/decisions/         architecture decision records (ADRs)
docs/audits/            BLOCK 1 audit of the existing Fenix/MSFS integrations, mapping and extraction plan;
                        BLOCK 10A read-only discovery audit of the Synaptic A220-300 (evidence for FSGAP.Synaptic)
```

## Usage

FSGAP is **one multi-aircraft SDK**. An application references one package, `FSGAP`, creates one `FsgapRuntime`, and
works with the loaded aircraft's normalized session. It never composes aircraft providers and never branches on the
aircraft vendor: what an aircraft supports is read from `session.Capabilities`.

```xml
<PackageReference Include="FSGAP" Version="[0.12.0-preview.2]" />
```

```csharp
var options = new FsgapRuntimeOptions
{
    Sdk = new FsgapOptions { ApplicationName = "MyApp", DataDirectory = @"C:\ProgramData\MyApp\fsgap" },
};

// One runtime: one simulator connection (connects in the background, retries while MSFS is absent, reconnects), and
// the built-in aircraft providers (Fenix A319/A320/A321, Synaptic A220-300) composed internally.
await using var runtime = new FsgapRuntime(options, loggerFactory);
await runtime.StartAsync();

await foreach (var state in runtime.WatchSessionAsync(cancellationToken))
{
    // Attached, NoAircraft, NotSupported (no provider for this aircraft), Ambiguous, AttachFailed.
    if (state.Session is not { } session)
    {
        continue;
    }

    var identity = session.Identity;   // manufacturer, model, ICAO type, registration and its source, livery...

    if (session.Capabilities.Telemetry.FlightState)
    {
        var telemetry = await session.Telemetry.GetSnapshotAsync();
        if (telemetry.Flight.IndicatedAirspeedKnots.TryGetValue(out var ias)) { /* a fresh reading, not a default */ }
    }

    // The same failure keys on every supported aircraft; per key, the capabilities say whether it can act.
    var blueLeak = new FailureCommand(FailureKey.Parse("hydraulic.blue.leak"), FailureTarget.HydraulicSystem("blue"));
    if (session.Capabilities.Failures.CanTrigger(blueLeak))
    {
        var result = await session.Failures.TriggerAsync(blueLeak);
        // Succeeded, or Unavailable (nothing applied), Unconfirmed (may be applied: read before retrying), Rejected,
        // Failed, NotSupported.
    }

    if (session.Capabilities.Failures.CanReadActiveFailures)
    {
        var active = await session.Failures.GetActiveFailuresAsync();
    }

    // Controlled degradations (documented controls forced into a degraded configuration, not failures).
    foreach (var degradation in session.Capabilities.Degradations.Catalog)
    {
        var current = await session.Degradations.GetStateAsync(degradation.Key);
    }
}

// Simulator services, whatever the aircraft: generic telemetry, nearest airport, installed aircraft of every family.
var flight = (await runtime.GenericTelemetry.GetSnapshotAsync()).Flight;
if (flight.LatitudeDegrees.TryGetValue(out var lat) && flight.LongitudeDegrees.TryGetValue(out var lon))
{
    var nearest = await runtime.Airports.FindNearestAirportAsync(new GeoPosition(lat, lon));   // null: none within 50 NM
}

foreach (var catalog in runtime.InstalledAircraft)
{
    var installed = await catalog.GetAllAsync();
}
```

The session is owned by the runtime (do not dispose it); disposing the runtime closes it, restoring what it applied when
it can. Adding an aircraft family to FSGAP (for example iniBuilds or PMDG) changes nothing in the application. The
provider-level types (`FSGAP.Fenix`, `FSGAP.Synaptic`, `FSGAP.SimConnect`) remain separate assemblies inside the SDK;
their own documentation describes them, but applications do not use them.

## Build

Requirements: .NET 8 SDK or later. Neither MSFS nor any aircraft add-on is needed to build or test.

```bash
dotnet restore
dotnet build
dotnet test
```

Live check against a running MSFS (see `docs/simconnect-lifecycle.md`):

```bash
dotnet run --project samples/FSGAP.Runtime.Console -- --minutes 1
```

`dotnet pack` produces versioned NuGet packages: `FSGAP`, the one package applications reference, and the SDK's
internal packages it depends on (`FSGAP.Abstractions`, `FSGAP.Core`, `FSGAP.Fenix`, `FSGAP.Synaptic`, `FSGAP.SimConnect`): final versions in `artifacts/releases/<version>/`, pre-release versions in
`artifacts/preview-packages/`. `artifacts/packages/` holds the immutable 0.9.0 packages; the build refuses to overwrite
any package or to produce 0.9.0 again. Applications will consume them from a package feed with a pinned version (see
`docs/decisions/0003-nuget-distribution.md`). Nothing is published yet.
