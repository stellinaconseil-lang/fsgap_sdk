# 0008 — Simulator abstractions

- **Status:** Accepted (BLOCK 2)

## Context

BLOCK 0 had no notion of the simulator itself. The audit (gaps G-S1, G-S2) showed what the applications need:

- connection states (MSFS absent, connecting, connected, lost, retrying);
- pause and crash;
- a session clock that is monotonic across reconnects;
- the loaded aircraft and its changes.

## Decision

Three small contracts in `FSGAP.Abstractions.Simulator`, independent of any simulator library:

| Contract | Responsibility |
|---|---|
| `ISimulatorConnection` | `StartAsync`/`StopAsync`, `Status` (`SimulatorConnectionState` + since + detail), `SessionElapsed`, `WatchStatusAsync` |
| `ISimulatorStateProvider` | `SimulatorState`: `Paused` (a `TelemetryValue<bool>`, so possibly unavailable), `CrashCount`, `LastCrashAt`; `WatchAsync` |
| `IAircraftDetector` | the loaded `AircraftDescriptor` (or `null`); `WatchAsync` |

- Connection states:
  - `Disconnected`: not started or stopped;
  - `Connecting`: first attempt;
  - `WaitingForSimulator`: MSFS absent, retrying;
  - `Connected`;
  - `Reconnecting`: an established connection was lost, retrying;
  - `Faulted`: terminal, no retry.
- **Observation** matches telemetry: an `IAsyncEnumerable<T>` that yields the current value, then each change,
  with "latest value wins" delivery (a slow observer skips intermediate values, never blocks the producer).
  - Cancellation throws `OperationCanceledException`; disposal of the source completes the enumeration.
  - No Rx, no event bus.
- A crash is observed through a monotonic `CrashCount`, so an observer that skipped intermediate states still
  notices it.
- The detector knows no vendor. Recognizing an aircraft stays the job of `IAircraftProvider`.
- `AircraftDescriptor.LiveryFolder` (new) is kept separate from `Livery` (display name). For Fenix it is the key to
  the registration.
- `IInstalledAircraftCatalog` (FSGAP.Abstractions.Aircraft) exposes what is installed locally:
  - lookup by livery folder or registration (the latter ignores case, spaces and hyphens);
  - `RefreshAsync` scan with progress.

  Lookups never scan.
- `FSGAP.Core.Observation.ObservableState<T>` implements the observation pattern once, thread-safely.
  `SimulatorObservationExtensions` adds `WaitForStateAsync` and `WaitForAircraftAsync`.
- `FsgapOptions` carries the host settings:
  - `ApplicationName`;
  - an absolute `DataDirectory`;
  - `Connection.RetryDelay` (5 s);
  - `Telemetry.StaleAfter` (15 s).

  Vendor settings belong to their provider.

## Consequences

- A SimConnect transport can implement all three contracts in one class. Tests use a fake built on
  `ObservableState<T>`.
- The applications' reconnect windows and pilot decisions remain application policy on top of these states.
