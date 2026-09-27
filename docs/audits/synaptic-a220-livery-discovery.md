# Synaptic A220 — livery and registration discovery (BLOCK 10A.5)

Controlled, experimental SimConnect test, 2026-09-27, MSFS 2024 `Microsoft.Limitless` 1.8.16.0, FSGAP_SDK 0.9.0.
Goal: can FSGAP rebuild the installed Synaptic A220 fleet (liveries, registrations) from MSFS itself, including the
Marketplace-streamed liveries whose files cannot be read?

Nothing here is production: no catalog, no registration resolver, no `FSGAP.Synaptic`, no public contract change,
no version bump. The experiment is internal to `FSGAP.SimConnect` (marked BLOCK 10A.5 / experimental) and driven by the
live sample. FSHANGAR, FLIPPP and fenixhangarweb are untouched.

Fixtures: [`tests/FSGAP.SimConnect.Tests/Fixtures/synaptic/`](../../tests/FSGAP.SimConnect.Tests/Fixtures/synaptic/)
(`a220-livery-enumeration.json`, `a220-livery-probes.json`).

## 1. Answers

| Question | Answer | Evidence |
|---|---|---|
| Q1 Can SimConnect enumerate the installed A220 liveries? | **YES** | `SimConnect_EnumerateSimObjectsAndLiveries(AIRCRAFT)`: 24 A220 rows, 11 named liveries, 5 identical runs |
| Q2 Does it include streamed Marketplace liveries? | **YES** | all 11 liveries are Marketplace content (`fs24-inibuilds-aircraft-a220` / `fs24-inibuilds-a220-liveries`, `.fsarchive`, unreadable on disk); no A220 livery exists as files on this machine |
| Q3 Exact values | §3 | titles `A220-300` / `A220-300 - No Cabin`; livery names `<Operator> A220-300` |
| Q4 Can an enumerated livery be instantiated temporarily? | **YES** | 19 non-ATC AI aircraft created, read and removed, all 11 livery names accepted (HRESULT 0, object id assigned in 18–72 ms) |
| Q5 Can the AI object's strings be read? | **YES** | TITLE, LIVERY NAME, LIVERY FOLDER, ATC ID, ATC AIRLINE, ATC MODEL, ATC TYPE read from the returned object id |
| Q6 Tail number `""` gives the registration? | **NO** | ATC ID blank on **all 16** empty-tail probes (outcome B) |
| Q7 Is LIVERY FOLDER stable enough to derive a registration? | **Stable and unique, but derived only** | same folder for a livery under both presets and across runs; 9 of 10 folders end with a registration-shaped token; one is inconsistent with its operator (§5) |
| Q8 Do accessible `livery.cfg` files give `atc_id`? | **NOT TESTABLE here** | no A220 livery installed as files (no Community livery on this machine) |
| Q9 Minimum reliable production strategy | §10 | enumeration + folder learning + explicit confidence + cache |

## 2. Wrapper capability (SimConnect.NET 0.2.2)

| Native capability | SimConnect.NET exposure | Approach used |
|---|---|---|
| `SimConnect_EnumerateSimObjectsAndLiveries` | **NOT_EXPOSED** (only the receive id `EnumerateSimobjectAndLiveryList` = 38) | own `DllImport` of the documented export, called with the transport's handle through the library dispatcher (`FacilityInterop.InvokeNativeAsync`); answer gathered from the public `RawMessageReceived` event |
| `SimConnect_AICreateNonATCAircraft_EX1` | **NOT_EXPOSED** (only the non-EX1 variant, internal, without livery) | own `DllImport`, same dispatcher |
| `SimConnect_AICreateParkedATCAircraft_EX1` | **NOT_EXPOSED** (non-EX1 internal only) | not used: the non-ATC variant was sufficient and avoids ATC side effects |
| `SimConnect_AIRemoveObject` | **AVAILABLE_INTERNAL** (internal P/Invoke) and public `SimObjectManager.RemoveObjectAsync`, which only knows objects it created | own `DllImport`, same dispatcher |
| Read string data from an AI object | **PUBLICLY_EXPOSED** (`SimVars.GetAsync<T>(objectId)`) | public API with the assigned object id |
| (for reference) `SimConnect_AICreateSimulatedObject_EX1` | PUBLICLY_EXPOSED (`SimObjectManager.CreateObjectWithLiveryAsync`, no tail number) | not used |

