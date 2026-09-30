# FSGAP.Synaptic — Synaptic Simulations A220-300 provider

Status: **0.10.0-preview.3**: automated qualification (BLOCK 10B.2), live qualification (BLOCK 10B.3, results below), APU switch fix.
The evidence behind every choice below is in [audits/synaptic-a220-discovery.md](audits/synaptic-a220-discovery.md)
(variables, BLOCK 10A-LIVE sessions) and [audits/synaptic-a220-livery-discovery.md](audits/synaptic-a220-livery-discovery.md)
(liveries and registrations, BLOCK 10A.5).

## Architecture

```text
FSGAP.Abstractions  <-  FSGAP.Core  <-  FSGAP.Synaptic      (this provider)
                                    <-  FSGAP.Fenix
                                    <-  FSGAP.SimConnect    (transport, single connection)
```

- `FSGAP.Synaptic` depends on `FSGAP.Abstractions`, `FSGAP.Core` and `Microsoft.Extensions.Logging.Abstractions`
  only. It references neither `FSGAP.SimConnect` nor `FSGAP.Fenix` (tests enforce it).
- Public API: `SynapticAircraftProvider` (`ProviderId = "synaptic"`) and `SynapticInstalledAircraftCatalog`.
  Everything else is internal.
- It is **one provider among others**: the host registers it next to `FenixAircraftProvider` in the same
  `AircraftProviderRegistry`; see [architecture.md § Multiple aircraft providers](architecture.md#multiple-aircraft-providers-010-preview).
- Every simulator access goes through the host's single connection: `ISimulatorVariableReader` (overlay reads),
  `IAircraftDetector` (stop on aircraft change), `ITelemetryProvider` (generic telemetry) and
  `IInstalledLiveryService` (catalog). Read-only: no LVAR, H-event or K-event write, no EFB, no WASM, and **no AI
  aircraft is ever created**.

```csharp
var synapticLiveries = new SynapticInstalledAircraftCatalog(options, simulator);   // IInstalledLiveryService
await synapticLiveries.RefreshAsync();
registry.Register(new SynapticAircraftProvider(
    synapticLiveries,
    genericTelemetry: simulator.Telemetry,
    simulatorVariables: simulator,
    aircraftDetector: simulator.AircraftDetector));
```

## Detection

`Match` answers `Dedicated` only when **all three** hold on the generic descriptor:

| Descriptor field | SimVar | Required value |
|---|---|---|
| `Title` | `TITLE` | exactly `A220-300` or `A220-300 - No Cabin` (trimmed, case-insensitive) |
| `Model` | `ATC MODEL` | `A220-300` |
| `Manufacturer` | `ATC TYPE` | `223` |

Anything else is `NotSupported`: Asobo `PassiveAircraft` A220s, FSLTL/AI models, repaints with a different title,
free text containing "A220", the exact title with missing or different ATC strings, Fenix, iniBuilds A380, GA aircraft.
`AttachAsync` refuses (`NotSupportedException`) an aircraft `Match` would not accept; no Synaptic variable is ever read
for another aircraft.

## Identity

| Field | Value |
|---|---|
| Developer | `Synaptic Simulations` |
| Manufacturer | `Airbus` |
| Family / Model | `A220` / `A220-300` |
| IcaoType | `BCS3` (never the simulator's `ATC TYPE` "223") |
| EngineVariant | `PW1500G` |
| Variant, WingtipConfiguration, OperatorIcao | `null` (no evidence; not inferred from the livery) |
| Livery | the simulator's livery name |
| Registration / RegistrationSource | see below |

## Registration

`AircraftIdentity.RegistrationSource` (new, vendor-neutral, `null` when not stated — Fenix leaves it `null`) says
where the registration came from. Resolution, in order:

1. **Authoritative** — `atc_id` of the livery's `livery.cfg`. The resolver accepts it, but **no reader exists**: the
   A220 liveries are streamed marketplace content and no accessible `livery.cfg` was found (BLOCK 10A.5).
2. **Observed** — the `ATC ID` SimVar, **only when it equals the folder-derived registration** (letters and digits,
   case-insensitive). The A220 is known to report a stale `ATC ID` (`C-FFCO` on Delta and airBaltic), so an
   uncorroborated `ATC ID` is never used, not even when the folder gives nothing.
3. **Derived** — strict parsing of `LIVERY FOLDER`: the folder is split on spaces, `_` and `.`; exactly one whole token
   must be a registration (`N` + US N-number, `HL####`, `JA…`, or `PP-XXXXX` with a known nationality prefix).
   Zero or several candidates give nothing. The token is reported as written: `G-GUAC` on the Air Canada livery is
   not "corrected".
4. **Cached** — what the catalog learned earlier for this livery name, **with its original source**.
5. Otherwise `null`. House (`A_BCS3_SYN_HOUSE`) and White liveries have no registration.

## Telemetry

Composition per snapshot: generic telemetry → A220 policy (mask) → overlay → freshness (`StaleAfter`, 15 s by
default). Once the detector reports another aircraft the session publishes `Unavailable` and stops reading.

### Generic policy

Masked to `Unavailable` (**GENERIC_WRONG** in BLOCK 10A-LIVE): engine `Running`, `FuelFlowKilogramsPerHour`,
`StarterActive`, `OilTemperatureCelsius`, `OilPressurePsi`; `Apu.BleedOn` (replaced by the overlay);
`Pressurization` (cabin altitude and rate). Everything else passes through, including fields not yet exercised live
but never shown wrong: flight-envelope warnings, reverser, `LandingGear.AntiskidActive` (inconclusive: never true,
no skid seen). They are the BLOCK 10B.3 live checks.

Declared sections: `FlightState`, `Warnings`, `Engines`, `LandingGear`, `FlightControls`, `Environment` (generic) and
`FuelPumps`, `Apu`, `Fire` (overlay). Not declared: `Pressurization`, `Electrical`, `Hydraulics`,
`InertialReferences`.

### Overlay (P0)

One group of 6 variables, read every 2 s (≈ 0.5 read/s) on the shared reader:

| Variable | Raw | Normalized |
|---|---|---|
| `L:A22X L Boost Pump`, `L:A22X R Boost Pump` | 0 / 1 / 2 / other | `FuelPumps[left/right]`: `Mode` Off / Auto / On / Unknown; `IsOn` false / **Unavailable** / true / Unknown (AUTO is never flattened to on or off); `Fault` Unavailable |
| `L:A22X APU Switch` | 0 / 1 / 2 / other | `Apu.MasterSwitchOn` false / true / true / Unknown (1 = selector at RUN, 2 = after a held START; both seen live in BLOCK 10B.3). No selector mode. |
| `L:A22X APU Bleed Off` | 0 / 1 / other | `Apu.BleedOn` (selection) true / false / Unknown. Not bleed flow. |
| `L:A22X L Eng Fire`, `L:A22X R Eng Fire` | 0 / 1 / other | `Engines[1/2].FireHandlePulled` false / true / Unknown, meaning "engine fire control activated": the variable is a latched state that stays 1 after the momentary pushbutton is released, until the aircraft is reloaded (BLOCK 10B.3). `FireDetected`, `FireWarningLit` stay Unavailable. |

`Apu.Available` and `Apu.Running` stay Unavailable (no readable source).

### Deferred

- **Master warning / caution**: live-validated in BLOCK 10A-LIVE, but no existing contract field maps cleanly
  (`WarningsTelemetry` is flight-envelope warnings). Needs a vendor-neutral alerts section first.
- IRS, hydraulics, electrical, pressurization, anti-ice, pump faults: no reliable source.

## Failures

None. Capabilities declare `FailureCapabilities.None`; `Failures` is `UnsupportedFailureProvider` (commands answer
`NotSupported`, reading active failures throws `NotSupportedException` as `CanReadActiveFailures` is false).
`FSGAP.Synaptic` contains no `IFailureProvider`.

## Installed-aircraft catalog

`SynapticInstalledAircraftCatalog` (`IInstalledAircraftCatalog`):

- **Source**: `IInstalledLiveryService` — the simulator's own enumeration, marketplace content included. No disk scan,
  no AI spawn.
- **Filter**: rows whose aircraft title is one of the two presets. Rows without a livery name (the preset's unnamed
  default) are skipped.
- **Dedupe**: cabin and no-cabin rows of the same livery name are one entry (key: model + livery name,
  case-insensitive); `Id` = `synaptic:` + 32 hex of SHA-256(model | livery name). The live fixture gives 11 liveries.
- **Registration and folder**: the enumeration has neither. `Learn` (called by the provider on attach) records the
  livery folder and the resolved registration; lookups by folder (single match only) and by registration use them.
- **Cache**: `DataDirectory/synaptic/installed-aircraft.json`, schema 1, atomic write (temporary file then rename).
  Corrupt, empty or other-schema files are ignored with a warning and rewritten. Learned data survives a refresh.
- **Errors**: a failed enumeration (`SimulatorServiceException`) keeps the previous content and is reported in
  `CatalogScanResult.Errors`.

## Tests

`tests/FSGAP.Synaptic.Tests` (104 tests): recognition and look-alikes, identity, registration parser and precedence,
policy mask and pass-through, overlay mapping (all pump values, APU, fire, unexpected values), composition (replaced,
stale), sessions (capabilities, `FailureCapabilities.None`, read cadence, stop on dispose and on aircraft change,
expiry on read failure), catalog (fixture, dedupe, learn, cache roundtrip, corrupt cache, refresh failure), the
Fenix + Synaptic registry (both registration orders, ambiguity, generic vs dedicated), switching
Fenix → Synaptic → Fenix → unsupported → Fenix on one shared reader, and architecture scans.

## Live qualification (BLOCK 10B.3, 2026-09-30)

Production providers only, through `samples/FSGAP.SimConnect.Console --qualify <dir>`: Fenix and Synaptic in one
`AircraftProviderRegistry` on one `SimConnectSimulator`, with a pass-through reader counting A22X vs other reads, the
raw A22X values as the provider received them, and an EFB request counter. MSFS 2024, LFKJ parking, then a flight
LFKJ → LFMN (ILS 04L). Nothing was written to the aircraft; no AI object was created.

| Area | Live result |
|---|---|
| Detection | `A220-300` and `A220-300 - No Cabin`, ATC MODEL `A220-300`, ATC TYPE `223`; registry `Resolved` to `synaptic` alone every time; Fenix A320 resolved to `fenix` alone |
| Identity | as specified; Variant/Wingtip/Operator null |
| Registration | Air France F-HZUF, Air Baltic YL-CSM, Delta N324DU, all `Derived`; the ATC ID changed during loads (C-FFCO, I-OVTU, empty) and was never used |
| Enumeration / catalog | 15 815 rows, 24 A220 preset rows (2 unnamed) → 11 logical liveries in ~55 ms + ~45 ms; House/White listed without registration |
| Cache restart | 3 learned liveries reloaded with folder, registration, source and last-observed time |
| Fuel pumps | both pumps OFF / AUTO / ON: `Off`/false, `Auto`/Unavailable, `On`/true |
| APU switch | raw 0 = OFF; **1 observed** (selector RUN after a brief START, 1 min 45 s); 2 after a held START, kept while the APU runs (spring-loaded START does not bring it back to 1). 0.10.0-preview.3 maps 1 and 2 to `MasterSwitchOn = true` |
| APU bleed | raw 1/0 ↔ selection off/on; generic `PNEUMATICS APU BLEED AIR` false throughout (mask confirmed) |
| Engine fire pushbuttons | raw 0 → 1 on press, exposed as `FireHandlePulled`; the value **stays 1** after the momentary pushbutton is released (latched logical state), reset only by reloading the aircraft |
| Generic pass-through | position, altitude, IAS/GS/VS, heading, attitude, G, weight, N1/N2/EGT (within 0.6 % / 0.5 % / 6 °C of the EICAS), throttle, gear handle and units (retraction 14 s, extension 14 s), flap handle and trailing-edge surfaces (FLAP 1 = slats only → 0 %, FULL → 100 %), speed brake / ground spoilers (99 %), brakes (100/100), steering, touchdown VS (−631 ft/min at LFMN), overspeed warning (N → Y at 355 kt, cockpit clacker heard) |
| Masked fields | fuel flow, oil temperature and pressure, cabin altitude and rate: EICAS showed 200 kg/h, 117 °C, 108–122 psi, CAB ALT 6 500 ft; FSGAP Unavailable as designed |
| Antiskid | false through two maximum-braking roll-outs: still unproven (no skid evidence), not masked |
| Reverse | not tested (reverse could not be selected) |
| Failures | `FailureCapabilities.None`, `UnsupportedFailureProvider`; 0 EFB requests during the whole session |
| Switching | Synaptic → Fenix → Synaptic live: old session disposed each time, A22X reads stop at disposal, Fenix reads stop at disposal, Fenix failure provider (40 keys) returns on the Fenix |
| Connection / cost | 1 native connection throughout; overlay 6 variables, one group, 0.47–0.50 read/s |

Follow-up in 0.10.0-preview.3: `APU Switch` = 1 now reads `MasterSwitchOn = true`; the engine fire mapping is
unchanged and documented as a latched "fire control activated" state. Still open: master caution/warning, cabin altitude
and ELT alerts are not exposed (no contract field).

### Session churn during aircraft loading (BLOCK 10B.3A analysis, not fixed)

The 15 sessions of the 10B.3 run came from 15 descriptor emissions, of which only **7 were real aircraft or livery
changes**; the other **8 changed only `ATC ID`** (Synaptic: C-FFCO, I-OVTU, I-FZRQ, empty; Fenix: G-EUYY → I-RQUV →
G-EUYY), a few seconds after each load. Trace:

1. `SimConnectSimulator` publishes an `AircraftDescriptor` whenever any of its fields changes
   (`ObservableState` with the record's default equality, which includes `Registration` = `ATC ID`).
2. There is no session coordinator in Core: the host decides, and a host that re-attaches on every emission (the
   sample does) recreates the session.
3. A host that kept the session would not be better off: both providers' sessions compare the detector's current
   descriptor to the attached one with full equality (`AircraftReplaced`), so an `ATC ID` change makes a running
   session consider its aircraft replaced — it publishes Unavailable and stops reading. This is the same in
   FSGAP.Fenix and FSGAP.Synaptic.

Classification: **real architecture defect** (a volatile metadata field forces session replacement), no leak. Proposed
vendor-neutral fix, pending a decision because it changes Fenix session behaviour: a "same loaded aircraft" comparison
that ignores `ATC ID` only (the one field proven volatile; livery name and folder changed only on real livery changes),
used by both providers' `AircraftReplaced` and offered to hosts for the replace-or-keep decision. Caveat: Fenix falls
back to `ATC ID` for the registration of liveries missing from its catalog, and `IAircraftSession.Identity` cannot be
updated, so keeping a session across an `ATC ID` change could keep a transient registration for such liveries.
