# User signature RGBA regression test

Run from the repository root:

```bash
dotnet run --project inventory/tests/UserSignatureTransparency/UserSignatureTransparency.csproj
```

The probe uses an in-memory SQLite database and the production `AuthController`, `FileStore`, and outgoing-letter PDF builder. It uploads an RGBA PNG containing a transparent corner, white paper, and antialiased ink; checks the stored PNG IHDR is RGBA (color type 6), the alpha-bounds crop leaves four transparent pixels, and original uploads are retained across replacements; then exercises the PDF renderer and PDFsharp letterhead overlay.

Existing on-disk signature PNGs can be migrated with `python3 inventory/tools/CropUserSignatures.py`. The script first preserves originals under `UserSignatures/originals/`; install Pillow with `python3 -m pip install Pillow` if needed. Run with `--dry-run` first to review the crop report.
