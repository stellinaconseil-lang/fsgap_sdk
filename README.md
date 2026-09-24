# FSGAP_SDK

Version 0.4.0. It provides:

- contracts for aircraft providers, telemetry and failures;
- normalized failure keys;
- simulator connection, state and aircraft-detection contracts;
- an installed-aircraft catalog contract;
- provider resolution;
- **Fenix A319/A320/A321 recognition and normalized identity**, and a catalog of the installed Fenix liveries
  that resolves registrations (`FSGAP.Fenix`);
- **a real MSFS SimConnect transport** (`FSGAP.SimConnect`): automatic connection and reconnection, pause and
  crash state, and detection of the loaded aircraft.

Aircraft telemetry, Fenix systems (LVARs) and Fenix failures are not implemented yet.

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
  FSGAP.Abstractions/   contracts: providers and sessions, telemetry, capabilities, failures (FailureKey,
                        FailureCatalog), simulator (connection, state, aircraft detector), installed-aircraft
                        catalog, FsgapOptions
  FSGAP.Core/           AircraftProviderRegistry, AircraftSession, ObservableState, TelemetryFreshness,
                        PollingTelemetryStream, null-object providers
  FSGAP.Fenix/          FenixAircraftProvider (recognition, identity) and FenixInstalledAircraftCatalog
                        (installed liveries, registration resolution)
  FSGAP.SimConnect/     SimConnectSimulator: MSFS connection lifecycle, simulation state, aircraft detection
                        (the only assembly referencing SimConnect.NET)
tests/                  xUnit tests, one project per library (none needs MSFS)
samples/                FSGAP.SimConnect.Console: live validation tool (not a package)
docs/architecture.md    principles, design and future targets
docs/decisions/         architecture decision records (ADRs)
docs/audits/            BLOCK 1 audit of the existing Fenix/MSFS integrations, mapping and extraction plan
```

## Usage

```csharp
var options = new FsgapOptions { ApplicationName = "MyApp", DataDirectory = @"C:\ProgramData\MyApp\fsgap" };

// Simulator: connects in the background, retries while MSFS is absent, reconnects after a loss.
await using var simulator = new SimConnectSimulator(options, logger);
await simulator.StartAsync();
var aircraft = await simulator.AircraftDetector.WaitForAircraftAsync();   // TITLE, ATC ID, LIVERY FOLDER, LIVERY NAME

var registry = new AircraftProviderRegistry();
var fenixLiveries = new FenixInstalledAircraftCatalog(options);   // finds MSFS 2024 from UserCfg.opt
await fenixLiveries.RefreshAsync();                                 // read-only scan, cached under DataDirectory
registry.Register(new FenixAircraftProvider(fenixLiveries));

var resolution = registry.Resolve(aircraft);
if (resolution.IsResolved)
{
    await using var session = await resolution.Selected.Provider.AttachAsync(aircraft);

    if (session.Capabilities.Telemetry.Apu)
    {
        var telemetry = await session.Telemetry.GetSnapshotAsync();
        if (telemetry.Apu.Running.TryGetValue(out var running)) { /* running is a real reading */ }
    }

    // Keys come from the provider's catalog (session.Capabilities.Failures.Catalog), never from a vendor id.
    var engineFire = new FailureCommand(FailureKey.Parse("engine.fire"), FailureTarget.Engine(1));
    if (session.Capabilities.Failures.CanTrigger(engineFire))
    {
        await session.Failures.TriggerAsync(engineFire);
    }
}
```

## Build

Requirements: .NET 8 SDK or later. Neither MSFS nor any aircraft add-on is needed to build or test.

```bash
dotnet restore
dotnet build
dotnet test
```

Live check against a running MSFS (see `docs/simconnect-lifecycle.md`):

```bash
dotnet run --project samples/FSGAP.SimConnect.Console -- --minutes 1
```

`dotnet pack` produces versioned NuGet packages (`FSGAP.Abstractions`, `FSGAP.Core`, `FSGAP.Fenix`,
`FSGAP.SimConnect`) in `artifacts/packages/`. Applications will consume them from a package feed with a pinned version (see
`docs/decisions/0003-nuget-distribution.md`). Nothing is published yet.
