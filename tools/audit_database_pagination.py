#!/usr/bin/env python3
"""Report known in-memory paging patterns; this is an audit, not a compiler/test.

Run from any directory. Exit status is nonzero with --strict while findings remain.
A finding needs semantic review: print layout, static metadata and file imports
are not necessarily database collection pagination.
"""
from pathlib import Path
import argparse
import re

ROOT = Path(__file__).resolve().parents[1]
PATTERNS = {
    "API materialized result": (ROOT / "src/Inventory.Api", "*.cs", r"Paging\.Result\("),
    "API materialized nested result": (ROOT / "src/Inventory.Api", "*.cs", r"Paging\.Slice\("),
    "Client local slicing (review required)": (ROOT / "src/Inventory.Client", "*.razor", r"\.Skip\("),
}


def findings():
    for label, (root, glob, pattern) in PATTERNS.items():
        for file in sorted(root.rglob(glob)):
            for line_no, line in enumerate(file.read_text(encoding="utf-8-sig").splitlines(), 1):
                if re.search(pattern, line):
                    yield label, file.relative_to(ROOT).as_posix(), line_no, line.strip()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--strict", action="store_true", help="Fail while any review findings remain")
    args = parser.parse_args()
    rows = list(findings())
    for label in PATTERNS:
        group = [r for r in rows if r[0] == label]
        print(f"\n## {label}: {len(group)}\n")
        for _, file, line, text in group:
            print(f"- {file}:{line}: {text}")
    print(f"\nTotal review findings: {len(rows)}")
    raise SystemExit(1 if args.strict and rows else 0)