All calls run on the **single** transport connection. The reflection into SimConnect.NET internals stays in
`FacilityInterop` (one resolved member, now also exposed as `InvokeNativeAsync`); `LiveryInterop` holds three plain
`DllImport` declarations and never keeps the client. The existing architecture tests (one factory, one client holder
set, reflection in `FacilityInterop` only, no vendor names in the transport) still pass.

## 3. Enumeration

| | |
|---|---|
| Total aircraft/livery rows | 15 815 (13 521 titles, 1 164 livery names; FSLTL and other AI model libraries account for most) |
| Packets / layout | 201 packets per answer; **28-byte header** + **512-byte entries** (2 × 256 chars). Each packet announces 79 entries in 28 + 80 × 512 bytes (one spare slot). Verified identical on 5 runs |
| Time | 60–73 ms, **one** native request |
| A220 rows | 24: 12 `A220-300`, 12 `A220-300 - No Cabin` |
| Unique A220 livery names | 11 (plus one unnamed row per preset) |
| Other titles containing A220 | `Asobo PassiveAircraft A220-100`, `Asobo PassiveAircraft A220-300` (AI models, correctly excluded by the exact-title rule) |
| Marketplace liveries visible | **YES** (all 11) |

| AircraftTitle | LiveryName | Source / preset | Duplicate? |
|---|---|---|---|
| A220-300 and A220-300 - No Cabin | Air Baltic A220-300 | Marketplace, both presets | same livery under 2 presets |
| both | Air Canada A220-300 | Marketplace, both presets | idem |
| both | Air France A220-300 | Marketplace, both presets | idem |
| both | Breeze A220-300 | Marketplace, both presets | idem |
| both | Delta A220-300 | Marketplace, both presets | idem |
| both | ITA Airways A220-300 | Marketplace, both presets | idem |
| both | JetBlue A220-300 | Marketplace, both presets | idem |
| both | Korean Air A220-300 | Marketplace, both presets | idem |
| both | Swiss A220-300 | Marketplace, both presets | idem |
| both | Synaptic House A220-300 | Marketplace, both presets | idem |
| both | White A220-300 | Marketplace, both presets | idem |
| both | (empty) | base preset row | idem |

**MSFS selector comparison.** The in-sim aircraft selector was not counted during this block (it would need a manual
count by the pilot). The three liveries loaded by hand in BLOCK 10A-LIVE (Delta, Air Baltic, Air France) are all in
the enumeration; no enumerated livery was rejected at creation. `missing from enumeration`: none known;
`extra in enumeration`: none known; duplicates: only the preset duplication above.

## 4. Deep probes (AI objects)

Placement: non-ATC AI aircraft, on the ground, about 3.3 km north of the user aircraft (which was airborne). One object
at a time: create → wait for `ASSIGNED_OBJECT_ID` → 3 s settle → read by object id → read object 0 in the same cycle →
remove → confirm (a read of the id no longer answers within 3 s).

**User vs AI object.** Every probe read the returned id (77 168 644 … 85 278 733, never 0). The same cycle read object 0
separately; the user flew Air France while probes returned Air Baltic, Delta, Air Canada, etc., so values cannot come
from the user aircraft.

