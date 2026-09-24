# 0004 — `FSGAP.SimConnect` layer and reflection isolation

- **Status:** Accepted (BLOCK 2, DEC-4). Implemented in BLOCK 3 (0.3.0) with the connection lifecycle, simulation
  state and aircraft detection, and **no reflection at all**. The airport list arrived in BLOCK 8 (0.8.0, see the
  follow-up). Parking and `FlightLoad` are still to
  come.

## Context

Both applications talk to MSFS through SimConnect.NET 0.2.2. Some features call *internal* SimConnect.NET native
functions through reflection: the airport facility list, parking facility data, `FlightLoad` and system-event
subscription. The audit also showed that the generic SimConnect part and the Fenix-specific part must be separated.

## Decision

- A new assembly, **`FSGAP.SimConnect`**, will hold the generic MSFS transport:
  - connection lifecycle;
  - batched data definitions;
  - simulation state;
  - aircraft detection;
  - simulator services.

  It implements the FSGAP.Abstractions simulator contracts (ADR 0008). It is the only FSGAP assembly allowed to
  reference SimConnect.NET.
- It is **not created in BLOCK 2**. An empty project would add nothing, and its first real content (and its
  SimConnect.NET reference) belongs to BLOCK 3.
- Dependency graph (no cycle):

  ```text
  FSGAP.Abstractions  <-  FSGAP.Core  <-  FSGAP.SimConnect
                                      <-  FSGAP.Fenix
  ```

  `FSGAP.Fenix` and `FSGAP.SimConnect` do not reference each other at first. When the Fenix provider needs the
  simulator (LVAR reads in BLOCK 6), it will depend on small data-access interfaces that `FSGAP.SimConnect`
  implements. It may also reference `FSGAP.SimConnect` directly if a dependency one way is enough, but
  `FSGAP.SimConnect` never depends on `FSGAP.Fenix`.
- **Reflection rule.** Reflection into SimConnect.NET internals may be kept temporarily, but only:
  - inside one internal class of `FSGAP.SimConnect`, pinned to the tested SimConnect.NET version, with a startup
    self-check that reports a clear error if the internal member is missing;
  - never in a public interface, never in a FSGAP.Abstractions model, never in FSHANGAR or FLIPPP code.
- BLOCK 2 contains no reflection call and no SimConnect.NET reference.

## Consequences

- Upgrading SimConnect.NET is a deliberate, tested change in one place.
- Airport list, parking and `FlightLoad` are ported in the simulator-services block, behind the same rule.

## Follow-up (BLOCK 8, 0.8.0)

- **The reflection now exists.** It is confined to `FSGAP.SimConnect.Native.FacilityInterop` and pinned to
  SimConnect.NET 0.2.2. It reaches:
  - `SimConnectNative.SimConnect_RequestFacilitiesList_EX1`;
  - `SimConnectClient.InvokeNativeAsync<T>`.
- **Startup check.** Members are resolved once and their signatures checked. A mismatch gives a clear
  "not compatible" error on every airport search and never a `NullReferenceException`.
- **Tests** pin the version and the members.
- **Why `InvokeNativeAsync`.** It runs the native call on the library's dispatcher, which keeps the whole transport
  on **one** connection. FSHANGAR needed a second one because it called the native function from its own thread.
- Details: [../simulator-airport-service.md](../simulator-airport-service.md).
