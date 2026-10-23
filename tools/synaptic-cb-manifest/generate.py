"""Generates the embedded Synaptic A220 circuit-breaker manifest from the lab evidence.

Input (read-only):  docs/qualification/synaptic-a220-circuit-breakers/evidence/*.json
                    docs/audits/synaptic-a220-variable-mapping.csv (the 55 documented L: cells)
Output:             src/FSGAP.Synaptic/CircuitBreakers/Resources/synaptic-a220-circuit-breakers.json
                    docs/qualification/synaptic-a220-circuit-breakers/QUALIFICATION_MATRIX.md

Nothing is typed by hand: every breaker, cell and qualification comes from the evidence files, and the script refuses
to write anything when the evidence is inconsistent. Output is deterministic (sorted, LF, no timestamp), so a re-run on
unchanged evidence is a no-op and a test compares the embedded manifest with the evidence.

Run from the repository root:  python -I tools/synaptic-cb-manifest/generate.py
"""

import csv
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "docs" / "qualification" / "synaptic-a220-circuit-breakers" / "evidence"
CSV = ROOT / "docs" / "audits" / "synaptic-a220-variable-mapping.csv"
MANIFEST = ROOT / "src" / "FSGAP.Synaptic" / "CircuitBreakers" / "Resources" / "synaptic-a220-circuit-breakers.json"
MATRIX = ROOT / "docs" / "qualification" / "synaptic-a220-circuit-breakers" / "QUALIFICATION_MATRIX.md"

SYNAPTIC_VERSION = "1.0.11"
KEY_PREFIX = "electrical.circuit-breaker."
CELL_PREFIX = "L:A22X Circuit Breaker "
KEY_RE = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*){1,7}$")
CELL_RE = re.compile(r"^L:A22X Circuit Breaker (?P<side>[LR]) (?P<position>[A-I][1-5])$")
EVIDENCE_FILES = [
    "A220_CB_MAPPING_55.json",
    "A220_CB_AUTO_QUALIFICATION.json",
    "A220_CB_DIRECT_L_TRIP_RESULTS.json",
    "A220_CB_REARM_TEST_RESULTS.json",
    "FSGAP_CB_TRIP_CAPABILITIES.json",
]


def fail(message: str) -> None:
    sys.exit(f"refused: {message}")


