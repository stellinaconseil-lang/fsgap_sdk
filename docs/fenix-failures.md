# Fenix failures (FSGAP.Fenix, 0.7.0)

A Fenix session triggers, clears and reads failures through the Fenix EFB backend, in **normalized terms only**:
`FailureKey`, `FailureTarget`, `FailureDefinition`, `AircraftFailure` and `FailureCommandResult`. The Fenix ids, the
titles the EFB requires, the endpoints and the port exist only inside FSGAP.Fenix.

```text
application ── FailureCommand(key, target) ──► session.Failures (FenixFailureProvider)
                                                   │ key → internal entry (Fenix id + title), or NotSupported
                                                   ▼
                                              FenixEfbClient ── HTTP ──► Fenix EFB (127.0.0.1:8083)
                                                   │ echo → confirmed, else read-back of the live list
                                                   ▼
                                              FailureCommandResult (Succeeded / Unavailable / Unconfirmed / ...)
```

## Enabling it

> Provider-level API. Applications do not compose providers: they use `FsgapRuntime` (package `FSGAP`), which composes
> this provider internally (see the README and [architecture.md](architecture.md#fsgap-is-a-multi-aircraft-sdk-0120-preview2)).

```csharp
var provider = new FenixAircraftProvider(
    catalog,
    genericTelemetry: simulator.Telemetry,
    simulatorVariables: simulator,
    aircraftDetector: simulator.AircraftDetector,
    fenixOptions: new FenixOptions());          // enables failures; optional efbHttpClient for tests
```

- **Without `fenixOptions`** a session supports no failure, as in 0.6.0: `FailureCapabilities.None` and
  `NotSupported`, with no HTTP.
- **No SimConnect.** The EFB is its own transport and never uses SimConnect; commands still work while the simulator
  connection is down.

| `FenixOptions` | Default | Meaning |
|---|---|---|
| `EfbBaseAddress` | `http://127.0.0.1:8083/` | EFB origin. The IPv4 literal is deliberate: `localhost` tries `::1` first and cost about 2 s per call. Another host is the integrator's responsibility; there is no authentication. |
| `RequestTimeout` | 3 s (at most 30 s) | Per request, measured on the injected clock. |
| `ConfirmationReadbacks` | 3 | Read-backs when the echo does not confirm the state. |
| `ConfirmationDelay` | 400 ms | Between two read-backs. |

**HTTP client.**
- One `HttpClient` per `FenixAircraftProvider`, created on first use and shared by every session.
- It uses a `SocketsHttpHandler` with a 2-minute pooled connection lifetime and has no global timeout.
- There is no client per call, so no socket exhaustion.
- An injected client (tests use a fake `HttpMessageHandler`) is never disposed by FSGAP.

## EFB protocol (audited, unofficial)

The protocol was reverse-engineered by FSHANGAR from the EFB's own JavaScript and verified live. See
`fenixhangarclient/windows-client/research/FENIX_FAILURE_PROTOCOL.md`.

| Call | Use |
|---|---|
| `POST /fenix/failures/saveManual`, body `{"id","title","failureCondition":null,"failed":true\|false}` | trigger (`true`) / clear (`false`). The title must be Fenix's own. The answer echoes the saved object. |
| `GET /fenix/failures/manual` → `atas[].groups[].failures[]{id,title,failureCondition,failed}` | the live state of every manual failure (384) |

- `failureCondition` (Fenix's conditional arming by IAS or altitude) is never used.
- No other endpoint is called; `/fenix/failures/arrival` was never understood, and a test forbids it.

## Catalogue and key policy

- **Raw catalog.** `fenix-failure-catalog.json` holds 384 entries in 19 ATA chapters. It was copied verbatim from
  FSHANGAR, which captured it from the EFB.
  - It is embedded in FSGAP.Fenix and versioned with it.
  - It was checked **identical to the live EFB** on 2026-09-24.
  - It provides the title each command needs and the ATA chapter used to classify an active failure.
- **Normalized keys: 384 of 384 (since 0.12.0-preview.4).** `fenix-failure-mapping.json` gives every EFB failure one
  normalized `FailureKey`.
  - Before 0.12.0-preview.4: 40 of 384, the failures consumers used first (24 FLIPPP scenario ids, FSHANGAR's live
    verification, tests and fire-probe trials, the real ids referenced by fenixhangarweb). Those 40 keep their keys,
    display names, categories and targets unchanged (a test compares them with the preview.3 mapping, verbatim).
  - BLOCK 12.2 normalized the other 344 by the same policy, because FSHANGAR plays all 384: an application must never
    need a raw Fenix id.
  - The full table is in [fenix-failure-mapping.md](fenix-failure-mapping.md); a versioned export for consumers that
    still store raw ids is in `docs/exports/`.
  - An active failure absent from the embedded catalog (for example after a Fenix update) is still reported, with
    `Key = null`.
- **The keys are a contract.** `tests/FSGAP.Fenix.Tests/Contract/failure-keys.txt` lists the 384 keys; renaming or
  removing one fails the tests, adding one is a deliberate edit of that file. A new aircraft provider reuses these
  keys where the meaning applies and never renames them.
- **Key rules.**
  - The first segment is the system domain of the ATA chapter: `air-conditioning` (21), `auto-flight` (22),
    `communications` (23), `electrical` (24), `fire` (26), `flight-controls` (27), `fuel` (28), `hydraulic` (29),
    `ice-rain` (30), `indicating` (31), `landing-gear` (32), `navigation` (34), `oxygen` (35), `pneumatic` (36),
    `information` (46), `apu` (49), `doors` (52), `engine` (70 and the start faults of 80).
  - Then, in this order and only when the catalog distinguishes them: the component, the instance (number, side,
    hydraulic colour, position `captain` / `fo` / `standby`), and the failure type. For example
    `hydraulic.blue.leak`, `engine.2.reverser.unlocked`, `ice-rain.static-heat.fo.left`,
    `fuel.inner-tank.high-temperature.advisory`, `flight-controls.sec.3.resettable-fault`.
  - Fenix engine 1 is the left engine, engine 2 the right one ("Left surge" is `engine.1.surge`).
  - No vendor id, no `fenix`, no Fenix spelling, no counter: every key is semantic. A test checks this for every key.
- **Targets.** Each key has exactly one target, and the instance is already in the key.
  - The typed kinds are used only where they fit exactly: `Engine(n)` for `engine.n.*` and `fire.engine.n.*`;
    `Apu` for `apu.*`, `fire.apu.*` and the APU generator; `InertialReference(n)` for `navigation.ir.n.*`;
    `FuelPump` for `fuel.pump.*`; `HydraulicSystem` for `hydraulic.<colour>.*`; `ElectricalBus` for
    `electrical.bus.*`. A test checks this policy for all 384.
  - Target ids are the telemetry ids (`left-1`, `blue`...).
  - Anything else targets `Aircraft` rather than a false precision. For example, generator 1 is electrical, not the
    engine; the bleed valve belongs to the pneumatic system.
- **Display names.** Written by hand: for example "Cabin pressure controller 1" rather than "CPC 1", "Engine 2
  reverser inhibited by maintenance" rather than "Right reverser inhibited by maint".
- **Categories.** They come from the ATA chapter.
- **Operations.** Every key supports trigger and clear.

## Commands

**Flow.**
1. Look up the key; an unknown key gives `NotSupported`, with no HTTP.
2. The operation must be allowed and the target must be the key's own target; otherwise `NotSupported`, with no HTTP.
3. If the detector reports another aircraft: `Unavailable`, with no HTTP.
4. `saveManual`.
5. Echo check, then read-backs if needed.
6. Result.

| Situation | Result | Retry? |
|---|---|---|
| Echo, or a read-back, shows the requested state | `Succeeded` | — |
| Connection refused, host not found (no Fenix loaded) | `Unavailable` (**new status**) | safe later |
| No answer within the timeout, exchange broken, no confirmation after the read-backs, session ended mid-command | `Unconfirmed` (**new status**) | read the active failures first |
| HTTP 4xx | `Rejected` | — |
| HTTP 5xx | `Failed` | — |

- **Never retried.** A command is never retried automatically: the EFB may have applied it.
- **Read-backs.** The echo check falls back to reading the list back, because some failures (the tyre pressures) echo
  a stale state.
- **Messages.** Result messages name the key, never the Fenix id.

## Active failures

- `GetActiveFailuresAsync` reads the EFB's live list. **Nothing is inferred from the commands sent**, and there is no
  cache.
- **Which entry becomes what:**
  - an active entry with a key is reported with its key, target, category and display name;
  - an active entry known to Fenix but without a key is reported with `Key = null`, its ATA category and its Fenix
    title as `Description`. It is never dropped;
  - an active entry unknown to the embedded catalog (a Fenix update) is reported with `Key = null` and logged with
    its id;
  - an active entry without an id is reported as "Unidentified failure";
  - the same failure listed twice counts once.
- **When the answer cannot be trusted:**
  - an entry whose state is missing or not a boolean, an unrecognized shape, an unreachable EFB, an HTTP error or a
    timeout all throw **`FailuresUnavailableException`** (a new neutral exception);
  - the contract says an empty list always means "no failure", so an uncertain read never returns empty.
- `CanReadActiveFailures = true`: the list gives the live state, and this was verified live.

## Capabilities and availability

- **Structural.** `FailureCapabilities.Catalog` holds the 384 definitions and `CanReadActiveFailures` is true.
  - They are declared whenever `fenixOptions` is set, even while the EFB is offline: the catalog does not change
    with availability.
  - Declaring them contacts nothing.
- **Runtime.** EFB availability comes out of each call (`Unavailable` / `FailuresUnavailableException`).
  - There is no separate availability API: no consumer needed one, and the proposal (G-F3) stays open.
  - A read that fails is logged once when it starts failing and once when it recovers.

## Lifecycle and threading

- **One provider per session.**
  - It is created only by `AttachAsync`, which accepts only a recognized Fenix. PMDG, Asobo, iniBuilds and C172
    therefore never produce an EFB request.
  - It is disposed with the session.
- **Order.** Commands of a session run **one at a time, in issue order**, so a trigger followed by a clear of the
  same failure reaches the EFB in that order. Reads run in parallel.
- **Aircraft change.** Once the detector reports another aircraft, the old session's commands return `Unavailable`
  and its reads throw `FailuresUnavailableException`, without HTTP. The new session starts clean.
- **Dispose.**
  - It cancels in-flight calls.
  - A command already sent returns `Unconfirmed`.
  - A command still waiting its turn, and an in-flight read, throw `ObjectDisposedException`.
  - Later calls throw `ObjectDisposedException`.
- **Caller cancellation** throws `OperationCanceledException`. The outcome of a command cancelled after sending is
  unknown.

## Limitations

- **The EFB API is unofficial and version-specific.** A Fenix update can change it. The catalog was checked
  identical on 2026-09-24; re-run that comparison after an update.
- **Asynchronous application (observed live, 2026-09-24).** A clear sent **30 ms** after its trigger was confirmed
  by the echo and by an immediate read-back. At the next independent check, moments later (seconds at most), the
  failure was active again:
  Fenix applied the trigger late and overwrote the clear.
  - With **5 s** between trigger and clear, the clear held (checked 5 s and about 40 s later).
  - FSGAP's `Succeeded` therefore means "the EFB confirmed the state now".
  - Do not toggle the same failure within a fraction of a second, and read the active failures to verify a
    critical state.
  - The exact safe interval is unknown; no timing rule was invented from a single observation.
- **Manual failures only.** `failureCondition` is not supported, and neither is Fenix's random-failure mode.
- **Persistence across an aircraft reload is not known.** It is not documented and was not tested.

## Live validation (2026-09-24, MSFS 2024, Fenix A319 C-GBIA parked, engines running)

| Operation | Validation | Result |
|---|---|---|
| EFB reachability, catalog, active failures (read-only) | LIVE TEST | reachable (~80 ms), 40 keys declared, 0 active; live list of 384 ids **identical** to the embedded catalog |
| Trigger `air-conditioning.cpc.1` (Fenix `F_PNEUMATIC_CPC_1`, the failure FSHANGAR used for its own first live round trip; reversible, CPC 2 takes over) | LIVE TEST | `Succeeded`; read-back 1 active, `air-conditioning.cpc.1` |
| Clear, 30 ms after the trigger (first run) | LIVE TEST | `Succeeded` and read-back 0. **Found active again at the next check, moments later** (asynchronous application, see Limitations); cleared with the same payload directly, then stable for 20 s |
| Trigger, hold 5 s, clear (second run) | LIVE TEST | trigger `Succeeded`, clear `Succeeded`, 0 active right after, 5 s after, and about 40 s after |
| Failures left active after the tests | LIVE TEST | **0** |
| Timeout, refusal, HTTP errors, malformed answers, dispose, concurrency, other aircraft | AUTOMATED ONLY | fake EFB (`HttpMessageHandler`) |

The sample runs it with `--failures` (read-only) or `--failure-roundtrip <key>` (trigger, hold 5 s, clear, read back
after 5 s). The round trip refuses to touch a failure that is already active.
