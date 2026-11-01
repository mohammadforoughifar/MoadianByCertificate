#!/usr/bin/env python3
"""One-time migration: crop stored user-signature PNGs to their alpha bounds.

Each original file is copied once to <UserSignatures>/originals/<relative path> before
its active counterpart is rewritten. Files under any `originals` directory are skipped.
Requires Pillow: python3 -m pip install Pillow
"""
from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

try:
    from PIL import Image, ImageOps
except ImportError as exc:  # pragma: no cover - dependency message for the operator
    raise SystemExit("Pillow is required. Install it with: python3 -m pip install Pillow") from exc


DEFAULT_ROOT = Path(__file__).resolve().parents[1] / "src" / "Inventory.Api" / "wwwroot" / "uploads" / "UserSignatures"
PADDING = 4


def is_backup(path: Path) -> bool:
    return any(part.lower() == "originals" for part in path.parts)


def migrate(root: Path, dry_run: bool = False) -> tuple[int, int, int]:
    if not root.is_dir():
        raise FileNotFoundError(f"UserSignatures directory not found: {root}")

    backup_root = root / "originals"
    processed = skipped = backed_up = 0
    files = sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() == ".png" and not is_backup(p.relative_to(root)))

    for source in files:
        relative = source.relative_to(root)
        backup = backup_root / relative
        try:
            with Image.open(source) as opened:
                opened.load()
                rgba = opened.convert("RGBA")
        except Exception as exc:
            print(f"SKIP unreadable PNG: {relative}: {exc}", file=sys.stderr)
            skipped += 1
            continue

        # Back up every readable source before making decisions, including empty images.
        # Backups are never overwritten and are excluded from future migration runs.
        if not dry_run and not backup.exists():
            backup.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, backup)
            backed_up += 1

        bounds = rgba.getchannel("A").getbbox()
        if bounds is None:
            print(f"SKIP fully transparent PNG: {relative}")
            skipped += 1
            continue

        left, top, right, bottom = bounds
        cropped = rgba.crop(bounds)
        trimmed = ImageOps.expand(cropped, border=PADDING, fill=(0, 0, 0, 0))
        print(f"{relative}: {rgba.width}x{rgba.height} -> {trimmed.width}x{trimmed.height}")
        if dry_run:
            processed += 1
            continue

        fd, temp_name = tempfile.mkstemp(prefix=source.name + ".", suffix=".tmp", dir=source.parent)
        os.close(fd)
        try:
            trimmed.save(temp_name, format="PNG", optimize=True)
            os.replace(temp_name, source)
        finally:
            if os.path.exists(temp_name):
                os.unlink(temp_name)
        processed += 1

    return processed, skipped, backed_up


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT, help="UserSignatures directory (defaults to the API wwwroot path)")
    parser.add_argument("--dry-run", action="store_true", help="Report proposed crops without changing files")
    args = parser.parse_args()

    processed, skipped, backed_up = migrate(args.root.resolve(), args.dry_run)
    mode = "Dry run" if args.dry_run else "Migration"
    print(f"{mode} complete: {processed} cropped, {skipped} skipped, {backed_up} original backups created.")
    if args.dry_run:
        print("Run again without --dry-run to apply; originals are preserved under UserSignatures/originals/.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
