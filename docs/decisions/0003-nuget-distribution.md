# 0003 — Distribution as versioned NuGet packages

- **Status:** Accepted (BLOCK 2, DEC-3). Nothing is published yet.

## Decision

- Each library is a NuGet package with the same id as its assembly:
  - existing: `FSGAP.Abstractions`, `FSGAP.Core`, `FSGAP.Fenix`;
  - later: `FSGAP.SimConnect`.
- All packages share one version, defined once in `Directory.Build.props` (currently **0.2.0**). Inter-package
  dependencies are generated with the same version.
- `dotnet pack` writes the packages to `artifacts/packages/` (git-ignored). Each package ships its XML
  documentation and the repository README.
- Applications reference the packages with an **explicitly pinned version**. They never copy DLLs or source files.
- Planned feed: GitHub Packages for the `stellinaconseil-lang` organization. A local folder feed works for
  development meanwhile.
- Versioning follows semver. While on 0.x, a minor bump may break contracts, and each breaking change is listed in
  the commit and the ADRs.

## Consequences

- Publishing (feed configuration, credentials, CI) is set up when the first application migrates.
- An application can only move to a new FSGAP version by an explicit version change, which is reviewable.
