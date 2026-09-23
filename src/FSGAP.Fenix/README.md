# FSGAP.Fenix

Aircraft provider for the Fenix Simulations A319 / A320 / A321.

## What it does in 0.1.0

- `FenixAircraftProvider.Match` recognizes a Fenix A319/A320/A321 from an `AircraftDescriptor` and returns a
  normalized `AircraftIdentity` (developer, manufacturer, family, model, ICAO type, plus registration and livery
  when the descriptor carries them).
- `AttachAsync` opens a session that declares `AircraftCapabilities.None`: telemetry snapshots are entirely
  `Unavailable` and failure commands return `NotSupported`.

Detection is a placeholder heuristic (see `Detection/FenixAircraftDetector.cs`); it is not derived from the Fenix
aircraft files.

## What it does not do yet

No SimConnect connection, no LVAR/HVAR/ClientData access, no Fenix failure ids, no dependency on any Fenix SDK.

## Planned layout (BLOCK 1 onwards)

Folders are created when their first class lands, not before:

| Folder       | Content                                                                                     |
|--------------|---------------------------------------------------------------------------------------------|
| `Detection/` | Aircraft recognition. Exists today; BLOCK 1 replaces the heuristic with FSHANGAR's rules.  |
| `Variables/` | Internal catalogue of Fenix LVARs / HVARs / events. Never exposed publicly.                  |
| `Telemetry/` | `ITelemetryProvider` implementation mapping Fenix variables to `AircraftTelemetry`.         |
| `Failures/`  | `IFailureProvider` implementation mapping `FailureType` + `FailureTarget` to Fenix failures. |

Everything Fenix-specific stays `internal` to this assembly: consumers only ever see the FSGAP.Abstractions types.
