# Synaptic A220 circuit breakers — qualification evidence

The source of truth for the 55 circuit-breaker recipes of FSGAP.Synaptic (0.13.0-preview.4).

| File | Content |
|---|---|
| `evidence/` | Lab results copied **byte for byte** from the Shadow lab transfer `FSHANGAR_A220_FAILURE_LAB_TRANSFER_20261023` (`docs/a220-failure-lab/`); `.gitattributes` keeps them unconverted. |
| `QUALIFICATION_MATRIX.md` | The 55 breakers with their mapping, reset and direct-trip qualification. Generated, do not edit. |
| `LIVE_QUALIFICATION_PROCEDURE.md` | How to qualify the direct trip of the 54 remaining breakers, in the lab only. |

## Provenance

- Lab: Shadow, `C:\Users\Shadow\source\repos\FSHangar\FSHANGAR-A220-FAILURE-LAB`, branch `experiment/a220-real-failures`
  (uncommitted working-tree files, exported), 2026-10-23.
- Environment: MSFS 2024 (SimConnect 12.2.0.0), Synaptic A220-300 (docs v1.0.11), FSGAP 0.13.0-preview.3.
- Imported on the main PC: `C:\Users\Jean\FSHANGAR-lab-import\20261023\` (copy on `D:\FSHANGAR-backups\20261023-lab-import\`),
  `SHA256SUMS.txt` 100/100 verified; the six files below match it.

| Evidence file | What it proves |
|---|---|
| `A220_CB_MAPPING_55.json` | 55 B: events ↔ 55 L: cells, each event flipping exactly one cell 0 → 1 (automap `--all`) |
| `A220_CB_AUTO_QUALIFICATION.json` | per breaker: actuation, mapping, re-arm by writing the cell = 0, no other cell changed, system recovered (55/55) |
| `A220_CB_DIRECT_L_TRIP_RESULTS.json` | `R B4` (`CB_LIGHTS_DOME`) opened by writing its cell = 1 with no input event, stable, then restored |
| `A220_CB_DIRECT_L_TRIP_REPORT.md` | the report of that run |
| `A220_CB_REARM_TEST_RESULTS.json` | the first re-arm run (`R B4`) |
| `FSGAP_CB_TRIP_CAPABILITIES.json` | ids, systems, master cautions, re-arm and recovery per breaker |

## Regenerating the manifest

```
python -I tools/synaptic-cb-manifest/generate.py
```

The script refuses inconsistent evidence (count, duplicates, cells outside the 55 documented ones, a failed re-arm, a
direct run that is not a clean restored open). Its output is deterministic; `CircuitBreakerTests` checks the embedded
manifest against these files (55 exact correspondences, qualifications, evidence SHA-256).
