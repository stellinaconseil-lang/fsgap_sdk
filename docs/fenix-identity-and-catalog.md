# Fenix identity and installed livery catalog

`FSGAP.Fenix` (since 0.4.0) recognizes the Fenix Simulations A319/A320/A321, produces a normalized
`AircraftIdentity`, and keeps a catalog of the Fenix liveries installed locally, which it uses to resolve
registrations. Since 0.5.0 a session can expose the generic SimConnect telemetry through a Fenix masking policy
([generic-telemetry.md](generic-telemetry.md#fenix-policy)), and since 0.6.0 the proven Fenix systems (ADIRS, fuel
pumps, fire panel, hydraulics: [fenix-system-telemetry.md](fenix-system-telemetry.md)). Since 0.7.0 it also handles
failures through the local EFB ([fenix-failures.md](fenix-failures.md)).

```text
FSGAP.SimConnect                         FSGAP.Fenix
AircraftDescriptor (generic)  ------->   FenixAircraftProvider.Match      recognition, preliminary identity
  TITLE, ATC ID,                         FenixAircraftProvider.AttachAsync  identity resolved with the catalog
  LIVERY FOLDER, LIVERY NAME                  ^
                                              |  FindByLiveryFolderAsync
                                         FenixInstalledAircraftCatalog  <-- MSFS package folders (read-only)
                                              '-- cache: DataDirectory/fenix/installed-aircraft.json
```

The public surface is `FenixAircraftProvider` and `FenixInstalledAircraftCatalog`. Everything else is internal.

## Recognition

```regex
Fenix\D{0,4}(319|320|321)(?!\d)          case-insensitive
```

- The regex is applied to `TITLE`, then to `LIVERY FOLDER`, and the first match gives the model.
- It covers `FenixA320` (the real, observed title, with no separator), `Fenix A320`, and `fenix-a320`.
- The trailing `(?!\d)` rejects `Fenix 3200`.
- The **"Fenix" marker is mandatory**. A title that merely contains "A320" never matches: FlyByWire, iniBuilds,
  Asobo, a generic Airbus, or any other developer.
- The audited applications also had a livery-scan regex that fails on `FenixA320`. It is not used.
- A recognized Fenix answers `MatchSpecificity.Dedicated`.

A Fenix `TITLE` is the title of the **preset variant** MSFS loaded, not of the livery. For example
`FenixA321 IAE WF SC` is exactly the title of `presets\fnx\FNX_321_IAE_WF_SC`. Its words are:

| Token | Meaning | Exposed as |
|---|---|---|
| `FenixA319` / `FenixA320` / `FenixA321` | model | `Model`, `IcaoType` |
| `CFM` / `IAE` | engine family | `EngineVariant` = `CFM` / `IAE` |
| `SL` / `WF` | sharklets / wingtip fence | `WingtipConfiguration` = `Sharklets` / `WingtipFence` |
| `SD` / `HD` (A319), `SC` / `TC` (A321) | cabin: standard / high density, single / two class | not exposed (no consumer yet) |

These meanings are confirmed by the audited applications (FLIPPP's preset resolver maps `Cabin_A319_SD` → `SD`
and `Cabin_A321_SingleClass` → `SC`) and by the preset folder names of a real installation. Tokens are matched as
whole words, never as substrings. If a title names both engines, or both wingtips, that value is unknown.

## Normalized identity

| Field | Value | Note |
|---|---|---|
| `Developer` | `Fenix Simulations` | |
| `Manufacturer` | `Airbus` | |
| `Family` | `A320` | for all three, following the `AircraftIdentity.Family` convention |
| `Model` | `A319` / `A320` / `A321` | |
| `IcaoType` | `A319` / `A320` / `A321` | **inferred from the recognized model** (for these models the name is the ICAO designator); not read from MSFS |
| `Variant` | null | the sub-series (e.g. A320-214) is not known |
| `EngineVariant` | `CFM` / `IAE` / null | never defaulted |
| `WingtipConfiguration` | `Sharklets` / `WingtipFence` / null | new in 0.4.0 (open string) |
| `Registration` | see below | |
| `Livery` | `LIVERY NAME`, then the installed livery's display name | |
| `OperatorIcao` | installed livery's `icao_airline` (3 letters) | new in 0.4.0; never derived from a name |

`WingtipConfiguration` and `OperatorIcao` are the two small `AircraftIdentity` extensions of 0.4.0 (audit gap
G-I2). They are open strings, not Fenix-specific flags.

## Registration resolution

1. The **installed livery matched by `LIVERY FOLDER`**: its registration from `livery.cfg`, `aircraft.cfg` or
   the livery JSON.
2. Otherwise the simulator's **`ATC ID`**, if not empty.
3. Otherwise **unknown** (null).

`LIVERY FOLDER` comes first because the ATC id can be empty, generic, or inconsistent between liveries.

- The BLOCK 1 audit found it empty on a Fenix A320 (`ACA-C-FDRP-2624`). The catalog resolves that aircraft to
  C-FDRP (checked against the real catalog).
- BLOCK 3 read `SX-DNH` live on a Fenix A321, so the ATC id is **not** always empty, which is why it remains a
  fallback.

Other rules:

- Registrations are normalized for display (trimmed, upper-cased, dash kept if present, never inserted).
- They are compared with a lookup key that ignores case, spaces and hyphens: `F-GKXY` = `FGKXY` = `f gkxy`.
- A registration is **never guessed from a folder or display name**. This deliberately drops the audited
  scanner's folder-name heuristic, per the principle "unknown is not a guess".

### Conflicts between the loaded title and the installed livery

For the loaded aircraft, `TITLE` wins for model, engine and wingtip, because it is what MSFS actually loaded. The
livery's `required_tags` only fill what the title does not say. A conflict (both known, different) is **logged as
a warning** and never merged silently.

## MSFS installation discovery

`UserCfg.opt` is searched in this order:

1. the Microsoft Store location;
2. the Steam location;
3. a one-level search for renamed Store or Steam folders.

`InstalledPackagesPath` is then read, and the existing `Community`, `Community2024`, `Official2024` and
`Official2020` roots are kept. Nothing is hard-coded to one Community path.

This logic is generic to MSFS. It is internal to FSGAP.Fenix because that is its only consumer today. A host can
also pass explicit package roots to the catalog constructor (for example a folder chosen by the user).

## Fenix package discovery

A top-level package folder is a Fenix candidate when its name contains `fenix` or `fnx`, or when its
`manifest.json` title, creator or content type does.

- A candidate without liveries contributes nothing.
- A manifest naming only the airframe (A320) without Fenix is another developer's package and is ignored.
- A malformed manifest is ignored.

## Livery parsing

A folder is a livery candidate when it directly contains `livery.cfg` or `aircraft.cfg`.

- Fenix's internal trees `presets\` and `attachments\` are pruned by exact folder name. They contain
  `aircraft.cfg` files with `[FLTSIM]` sections and house registrations, but they are variants, not selectable
  liveries.
- The search depth is limited to 10.

Two shapes exist:

- the Fenix community livery, with only a `livery.cfg`;
- the classic `aircraft.cfg` with `[FLTSIM.n]`.

A livery whose `required_tags` is `Disabled` (Fenix's placeholder `FNX_3xx_Fenix` liveries) is skipped as not
selectable.

| Field | Sources, first hit wins |
|---|---|
| Model | exact `A319`/`A320`/`A321` tag, then the Fenix title rule on the aircraft.cfg title. **Never** package or folder names: `fnx-aircraft-319-321` would give a wrong model. |
| Engine, wingtip | exact tags, then exact aircraft.cfg title words |
| Registration | livery.cfg `[FLTSIM] atc_id`, then aircraft.cfg `atc_id`, then `registration` / `tailNumber` / `registrationNumber` in a livery JSON |
| Display name | aircraft.cfg `ui_variation`, then livery.cfg `[GENERAL] name` |
| Operator ICAO | `icao_airline`, only if exactly three letters |
| `LiveryFolder` | the folder name as on disk |
| `PackageName` | the top-level package folder |
| `ModifiedAt` | newest write time of the files read |
| `Id` | `fenix:` + a hash of the package name and relative path (stable across rescans) |

Inside one livery, `required_tags` wins over the aircraft.cfg title.

## Duplicates

- **Same livery folder** (case-insensitive) installed twice: the folder is **ambiguous**.
  `FindByLiveryFolderAsync` returns null rather than an arbitrary pick, and the refresh reports it. Registration
  then falls back to the ATC id.
- **Same registration** on several liveries is legitimate (two liveries of one airframe). `FindByRegistrationAsync`
  returns all of them in a deterministic order. A real installation has five such registrations, for example
  SX-DNG.

## Cache

- Technology: **one JSON file** at `FsgapOptions.DataDirectory/fenix/installed-aircraft.json`, with schema
  version 1. SQLite was rejected: a few hundred entries are loaded whole and indexed in memory, so a database would
  add a dependency without a benefit.
- Purpose: lookups work as soon as the application starts. A real scan of 129 liveries took about 4 s on a cold
  disk.
- Writes are atomic: temporary file, then rename.
- A missing, unreadable, corrupted or other-schema file is ignored (warning), and the next refresh rebuilds it.
- The cache is only an accelerator. The installed files are the source of truth, and the catalog never writes
  anywhere else: never in MSFS, Fenix, FSHANGAR or FLIPPP folders.

## Refresh, progress, thread safety

- `RefreshAsync` always rescans. Lookups never scan.
- Reading the small `.cfg` files is cheap compared with enumerating the folders, so there is no per-entry
  incremental parse and no file watcher.
- Progress: one `Discovering` report while the total is unknown, then `Scanning(processed, total)` reports
  throttled to about 20, ending at processed = total.
- Each refresh builds a new immutable indexed snapshot and **swaps it atomically**, so readers keep the previous
  one and never see a half-built catalog.
- Refreshes are serialized.
- A cancelled refresh (`OperationCanceledException`) keeps the previous content and cache.

## Error handling

| Kind | Handling |
|---|---|
| Fatal: MSFS 2024 not found | The refresh returns the error; the previous content is kept |
| Unreadable package root or folder (permissions, broken junction or symlink) | Reported in `Errors`; the scan continues |
| Unreadable or broken livery file | Reported in `Errors`; the scan continues |
| Malformed `.cfg` lines | Skipped; the rest of the file is used |
| Not a livery (no `[FLTSIM]` / `livery.cfg`), disabled livery, non-Fenix package | Skipped silently (counted in the log) |
