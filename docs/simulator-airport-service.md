# Simulator airport service (FSGAP.SimConnect, 0.8.0)

`SimConnectSimulator` implements `IAirportService`: the airports the simulator knows near a coordinate, nearest
first. It is the first simulator service: generic (no aircraft logic, it works the same for a C172, a PMDG or a Fenix),
read-only, and on **the transport's single native connection**.

```csharp
IAirportService airports = simulator;
var flight = (await simulator.Telemetry.GetSnapshotAsync()).Flight;
if (flight.LatitudeDegrees.TryGetValue(out var lat) && flight.LongitudeDegrees.TryGetValue(out var lon))
{
    AirportInfo? nearest = await airports.FindNearestAirportAsync(new GeoPosition(lat, lon));   // null: none in range
    IReadOnlyList<AirportInfo> around = await airports.FindNearbyAirportsAsync(
        new GeoPosition(lat, lon), new AirportSearchOptions { MaxDistanceNauticalMiles = 20, MaxResults = 5 });
}
```

The service answers "which airports are near this point". Whether that is the departure, the arrival or anything
else is the application's decision. There is no flight-phase logic.

## Public API (FSGAP.Abstractions)

| Type | Content |
|---|---|
| `IAirportService` | `FindNearbyAirportsAsync(position, options?, ct)` returns a nearest-first list; `FindNearestAirportAsync(position, options?, ct)` returns an `AirportInfo` or `null` |
| `GeoPosition` (`Geography`) | latitude/longitude in degrees, validated (±90 / ±180, finite); `DistanceNauticalMilesTo`; `TryFrom` for telemetry readings |
| `AirportInfo` | `Icao`, `Region?`, `Position`, `ElevationFeet?`, `DistanceNauticalMiles`: only what the simulator provides |
| `AirportSearchOptions` | `MaxDistanceNauticalMiles` (default **50 NM**, at most 1,000), `MaxResults` (default **10**, at most 1,000). The defaults are defined here only. |
| `SimulatorServiceException` + `SimulatorServiceError` | `SimulatorUnavailable` (not connected, lost, stopped) or `QueryFailed` (rejected, timeout, unreadable answer) |

Outcomes are distinct:
- an empty list or `null` means the simulator answered and nothing is within range;
- an exception means it could not be asked or did not answer usably;
- `ObjectDisposedException` after disposal.

**Not provided** (not in the source): name, city, country, runways, parking, frequencies, magnetic variation.

## Native mechanism

- **Legacy audit** (FSHANGAR `SimConnectDataSource.FindNearestIcaoAsync`; FLIPPP has the same code, not in
  production).
  - **Native call.** `SimConnect_RequestFacilitiesList_EX1(handle, AIRPORT, requestId)` is reached **by reflection**:
    SimConnect.NET 0.2.2 declares it on its internal `SimConnectNative` class but exposes no public method that
    requests a facility list.
  - **Answer.** It arrives as `SIMCONNECT_RECV_AIRPORT_LIST` through the public `RawMessageReceived` event. The
    bytes are copied inside the callback, since the pointer is valid only then.
  - **Parsing.** A 64-byte header was observed on MSFS 2024 (28 documented, plus 36 unidentified bytes), followed by
    36-byte `SIMCONNECT_DATA_FACILITY_AIRPORT` entries: `ident[9]`, `region[3]`, then latitude, longitude and altitude
    as doubles.
  - **Selection.** Strict ASCII identifiers only, then the nearest by haversine (km), with no radius.
  - **A second connection, and why.** The first version called the native function on the **main** client's handle
    from its own thread. It raced the library's message loop, and every other request failed with E_FAIL for the
    rest of the session. FSHANGAR then moved the lookup to a **second, short-lived connection**
    (`"FSHangar.FacilityLookup"`), one per lookup.
- **FSGAP.**
  - **Same native request, same connection.** `FacilityInterop` calls it through
    `SimConnectClient.InvokeNativeAsync<T>`, the internal method the library uses for its own native calls. That
    method runs the call **on the library's dispatcher, serialized with its message loop**, which removes the race
    without a second connection.
  - **SimConnect native connections: 1.** Live: 13 lookups over 2.3 minutes, with the telemetry and the Fenix
    polling uninterrupted.
  - **Answer.** It is read through the public `RawMessageReceived`. Packets are filtered by a unique request id, and
    all of them are gathered (`EntryNumber` 0 to `OutOf-1`), whereas FSHANGAR read only the first.
  - **Guards.** One airport request at a time per connection, and a 5 s timeout.
  - **Parsing** (`AirportListParser`): managed bytes only, with no unsafe access.
    - The header size is derived from the packet size and must be **28 or 64**. Anything else is rejected
      (`QueryFailed`) rather than read at a guessed offset.
    - Entries whose identifier is not ASCII text are skipped.

### Reflection boundary (ADR 0004)

| | |
|---|---|
| Class | `FSGAP.SimConnect.Native.FacilityInterop` (internal): the only reflection into SimConnect.NET internals |
| Members | `SimConnect.NET.SimConnectNative.SimConnect_RequestFacilitiesList_EX1(IntPtr, uint, uint) : int` (static, internal P/Invoke); `SimConnect.NET.SimConnectClient.InvokeNativeAsync<T>(Func<IntPtr, T>, CancellationToken) : Task<T>` (internal) |
| Library | SimConnect.NET **0.2.2**, pinned in `Directory.Packages.props` |
| Guard tests | the members exist with these signatures; the referenced library is 0.2.2 (an upgrade fails this test first); another assembly yields a clear "not compatible … verified with 0.2.2" error and no `NullReferenceException`; no other type holds these names |
| Failure mode | resolution happens once. When it fails, every search throws `SimulatorServiceException(QueryFailed)` with that message; the rest of the transport is unaffected |

