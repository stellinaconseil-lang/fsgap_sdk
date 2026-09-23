# Architecture decision records

Short records of decisions that shape the public contracts. Each states the context, the decision and its
consequences. A decision is changed by adding a new record that supersedes the old one, not by editing history.

| # | Decision | Status | Block |
|---|---|---|---|
| [0001](0001-failure-key-catalog.md) | Failures are identified by an open, normalized `FailureKey`, published per provider in a `FailureCatalog` | Accepted | 2 |
| [0002](0002-server-failure-key.md) | Servers and applications exchange `failure_key`, never a vendor failure id | Accepted (target) | 2 |
| [0003](0003-nuget-distribution.md) | FSGAP is distributed as versioned NuGet packages | Accepted | 2 |
| [0004](0004-simconnect-layer-and-reflection.md) | `FSGAP.SimConnect` is a separate layer; reflection into SimConnect.NET stays internal to it | Accepted | 2 |
| [0005](0005-position-update-rate.md) | Aircraft position is provided at 1 Hz by default | Accepted | 2 |
| [0006](0006-telemetry-freshness.md) | Every known value carries its observation time; stale values become `Unknown` | Accepted | 2 |
| [0007](0007-immutable-snapshots.md) | Published snapshots and models are immutable | Accepted | 2 |
| [0008](0008-simulator-abstractions.md) | Simulator connection, simulation state and aircraft detection are three small contracts observed as latest-value streams | Accepted | 2 |
