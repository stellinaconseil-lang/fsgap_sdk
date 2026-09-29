# Installed aircraft liveries (FSGAP.SimConnect, since 0.10)

`SimConnectSimulator` implements `IInstalledLiveryService`: every (aircraft title, livery name) pair MSFS 2024 can load,
as the simulator itself enumerates it. Generic, read-only, and on **the transport's single native connection**.

```csharp
IInstalledLiveryService liveries = simulator;
IReadOnlyList<InstalledLivery> rows = await liveries.GetInstalledAircraftLiveriesAsync();
// rows[i].AircraftTitle  e.g. "A220-300 - No Cabin"
// rows[i].LiveryName     e.g. "Air France A220-300", or null for an unnamed default livery
```

It exists because the local file system cannot list everything the simulator can load: marketplace aircraft and liveries
are streamed `.fsarchive` content, unreadable on disk. The BLOCK 10A.5 audit showed the simulator lists them all
([audits/synaptic-a220-livery-discovery.md](audits/synaptic-a220-livery-discovery.md)).

## What it provides, and what it does not

| Provided | Not provided (provider-specific) |
|---|---|
| `AircraftTitle`: the container title (`title` of a preset in `aircraft.cfg`) | registration |
| `LiveryName`: the `name` of the livery's `livery.cfg`; `null` when empty | operator |
| | livery folder |
| | vendor, model, preset or cabin interpretation |
| | deduplication of a livery offered under several presets |

Recognizing which rows belong to an aircraft family, merging the same livery under several presets, and resolving a
registration (for example from a livery file, or a folder learned when the user loads the livery) belong to the
aircraft providers. The generic service never filters or merges.

## Public API (FSGAP.Abstractions)

| Type | Content |
|---|---|
| `IInstalledLiveryService` | `GetInstalledAircraftLiveriesAsync(ct)`: every row, in the simulator's order, duplicates included |
| `InstalledLivery` | `AircraftTitle` (never empty), `LiveryName` (`null` when the simulator reports an empty name) |
| `SimulatorServiceException` | shared with the airport service: `SimulatorUnavailable` (not connected, lost, stopped) or `QueryFailed` (rejected, incomplete in time, malformed) |

| Case | Result |
|---|---|
| The simulator answers with no row | empty list |
| Duplicate rows | all returned |
| Same livery name under two titles (two presets) | both returned |
| Row with an empty aircraft title | dropped (names no aircraft) |
| Row with an empty livery name | returned with `LiveryName = null` |
| Malformed or truncated packet, inconsistent packet count, repeated packet | `QueryFailed` |
| Answer incomplete after 10 s | `QueryFailed` (an incomplete list is never returned) |
| Not connected, connection lost or transport stopped during the request | `SimulatorUnavailable` |
| Caller cancellation | `OperationCanceledException` for that caller only |
| After `DisposeAsync` | `ObjectDisposedException` |

## Native mechanism

- **Native call.** `SimConnect_EnumerateSimObjectsAndLiveries(handle, requestId, SIMCONNECT_SIMOBJECT_TYPE_AIRCRAFT)`.
  SimConnect.NET 0.2.2 does not bind it (it only defines the receive id), so `LiveryInterop` declares a plain P/Invoke
  of the documented `SimConnect.dll` export.
- **Same connection, same thread discipline.** The call runs on the library's dispatcher through
  `FacilityInterop.InvokeNativeAsync`, serialized with its message loop, exactly like the airport list. The one piece of
  reflection into SimConnect.NET stays in `FacilityInterop` ([ADR 0004](decisions/0004-simconnect-layer-and-reflection.md)).
  **Native connections: 1.**
- **Answer.** Packets arrive through the public `RawMessageReceived` event. The handler reads the request id from native
  memory and copies only this request's packets, because the pointer is valid during the callback only. A
  `LiveryListAssembly` completes when packets 0 … `dwOutOf` − 1 have all arrived, in any order.
- **One request at a time per connection.** Concurrent callers share the request in flight. Nothing is cached once it
  has answered.

### Packet layout

| Fact | Status |
|---|---|
| `SIMCONNECT_RECV` (`dwSize`, `dwVersion`, `dwID`) + `SIMCONNECT_RECV_LIST_TEMPLATE` (`dwRequestID`, `dwArraySize`, `dwEntryNumber` 0 … `dwOutOf` − 1, `dwOutOf`) | OFFICIAL (MSFS 2024 SDK) |
| Element `SIMCONNECT_ENUMERATE_SIMOBJECT_LIVERY` = `SIMCONNECT_STRING(AircraftTitle, 256)` + `SIMCONNECT_STRING(LiveryName, 256)` = 512 bytes | OFFICIAL |
| The seven header DWORDs are packed: the first element starts at byte 28 | EMPIRICALLY CONFIRMED (BLOCK 10A.5, 5 runs) |
| A packet can hold more 512-byte slots than `dwArraySize` (79 announced, 80 present); extra slots are ignored | EMPIRICALLY CONFIRMED |
| Strings are NUL-terminated ASCII in practice; decoded as UTF-8 with replacement, trimmed | EMPIRICALLY CONFIRMED |

The parser checks every bound before reading. A packet is rejected (`FormatException`, then `QueryFailed`) when:

- it is shorter than 28 bytes or than its declared size;
- it is not message 38;
- its payload is not a whole number of 512-byte slots;
- it announces more elements than it holds, or more than 4 096;
- its packet number is outside 0 … `dwOutOf` − 1, or `dwOutOf` exceeds 100 000.

A field without a terminator is read within its 256 bytes, never beyond.

## Performance

Live (BLOCK 10A.5, MSFS 2024 1.8.16.0): **15 815 rows in 201 packets, 60–73 ms**, one native request. The production
implementation differs from the discovery harness in three ways:

- it skips other requests' packets before copying them;
- it no longer keeps diagnostic header copies;
- it enforces the stricter layout.

It keeps one copy per packet, which is unavoidable because the native buffer lives only during the callback. The
10 s timeout leaves two orders of magnitude of margin.

## Not in this service

AI object creation (`SimConnect_AICreateNonATCAircraft_EX1`, `SimConnect_AIRemoveObject`) remains an **internal,
experimental** diagnostic of the live sample (`AiProbeInterop`, BLOCK 10A.5). It is not a production technique and has
no public API.