## Search, distance, duplicates

1. **Normalization.**
   - The identifier is trimmed and upper-cased; it is never invented, and an empty one is dropped.
   - The region is trimmed and upper-cased; an empty region becomes `null`.
   - The altitude arrives in meters and is converted to feet.
   - Invalid coordinates are dropped.
2. **Distance.** Great-circle (haversine, R = 3440.0695 NM), in **nautical miles**. It is correct across ±180° and
   near the poles (tested).
3. **Duplicates.** For the same identifier, the entry nearest to the searched point is kept; on a tie, the lower
   latitude, then the lower longitude. The result is deterministic.
4. **Filtering and sorting.** Entries are filtered by radius (inclusive) and sorted by distance, then identifier.
   The list is then cut to `MaxResults`.

**Cache.**
- The raw list is kept **10 s per connection**, and concurrent searches share one native request.
- A caller's cancellation only stops that caller's wait.
- A failed request is not cached, and a new connection starts empty.
- There is no persistent airport database, navdata or scenery index: the simulator is the source.

## Threading and lifecycle

- The native callback only copies and parses bytes, and completes a task with asynchronous continuations. Nothing
  runs on the message thread.
- The request goes through the library's dispatcher, the same path as every other SimConnect.NET call. Telemetry,
  identity polling and Fenix polling keep running during a search (tested and verified live).
- **Lifecycle:**

| Situation | Result |
|---|---|
| Not started, simulator absent | `SimulatorServiceException(SimulatorUnavailable)` without any request |
| Connection lost during a search | `SimulatorUnavailable` (the request is bound to the connection's lifetime) |
| Reconnection | the new connection answers; the old cache is ignored |
| `StopAsync` during a search | `SimulatorUnavailable` |
| After `DisposeAsync` | `ObjectDisposedException` |

- **Fenix failure calls are independent.** They go over HTTP to the EFB and share no lock or connection with this
  service.

## Performance

- **Cost.** 1 native request per search, or 0 while the cache is fresh.
- **Measured live** (MSFS 2024, Nice): 31–42 ms per search, including the request. The bubble held **295 airports**
  within 109 NM.
- **FSHANGAR by comparison** opened and closed a connection for every lookup.

## Parity with FSHANGAR

- **Algorithm, on live data.** FSHANGAR's `NearestAirportMath` was compiled **as-is** (read-only link, outside both
  repositories) and run on the live bubble list.
  - The points were the parked position, Cannes, 40 airport positions, and 2,000 random points around Nice.
  - Result: **2,043 of 2,043 identical nearest airports**, with distances equal to 4 decimals (0.9603 km).
- **Unit parity test.** The same rule is ported verbatim into the tests and compared on 50 random bubbles × 20
  points.
- **Differences, all deliberate:**
  - FSGAP applies a radius (50 NM by default; FSHANGAR had none);
  - FSGAP de-duplicates identifiers deterministically;
  - FSGAP reads every packet (FSHANGAR read the first);
  - FSGAP uses one connection instead of one per lookup;
  - FSGAP distinguishes "none in range" from "cannot ask" (FSHANGAR returned `null` for both).

## Limitations

- **Reality bubble only.** The list covers the area loaded around the **user aircraft**, not around the searched
  point.
  - Observed live: about 109 NM around Nice.
  - A point far from the aircraft only sees the bubble's airports, so the result can be `null`, or an airport further
    than the real nearest one.
  - Searching from the aircraft's own position, the normal case, is exact.
- **Identifiers are what the simulator reports.** Most are ICAO codes, but small fields have local identifiers
  (`LF5QW`, `LFKQP` near Nice, with no region). They are exposed as-is, never "corrected".
- **Private API.** It depends on SimConnect.NET 0.2.2 internals (see the reflection boundary). A library update needs
  the compatibility tests to pass again.
- **Header layout.** The 36 extra header bytes on MSFS 2024 are unidentified. They are skipped, never read.

## Live validation (2026-09-24, MSFS 2024, Fenix A319 parked at LFMN)

| Scenario | Validation | Result |
|---|---|---|
| Nearest airport from the aircraft's telemetry position | LIVE TEST | **LFMN 0.5 NM**, then LF5QW 1.6 NM and LFKQP 1.9 NM; elevation 16 ft (4.9 m in the sim); 31–39 ms |
| From another known position (Cannes, `--airport-at 43.5479,6.9533`) | LIVE TEST | **LFMD 0.1 NM** |
| 2.3 minutes, one search every 10 s, alongside the telemetry and the Fenix polling | LIVE TEST | 13 answers; no warning; no E_FAIL; the telemetry flowed until the end |
| Parity with FSHANGAR on the live list | LIVE TEST (offline algorithm on live data) | 2,043 / 2,043 identical |
| Disconnection, reconnection, stop and dispose during a search; malformed packets | AUTOMATED ONLY | fake session and synthetic packets |

Sample: `--nearest-airport` (every 10 s, from the telemetry position) or `--airport-at <lat,lon>`.

## Future work (not in 0.8.0)

- **Parking.** SimConnect.NET 0.2.2 also declares `SimConnect_AddToFacilityDefinition` and
  `SimConnect_RequestFacilityData(_EX1)` internally. They can reach parking spots through the same `FacilityInterop`
  pattern. Not implemented.
- **FlightLoad.** Not implemented, and nothing here writes to the simulator.
