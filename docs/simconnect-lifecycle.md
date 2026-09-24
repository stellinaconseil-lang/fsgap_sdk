# FSGAP.SimConnect — transport and lifecycle

`FSGAP.SimConnect` (since 0.3.0) is the generic Microsoft Flight Simulator transport. It implements the simulator
contracts of FSGAP.Abstractions ([ADR 0008](decisions/0008-simulator-abstractions.md)). It is **vendor-neutral**: it
reports what MSFS says and never recognizes a developer, a model or an engine, which is the job of the aircraft
providers. Since 0.5.0 it also reads the generic flight telemetry on the same connection
([generic-telemetry.md](generic-telemetry.md)).

## Public API

A single public type:

```csharp
public sealed class SimConnectSimulator : ISimulatorConnection, ISimulatorVariableReader   // + IAsyncDisposable
{
    public SimConnectSimulator(FsgapOptions options, ILogger<SimConnectSimulator>? logger = null, TimeProvider? timeProvider = null);

    public SimulatorConnectionStatus Status { get; }
    public TimeSpan SessionElapsed { get; }
    public ISimulatorStateProvider State { get; }        // pause, crashes
    public IAircraftDetector AircraftDetector { get; }   // loaded aircraft
    public ITelemetryProvider Telemetry { get; }         // generic flight telemetry (0.5.0)

    // 0.6.0: read-only, batched read of named variables on this connection (used by aircraft providers)
    public Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default);

    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync(CancellationToken cancellationToken = default);
    public IAsyncEnumerable<SimulatorConnectionStatus> WatchStatusAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}
```

- No SimConnect.NET type appears in the public API. A test enforces it.
- Dependencies: `FSGAP.Abstractions`, `FSGAP.Core`, `SimConnect.NET 0.2.2` (the version used by FSHANGAR and FLIPPP),
  and `Microsoft.Extensions.Logging.Abstractions` 8.0.
- It never references a provider assembly.

## Internal architecture

```text
SimConnectSimulator                 lifecycle loop, state publication (testable, no SimConnect types)
   |-- SimulatorStateSource         ObservableState<SimulatorState>   (pause, crashes)
   |-- AircraftDetectorSource       ObservableState<AircraftDescriptor?>
   '-- ISimConnectSessionFactory    internal seam
          '-- ISimConnectSession    one open native connection (events + identity read)
                 '-- SimConnectNetSession / SimConnectNetSessionFactory   the only SimConnect.NET code
```

The tests replace the internal seam with a scriptable fake, so almost the whole lifecycle runs without MSFS. The
fake covers:

- simulator absent, successful connection, loss;
- unexpected and terminal errors;
- pause and crash events, identity changes, read failures;
- late callbacks after a connection has ended.

A smoke test also runs the real library. It passes with or without MSFS.

## Connection lifecycle

```text
                StartAsync
Disconnected ──────────────> Connecting
     ^                        |      \
     |              MSFS up   |       \ MSFS absent / transient error
     |                        v        v
     |                    Connected  WaitingForSimulator ──(retry every RetryDelay)──┐
     |                        |   ^        |                                         |
     |      connection lost   |   |        '──────────── MSFS up ──> Connected <─────┘
     |                        v   | MSFS back
     |                    Reconnecting ──(retry every RetryDelay, stays Reconnecting while MSFS is away)
     |
     '─── StopAsync (from any state)
Any attempt ── native library unusable (DllNotFound / BadImageFormat / EntryPointNotFound) ──> Faulted
Faulted ── StopAsync ──> Disconnected ── StartAsync ──> Connecting ...
```

| Transition | Trigger |
|---|---|
| Disconnected → Connecting | `StartAsync` |
| Connecting → Connected | Native open succeeded. The Crashed and Pause_EX1 subscriptions are then attempted. |
| Connecting → WaitingForSimulator | MSFS not running (`SimConnectException`), or an unexpected error (detail = its message) |
| WaitingForSimulator → Connected | A later attempt succeeds |
| Connected → Reconnecting | The library reports the disconnection, or an identity read fails while the library says the connection is closed (safety net) |
| Reconnecting → Connected | A later attempt succeeds. The state stays Reconnecting while MSFS is away. |
| any → Faulted | The native SimConnect library cannot be loaded. There is no retry. |
| any → Disconnected | `StopAsync` or `DisposeAsync` |