| Livery requested | Preset | TITLE | LIVERY FOLDER | ATC ID (tail `""`) | ATC AIRLINE | ATC MODEL / TYPE | Removed |
|---|---|---|---|---|---|---|---|
| Air France A220-300 | no cabin | A220-300 - No Cabin | `AIR FRANCE F-HZUF` | *(blank)* | *(blank)* | A220-300 / 223 | YES |
| Air Baltic A220-300 | no cabin | idem | `AIR BALTIC YL-CSM` | *(blank)* | *(blank)* | idem | YES |
| Delta A220-300 | no cabin | idem | `DELTA N324DU` | *(blank)* | *(blank)* | idem | YES |
| Air Canada A220-300 | no cabin | idem | `AIR CANADA G-GUAC` | *(blank)* | *(blank)* | idem | YES |
| Breeze A220-300 | no cabin | idem | `BREEZE AIRWAYS N214BZ` | *(blank)* | *(blank)* | idem | YES |
| ITA Airways A220-300 | no cabin | idem | `ITA AIRWAYS EI-HHU` | *(blank)* | *(blank)* | idem | YES |
| JetBlue A220-300 | no cabin | idem | `JETBLUE N3115J` | *(blank)* | *(blank)* | idem | YES |
| Korean Air A220-300 | no cabin | idem | `KOREAN AIR HL8315` | *(blank)* | *(blank)* | idem | YES |
| Swiss A220-300 | no cabin | idem | `SWISS HB-JCO` | *(blank)* | *(blank)* | idem | YES |
| Synaptic House A220-300 | no cabin | idem | `A_BCS3_SYN_HOUSE` | *(blank)* | *(blank)* | idem | YES |
| Air France / Air Baltic / Delta | **cabin** (`A220-300`) | A220-300 | same three folders as without cabin | *(blank)* | *(blank)* | idem | YES |

White and the unnamed rows were not probed (10-probe cap).

### 4.1 Empty tail number

