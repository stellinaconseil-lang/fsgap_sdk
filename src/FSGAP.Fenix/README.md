# FSGAP.Fenix

Aircraft provider for the Fenix Simulations A319 / A320 / A321.

## What it does (0.6.0)

- `FenixAircraftProvider.Match` recognizes a Fenix A319/A320/A321 from the generic `AircraftDescriptor`. The rule
  `Fenix\D{0,4}(319|320|321)` is applied to `TITLE`, then to `LIVERY FOLDER`. It returns `Dedicated` and a
  normalized identity with model, ICAO type (inferred), engine and wingtip from the title words.
- `FenixAircraftProvider.AttachAsync` resolves the identity with the installed-livery catalog. Registration comes
  from the installed livery matched by `LIVERY FOLDER`, then from the ATC id, otherwise it is unknown. The operator
  ICAO also comes from the livery.
- `FenixInstalledAircraftCatalog` (`IInstalledAircraftCatalog`):
  - finds MSFS 2024 from `UserCfg.opt`, then the Fenix packages;
  - scans their liveries read-only and indexes them by livery folder (case-insensitive) and registration;
  - caches the result as JSON under `FsgapOptions.DataDirectory/fenix/`.
- **Session telemetry** (all inputs optional, all from the one simulator connection):
  - with `genericTelemetry`: the simulator's generic telemetry, with the values known to be wrong on Fenix masked
    (0.5.0);
  - with `simulatorVariables` (the `ISimulatorVariableReader` of the same connection): the proven Fenix systems
    polled for the session's lifetime and laid over the generic snapshot (0.6.0). These are ADIRS modes, the six
    fuel pump switches, the fire handles and engine fire lights, and green/blue hydraulic pressure;
  - with `aircraftDetector`: no read, and nothing published, once another aircraft is loaded;
  - neither input: `AircraftCapabilities.None`.
- Failure commands return `NotSupported` (failures are BLOCK 7).

Details:
- [docs/fenix-identity-and-catalog.md](../../docs/fenix-identity-and-catalog.md);
- [docs/fenix-system-telemetry.md](../../docs/fenix-system-telemetry.md), which includes the inventory of the 39
  legacy LVARs.

## What it does not do

- No write of any kind: no LVAR, HVAR or event.
- No EFB call and no failure id.
- No fire-test diagnostic probe.
- No dependency on any Fenix SDK or on SimConnect: variables are read through `ISimulatorVariableReader`.

## Layout

| Folder | Content |
|---|---|
| `Detection/` | Fenix naming rules (`FenixTokens`) and recognition from a descriptor |
| `Identity/` | Normalized identity values, registration normalization, title/catalog resolution |
| `Msfs/` | MSFS 2024 installation discovery (generic MSFS logic; internal until a second provider needs it) |
| `Catalog/` | Package discovery, livery parsing, scanner, immutable indexed snapshot, JSON cache |
| `Variables/` | `FenixVariables`: the only place in FSGAP where Fenix variable names exist, and their read groups |
| `Telemetry/` | Generic policy (mask), system mapper, session polling source, composer (overlay) |
| `Failures/` | Planned (BLOCK 7) |

Everything Fenix-specific stays `internal`. The public API is `FenixAircraftProvider` and
`FenixInstalledAircraftCatalog`; consumers otherwise only see FSGAP.Abstractions types.
