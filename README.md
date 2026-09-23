# FSGAP_SDK

Version 0.1.0. Foundation release: contracts, provider resolution and a Fenix placeholder. No simulator connection yet.

## What is FSGAP?

FSGAP is the abstraction layer between flight simulation applications and aircraft-specific integrations.

Add-on aircraft for Microsoft Flight Simulator each expose their systems differently: LVARs, HVARs, SimConnect
ClientData, custom events, vendor SDKs. FSGAP_SDK hides these differences behind one vendor-neutral API. An
application asks for normalized telemetry ("is the APU running?") or a normalized action ("trigger an engine 1
fire"). An aircraft provider translates the request into the technology of the loaded aircraft.

## Goals

- Isolate aircraft-specific technology inside dedicated providers.
- Normalized telemetry: one model for every aircraft, with explicit "unknown" and "unavailable" states.
- Normalized failure control: trigger, clear and read failures without vendor failure ids.
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
  FSGAP.Abstractions/   contracts: IAircraftProvider, IAircraftSession, telemetry, capabilities, failures
  FSGAP.Core/           AircraftProviderRegistry, AircraftSession, PollingTelemetryStream, null-object providers
  FSGAP.Fenix/          FenixAircraftProvider (identification only in 0.1.0)
tests/                  xUnit tests, one project per library
docs/architecture.md    principles, decisions and future targets
```

## Usage

```csharp
var registry = new AircraftProviderRegistry();
registry.Register(new FenixAircraftProvider());

// The descriptor comes from aircraft detection (not part of 0.1.0).
var aircraft = new AircraftDescriptor { Title = "...", IcaoType = "A320" };

var resolution = registry.Resolve(aircraft);
if (resolution.IsResolved)
{
    await using var session = await resolution.Selected.Provider.AttachAsync(aircraft);

    if (session.Capabilities.Telemetry.Apu)
    {
        var telemetry = await session.Telemetry.GetSnapshotAsync();
        if (telemetry.Apu.Running.TryGetValue(out var running)) { /* running is a real reading */ }
    }

    if (session.Capabilities.Failures.CanTrigger(FailureType.EngineFire))
    {
        await session.Failures.TriggerAsync(new FailureCommand(FailureType.EngineFire, FailureTarget.Engine(1)));
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

The libraries are ready to be packed as NuGet packages (`dotnet pack`), but none is published yet.
