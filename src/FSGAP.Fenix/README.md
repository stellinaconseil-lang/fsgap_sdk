# FSGAP.Fenix

Aircraft provider for the Fenix Simulations A319 / A320 / A321.

## What it does (0.4.0)

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
- Sessions still declare `AircraftCapabilities.None`: telemetry snapshots are entirely `Unavailable`, and failure
  commands return `NotSupported`.

Details: [docs/fenix-identity-and-catalog.md](../../docs/fenix-identity-and-catalog.md).

## What it does not do yet

No LVAR/HVAR/ClientData access, no cockpit monitoring, no Fenix telemetry, no Fenix failure ids or EFB calls, and
no dependency on any Fenix SDK or on SimConnect.

## Layout

| Folder | Content |
|---|---|
| `Detection/` | Fenix naming rules (`FenixTokens`) and recognition from a descriptor |
| `Identity/` | Normalized identity values, registration normalization, title/catalog resolution |
| `Msfs/` | MSFS 2024 installation discovery (generic MSFS logic; internal until a second provider needs it) |
| `Catalog/` | Package discovery, livery parsing, scanner, immutable indexed snapshot, JSON cache |
| `Variables/`, `Telemetry/`, `Failures/` | Planned (Fenix systems telemetry and failures); created when their first class lands |

Everything Fenix-specific stays `internal`. The public API is `FenixAircraftProvider` and
`FenixInstalledAircraftCatalog`; consumers otherwise only see FSGAP.Abstractions types.