def load(name: str):
    return json.loads((EVIDENCE / name).read_text(encoding="utf-8-sig"))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    mapping = load("A220_CB_MAPPING_55.json")
    qualification = load("A220_CB_AUTO_QUALIFICATION.json")
    direct = load("A220_CB_DIRECT_L_TRIP_RESULTS.json")
    capabilities = load("FSGAP_CB_TRIP_CAPABILITIES.json")

    if capabilities["source"]["synaptic_docs"] != "v" + SYNAPTIC_VERSION:
        fail(f"capabilities are for Synaptic {capabilities['source']['synaptic_docs']}, not v{SYNAPTIC_VERSION}")

    # 55 confirmed, unique B: -> L: pairs.
    pairs = mapping["mappings"]
    if mapping["count"] != 55 or len(pairs) != 55 or not all(p["confirmed"] for p in pairs):
        fail("the mapping does not hold 55 confirmed pairs")
    for field in ("b", "l", "hash"):
        if len({p[field] for p in pairs}) != 55:
            fail(f"duplicate '{field}' in the mapping")
    cell_by_event = {p["b"]: p["l"] for p in pairs}

    # The cells are exactly the 55 documented ones.
    with CSV.open(encoding="utf-8") as handle:
        documented = {row["variable"] for row in csv.DictReader(handle) if row["variable"].startswith(CELL_PREFIX)}
    if documented != set(cell_by_event.values()):
        fail("the mapped cells differ from the 55 documented cells")

    # Automatic qualification: mapping and re-arm (L:=0) passed for every breaker, with no side effect.
    qualified = {b["name"]: b for b in qualification["breakers"]}
    if set(qualified) != set(cell_by_event):
        fail("the qualification run does not cover exactly the mapped breakers")
    for name, run in qualified.items():
        if run["l_cell"] != cell_by_event[name]:
            fail(f"{name}: qualification cell {run['l_cell']} differs from the mapping")
        for flag in ("actuation_pass", "mapping_pass", "rearm_pass", "others_unchanged", "system_recovery_pass"):
            if run.get(flag) is not True:
                fail(f"{name}: {flag} is not true")
        if run.get("cell_after_rearm") != 0:
            fail(f"{name}: the cell did not read 0 after the re-arm")
    if qualification["summary"]["final_l_all_zero"] is not True:
        fail("the qualification run did not end with every cell at 0")

    # Direct L:=1 trip: proven only where the dedicated run confirms it.
    direct_ok = (
        direct["b_writes"] == 0
        and direct["write_accepted"] is True
        and direct["cells_flipped_up"] == [direct["target_cell"]]
        and direct["other_cells_changed"] == []
        and direct["position_stable"] is True
        and direct["physical_open_confirmed"] is True
        and direct["restored"] is True
        and direct["all_l_zero_after_restore"] is True
    )
    if not direct_ok:
        fail("the direct L:=1 run is not a clean confirmed open and restore")
    direct_event = direct["breaker"]
    if cell_by_event.get(direct_event) != direct["target_cell"]:
        fail("the direct L:=1 run targets a cell that is not the mapped one")

    by_event = {b["b_event"]: b for b in capabilities["breakers"]}
    if set(by_event) != set(cell_by_event):
        fail("the capability list does not cover exactly the mapped breakers")

    breakers = []
    for event in sorted(cell_by_event):
        capability = by_event[event]
        cell = cell_by_event[event]
        match = CELL_RE.match(cell)
        if not match or capability["l_cell"] != cell:
            fail(f"{event}: unexpected cell {cell}")
        if not event.startswith("AIRLINER_CB_"):
            fail(f"{event}: unexpected input event name")
        breaker_id = "CB_" + event[len("AIRLINER_CB_"):]
        if capability["id"] != breaker_id:
            fail(f"{event}: capability id {capability['id']} differs from {breaker_id}")
        if capability["rearm_verified"] is not True or capability["recovery_verified"] is not True:
            fail(f"{event}: re-arm or recovery not verified in the capability list")
        key = KEY_PREFIX + breaker_id[3:].lower().replace("_", "-")
        if not KEY_RE.match(key) or len(key) > 128:
            fail(f"{event}: invalid failure key {key}")
        label = breaker_id[3:].replace("_", " ")
        breakers.append({
            "id": breaker_id,
            "failureKey": key,
            "displayName": f"Circuit breaker {label} (panel {match['side']} {match['position']})",
            "cell": cell,
            "system": capability["system"],
            "mapping": "LabValidated",
            "reset": "LabValidated",
            "trip": "LabValidated" if event == direct_event else "Unqualified",
            "masterCaution": capability["master_caution"] is True,
            "labInputEvent": event,
        })

    for field in ("id", "failureKey", "cell", "displayName"):
        if len({b[field] for b in breakers}) != 55:
            fail(f"duplicate {field} in the generated manifest")

    manifest = {
        "schema": "fsgap.synaptic.circuit-breakers/1",
        "aircraft": "synaptic.a220-300",
        "synapticVersion": SYNAPTIC_VERSION,
        "trip": "write the breaker cell = 1 (L: only, through the session's variable writer)",
        "read": "read the breaker cell (1 = tripped / out, 0 = in)",
        "reset": "write the breaker cell = 0, read back 0",
        "evidence": {name: sha256(EVIDENCE / name) for name in EVIDENCE_FILES},
        "breakers": breakers,
    }
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=True) + "\n", encoding="utf-8", newline="\n")

    lines = [
        "# Synaptic A220 circuit breakers - qualification matrix",
        "",
        f"Generated by `tools/synaptic-cb-manifest/generate.py` from `evidence/` (Synaptic A220 v{SYNAPTIC_VERSION}). Do not edit by hand.",
        "",
        "| Level | Meaning |",
        "|---|---|",
        "| Mapping `LabValidated` | the lab tripped the breaker and exactly this one L: cell went 0 -> 1 (automap, 55/55) |",
        "| Reset `LabValidated` | writing the cell = 0 re-armed it, read back 0, no other cell changed, system recovered (55/55) |",
        "| Trip `LabValidated` | writing the cell = 1 directly (no input event) opened it, stable, then restored (direct L:=1 run) |",
        "| Trip `Unqualified` | direct L:=1 opening not yet proven live: listed, never executable unless the application qualifies it |",
        "",
        "| # | Id | Failure key | Cell | System | Mapping | Reset | Trip (L:=1) | Master caution |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for index, b in enumerate(breakers, 1):
        lines.append(
            f"| {index} | `{b['id']}` | `{b['failureKey']}` | `{b['cell'][len(CELL_PREFIX):]}` | {b['system']} | {b['mapping']} | "
            f"{b['reset']} | {b['trip']} | {'yes' if b['masterCaution'] else 'no'} |"
        )
    trip_ok = sum(b["trip"] == "LabValidated" for b in breakers)
    lines += [
        "",
        f"Totals: 55 breakers, 55 mappings validated, 55 resets validated, {trip_ok} direct trip validated, {55 - trip_ok} direct trips unqualified.",
        "",
        "Evidence (SHA-256):",
        "",
    ]
    lines += [f"- `{name}` `{digest}`" for name, digest in manifest["evidence"].items()]
    MATRIX.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"manifest: {len(breakers)} breakers, {trip_ok} trip-qualified -> {MANIFEST.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