**Fault semantics.**

- MSFS absent, connection refused, connection lost and transient native errors are **normal lifecycle** and are
  retried.
- `Faulted` is reserved for failures that retrying cannot fix: `DllNotFoundException`, `BadImageFormatException` and
  `EntryPointNotFoundException` (the native library is missing or has the wrong architecture).
- An unexpected crash of the loop itself also ends in `Faulted`, with a critical log.
- A faulted connection is restarted with `StopAsync` + `StartAsync`.

## Retry and reconnection

- The delay between attempts is `FsgapOptions.Connection.RetryDelay` (default 5 s). No other retry constant
  exists.
- The delay is scheduled on the injected `TimeProvider`, so tests control time with a fake clock.
- After a loss, the first reconnection attempt also waits one `RetryDelay`.
- The library's own auto-reconnect is disabled (`AutoReconnectEnabled = false`). The FSGAP loop is the only
  reconnect path.

## Resource disposal

| Risk | Protection |
|---|---|
| Client leak on a failed attempt | `SafeOpen.OpenAsync` disposes the `SimConnectClient` if `ConnectAsync` throws or is cancelled. This fixes the leak found in the audited apps. |
| Reconnect leak / duplicate client | A connection is always disposed before the next attempt is opened. Tests check that at most one connection is alive at any time. |
| Double Start | `StartAsync` while started, including while Faulted, does nothing. Start/Stop/Dispose are serialized by one lock. |
| Double Stop | `StopAsync` while stopped does nothing. `DisposeAsync` is idempotent. |
| Stale callbacks | Native handlers are detached when a connection ends. A per-connection guard also ignores any late crash or pause callback from a closed connection. |
| Disposed client callbacks | Handlers are removed before `SimConnectClient.DisposeAsync`. The session adapter disposes only once. |
| Stop during a pending open | The open is cancelled and the loop ends. |

`StopAsync`:

- cancels the loop (retry delays, identity polling, pending open);
- detaches the handlers;
- disposes the native connection;
- resets the simulation state and the aircraft to `null`;
- resets `SessionElapsed` to zero;
- publishes `Disconnected`.

It is safe in every state: never connected, connecting, connected, just lost, reconnecting, faulted.

## Threading model

- **Ownership:** one background task, started by `StartAsync`, owns the native connection: it opens, subscribes,
  polls and disposes it. No other code touches it.
- **Native thread:** SimConnect.NET raises connection, crash and pause events on its message-processing thread.
  The handlers only update immutable state under a short lock and return. There is no I/O, no blocking and no
  consumer code.
- **Identity reads:** they continue with `ConfigureAwaitOptions.ForceYielding`, so the mapping and publication never
  run on the native thread, whatever the library does with its continuations.
- **Publication:** state goes into `ObservableState<T>` (FSGAP.Core). `Set` only writes to bounded single-slot
  channels in "drop oldest" mode, and never waits.
- **Consumer notification:** consumers read those channels in their own `await foreach`. Channel continuations run
  asynchronously (never inline in the writer), so consumer code **never runs on the native thread**. A test proves
  it.
- **Slow consumer:** it can never block the transport. It skips intermediate values and always receives the
  latest one. Crashes are never lost, because `CrashCount` only increases.
- **Failing consumer:** an exception only ends that consumer's enumeration. The transport, the retries and the
  other observers are unaffected. Tests cover it.

## Simulation state

| Field | Source | Semantics |
|---|---|---|
| `Paused` | SimConnect `Pause_EX1` (`SystemEventEx1Received`, `Data0 != 0` means paused, as in the audited apps; the bit layout is unconfirmed) | `Unavailable` when disconnected or if the subscription failed; `Unknown` once connected until the first notification; then `Known(value, observedAt)`. A repeated identical notification changes nothing. |
| `CrashCount` | SimConnect `Crashed` (`SystemEventReceived`) | +1 per crash. Monotonic for a session (StartAsync → StopAsync), kept across reconnections, reset by Stop. |
| `LastCrashAt` | same | Time of the most recent crash, from the injected clock |

MSFS only notifies pause *changes*. After connecting, `Paused` therefore stays `Unknown` until the first pause or
resume.

## Aircraft detection