**TailNumber = `""`: does ATC ID become the livery registration? NO.** 16 of 16 empty-tail probes (10 liveries; Air France,
Air Baltic and Delta three times each, under both presets) returned a **blank** ATC ID (outcome B). The fixture keeps
the 13 probes of the two last runs. No generated value, no inherited user value (the
user's own ATC ID was also blank).

### 4.2 Explicit tail control

| | |
|---|---|
| TailNumber | `FSGAP01` (three times, Air France: twice with the no-cabin preset, once with the cabin preset) |
| ATC ID observed | `FSGAP01` |
| Livery preserved | YES (`Air France A220-300`) |
| Folder preserved | YES (`AIR FRANCE F-HZUF`) |
| Other effects | none observed; the argument only sets the ATC ID |

## 5. Livery folder and registration candidates

The audit-only parser (sample `LiveryDiscoveryAnalysis.ParseRegistration`, unit-tested) looks for whole
registration-shaped tokens: a known hyphenated nationality prefix, a US N-number, or an unhyphenated Korean/Japanese
registration. Its output is always `Derived`, never `Authoritative`.

| LiveryName | Folder | Candidate | Confidence |
|---|---|---|---|
| Air France A220-300 | AIR FRANCE F-HZUF | F-HZUF | High |
| Air Baltic A220-300 | AIR BALTIC YL-CSM | YL-CSM | High |
| Delta A220-300 | DELTA N324DU | N324DU | High |
| Air Canada A220-300 | AIR CANADA G-GUAC | G-GUAC | High (shape) — **operator/prefix mismatch**: a Canadian operator with a UK `G-` prefix; the folder is likely a typo of `C-GUAC`. Shape alone cannot catch this |
| Breeze A220-300 | BREEZE AIRWAYS N214BZ | N214BZ | High |
| ITA Airways A220-300 | ITA AIRWAYS EI-HHU | EI-HHU | High |
| JetBlue A220-300 | JETBLUE N3115J | N3115J | High |
| Korean Air A220-300 | KOREAN AIR HL8315 | HL8315 | High (the unhyphenated `HL` form was added after this probe showed it) |
| Swiss A220-300 | SWISS HB-JCO | HB-JCO | High |
| Synaptic House A220-300 | A_BCS3_SYN_HOUSE | none | None — a house livery with no registration; `Registration = null` is correct |

- **Stable**: identical folder for a livery across runs and across both presets.
- **Unique**: 10 different folders for 10 liveries.
- **Registration-bearing**: 9 of 10, but the Air Canada case shows the text can be wrong. Folder parsing is a
  **derived fallback** with a confidence, never an authority.

## 6. Accessible `livery.cfg`

No A220 livery is installed as files on this machine (Community and Community2024 contain no `livery.cfg` with
`ext_a223_fuselage`, `A220` or `Synaptic`). The comparison `livery.cfg atc_id` vs enumeration vs folder vs AI ATC ID could
not be made. A third-party Community livery is needed to close Q8.

## 7. Registration sources

| Source | Works? | Reliability | Production suitability |
|---|---|---|---|
| `livery.cfg atc_id` | not testable here | authoritative by definition when present | primary when a readable file exists |
| Live user-aircraft ATC ID | observed (10A-LIVE) | **low**: `C-FFCO` on Delta and Air Baltic, often blank; not livery-bound | only if non-empty and not contradicted; never alone |
| AI probe ATC ID (tail `""`) | yes, but always blank | none | **not usable** |
| LIVERY FOLDER (user aircraft or AI probe) | yes | stable, unique; 9/10 registration-shaped, 1 inconsistent | **derived fallback** with confidence |
| Learned cache | conceptual | as good as its sources, with their labels | yes, to avoid re-probing |

**Operator.** `ATC AIRLINE` was blank on all 19 probes and on the user aircraft. It cannot populate `OperatorIcao`.
The livery name (`Air France A220-300`) names the operator in words, but mapping it to an ICAO code would be an
inference and stays out (or explicitly `Derived`).

## 8. Duplication

Each named livery exists under both presets (`A220-300`, `A220-300 - No Cabin`) and resolves to **the same LIVERY
FOLDER** (checked for Air France, Air Baltic, Delta). The presets differ only by the cabin model. **Without
deduplication, one real aircraft would appear twice in a future FSHANGAR hangar.** Proposed identity:
`Model + LiveryName` (equivalently `Model + LiveryFolder` once learned), with the presets kept as variants of that one
entry. Registration is not a safe key: it can be null (house, white) or wrong (Air Canada).

## 9. Performance and safety

| | |
|---|---|
| Time to enumerate | 60–73 ms, 1 native request, 201 packets |
| Deep probe | ~7.1 s each, dominated by the 3 s settle and the 3 s removal confirmation (deliberately conservative) |
| AI creation latency (request → object id) | 18–72 ms |
| Identity read latency | 15–50 ms |
| AI removal call | < 1 ms (removal confirmed within the 1 s + 3 s check) |
| Simulator working set | −37 to +86 MB around runs of 3–11 probes, no upward trend (10.5–10.6 GB) |
| Visible FPS impact | not measured by the harness; to be confirmed by the pilot (the aircraft was flying during the probes) |
| MSFS instability | none: no crash event, no disconnection, telemetry and identity polling continued |
| Synaptic initialization side effects | none observable from SimConnect; the systems WASM of an AI object cannot be observed from outside |
| Objects created / removed / remaining | **19 / 19 / 0** (runs of 4, 11 and 4), ledger-checked, cleanup in `finally`, explicit `--livery-cleanup <ids>` path |

## 10. Decisions

| Capability | Decision |
|---|---|
| Livery enumeration | **PRODUCTION_READY** as a transport capability: official API, fast, complete for Marketplace content, stable layout. Pinned to SimConnect.NET 0.2.2 like the airport service |
| Registration via empty-tail AI probe | **NOT_USABLE** (always blank) |
| Registration via LIVERY FOLDER | **DERIVED_FALLBACK** |
| `livery.cfg` parsing | **PRIMARY_WHEN_ACCESSIBLE** (principle; untested here) |
| AI deep probe overall | **SUITABLE_ONLY_FOR_DIAGNOSTICS**: safe and cheap per object, but it spawns aircraft into the user's world. Its only unique value is learning a livery's folder before the user loads it; if ever offered, it should be an explicit user action ("scan my liveries"), never automatic at startup |

### Revised resolution hierarchy (validated / reordered)

1. **Enumeration** → the list of installed liveries (authoritative for *existence*, not for registration).
2. **`livery.cfg atc_id`** when a readable file exists → `Authoritative`.
3. **Learned LIVERY FOLDER** (from the user aircraft whenever that livery is loaded; optionally from an explicit deep
   scan) → registration candidate `Derived` + confidence.
4. **Live ATC ID** only as a corroborating `Observed` value when non-empty and consistent; never alone (moved down: it
   proved stale).
5. **Cache** of what was learned, with sources.
6. **Unresolved** → `Registration = null`, livery kept.

The empty-tail AI ATC ID is removed from the hierarchy.

## 11. Recommended catalog algorithm (not implemented)

```text
SynapticInstalledAircraftCatalog.Refresh():
  rows      = transport.EnumerateAircraftLiveries()                 # one request, ~70 ms
  a220      = rows where Title ∈ {"A220-300", "A220-300 - No Cabin"}  (exact, trimmed, case-insensitive)
  fleet     = group a220 by LiveryName (case-insensitive); skip the unnamed base row or show it as "default"
              → one entry per livery; presets kept as variants
  for entry in fleet:
      cached = cache[Model + LiveryName]
      entry.LiveryFolder = cached.LiveryFolder (learned) or null
      entry.Registration, Source, Confidence =
            livery.cfg atc_id (Authoritative)                  if a readable livery file matches
         or ParseRegistration(LiveryFolder) (Derived, High/Low) if a folder was learned
         or null (None)
      entry.OperatorIcao = null                                 (ATC AIRLINE blank; no inference)
  drop cache rows whose LiveryName is no longer enumerated

On every aircraft change (live descriptor):
  if Synaptic A220: cache[Model + descriptor.Livery].LiveryFolder = descriptor.LiveryFolder, LastObserved = now
  (and ATC ID as an Observed corroboration only when non-empty)

Optional, user-triggered only: deep scan = for each fleet entry without a folder, one AI probe
  (create, read LIVERY FOLDER, remove, confirm), sequential, ledger-checked.
```

Cache (`synaptic-aircraft-catalog.json`, under `DataDirectory`): `Model`, `LiveryName`, `Presets[]`, `LiveryFolder`,
`Registration`, `RegistrationSource`, `RegistrationConfidence`, `LastObserved`. It makes folders learned from the live
aircraft survive restarts, which is what turns "derived when loaded once" into a usable hangar.

## 12. Contract and transport review (proposals only)

- **Generic capability.** Livery enumeration is not Synaptic-specific: every MSFS 2024 aircraft is listed. It belongs
  in the vendor-neutral transport, for example an `IInstalledLiveryService` returning `InstalledLivery { AircraftTitle,
  LiveryName }`, implemented by `SimConnectSimulator`. Proposed P1 for BLOCK 10B if the catalog is built.
- **Installed-aircraft contract.** `InstalledAircraft` already carries `Identity` (model, livery, registration,
  operator), `LiveryFolder` and `PackageName`. What it lacks is **where the registration came from**: add an optional,
  vendor-neutral `RegistrationSource` (`None`, `Derived`, `Observed`, `Authoritative`) and a confidence, so FSHANGAR can
  show "F-HZUF (derived)" honestly. No Synaptic-specific DTO is needed.
- Everything Synaptic-specific (exact titles, folder learning rules) stays in the future `FSGAP.Synaptic`.

## 13. Architecture confirmation

| | |
|---|---|
| Native SimConnect connections | 1 (every call through the sample's one `SimConnectSimulator`) |
| Temporary AI objects created / removed / remaining | 19 / 19 / 0 |
| Production catalog / registration parser / FSGAP.Synaptic | NO / NO (the parser is sample-only) / NO |
| FSHANGAR, FLIPPP, fenixhangarweb modified | NO |
| Version | 0.9.0 |