**SimVars:** one batched request of 4 `String256` SimVars for the user aircraft (object 0), with the names proven in
FSHANGAR/FLIPPP:

| SimVar | `AircraftDescriptor` field |
|---|---|
| `TITLE` | `Title` |
| `ATC ID` | `Registration` |
| `LIVERY FOLDER` | `LiveryFolder` |
| `LIVERY NAME` | `Livery` |

**Normalization:**

- values are trimmed, and empty values become `null`;
- an empty `TITLE` means "no aircraft loaded" (descriptor `null`), as in the audited apps;
- nothing is inferred (no manufacturer, model, ICAO type or engine).

**Cadence:**

- the first read happens immediately after connecting;
- reads then run every **2 s** while the aircraft is changing or unknown;
- after **3 consecutive identical reads** the aircraft is stable and reads run every **5 s**;
- any change, or a read failure, returns to 2 s;
- worst-case detection delay of an aircraft change: about 5 s.

**Change detection:**

- a new value is published only when the descriptor differs by value. Title, ATC id, livery folder and livery name
  all count, so a new livery of the same aircraft is a change;
- identical polls notify nobody;
- a lost connection publishes `null`.

**Read failures:** a failed read is logged once per failure streak (with a matching "recovered" message), and polling
continues. If the library also reports the connection closed, the failure counts as a loss.

## Logging

- `Microsoft.Extensions.Logging.Abstractions` `ILogger`; nothing is logged without a logger.
- **Information:** first connection attempt, connected, waiting for the simulator, stopped, aircraft changes,
  crashes.
- **Warning:** connection lost, unexpected connection error (once per distinct message), failed subscriptions,
  identity-read failure streaks.
- **Error / Critical:** Faulted.
- **Debug:** individual retry attempts.
- Polls are never logged individually. A test checks that 100 retries while MSFS is absent produce at most 2
  informative entries.

## Live validation procedure

Tool: `samples/FSGAP.SimConnect.Console`. It prints the status, the pause/crash state and the loaded aircraft until
Ctrl+C.

```bash
dotnet run --project samples/FSGAP.SimConnect.Console              # until Ctrl+C
dotnet run --project samples/FSGAP.SimConnect.Console -- --minutes 10
```

| Scenario | Steps | Expected |
|---|---|---|
| A | MSFS running, start the sample | `Connected`, aircraft line with title / ATC id / livery folder / livery |
| B | Start the sample without MSFS, then launch MSFS | `WaitingForSimulator`, then `Connected` automatically |
| C | Connected, quit MSFS, relaunch | `Reconnecting` (stays while MSFS is away), then `Connected` |
| D | Pause, then resume in MSFS | `paused=True`, then `paused=False` |
| E | Trigger a crash (crash detection on) | `crashes=1`, `lastCrash` set |
| F | Leave the sample waiting without MSFS for about 10 minutes | Only 2 informative log lines, no growth in memory or handles |

The results of the BLOCK 3 validation are recorded in [audits/fenix-extraction-plan.md](audits/fenix-extraction-plan.md)
(BLOCK 3 section).

## Telemetry groups (implemented in 0.5.0)

The generic telemetry reads three SimVar groups on the same connection, each on its own loop and cadence:

- **FAST**: every 1 s, including position at 1 Hz per
  [ADR 0005](decisions/0005-position-update-rate.md);
- **NORMAL** (gear, flaps, speed brake): every 2 s;
- **SLOW** (engines): every 5 s.

Each read is one batched one-shot request (`ISimConnectSession.ReadTelemetryGroupAsync`), rather than the native
periodic subscription planned in BLOCK 3. Polling keeps the same session seam, the same cancellation and the same
fake-clock testability as the identity poll, at the cost of one request per read. Request structs stay
`internal`, which requires the
`InternalsVisibleTo("SimConnect.NET")` described below.

## Known library constraint

SimConnect.NET 0.2.2 unmarshals request structs through `dynamic` over generic types closed on the struct type.
With an **internal** struct, those types are inaccessible from SimConnect.NET's call sites, and the runtime binder
fails with *"'object' does not contain a definition for 'OffsetBytes'"*.

- This was found in live validation; unit tests could not reveal it.
- FSGAP grants `InternalsVisibleTo("SimConnect.NET")` (the library is not strong-named) rather than making request
  structs public API.
- A test guards this.
