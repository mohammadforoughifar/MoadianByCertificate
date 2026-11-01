# Attachment preview verification — v3

This is a **test-only** harness. Do not publish `AttachmentPreview.csproj` as your website. The production implementation is in `src/Inventory.Api`, `src/Inventory.Client` and `src/Inventory.Shared` and requires no Python, Playwright, installed Office, LibreOffice, converter executable or cloud preview service.

## What is actually exercised

- Production `AttachmentBox.razor` and `FilePreviewModal.razor`, compiled by `../DocArchiveSecurity/UiProbe` together with **all 299 real Razor components**. They are placed inside a real form to detect unintended form submission.
- The actual `file-preview.js`, vendored PDF.js 6.3.289, attachment controllers, archive permission service/guard, confirmation service, API client, Office renderer and protected download path.
- An isolated SQLite database, JWT authentication, disk **and** database-backed attachments, and a scratch `FileStore`. No production connection string, database, uploads, migration or background indexing is used.
- The browser host uses Interactive Server to drive the actual compiled components; this is not a mock preview UI. The production WebAssembly Client is separately built in Release/Debug and published.

## Recorded outcome

`RENDERER-RESULTS.txt`: **56/56 PASS**. `BROWSER-RESULTS.json`: **78/78 PASS**, Chromium 153.0.8010.12. Archive regression checks: **104/104 PASS**, recorded in `../DocArchiveSecurity/TEST-RESULTS.txt`.

The browser checks cover seven image formats; PDF page pixels, pagination, zoom, password and close; real Word/Excel/PowerPoint/CSV content and embedded images; isolated Office HTML; corrupt/unsupported files and bounded worksheets; 401/403/404, HTML-fallback and network errors; expired confidential confirmation and wrong/correct password; authorization headers; resource cleanup, late responses and no unintended downloads; independent Download grants; server watermark, API logs, no external Office requests and no uncaught JavaScript errors.

This is **not** a claim of exhaustive Office fidelity, every file variant, Windows/IIS runtime validation or testing against a live SQL Server/customer database. See the source-root Persian `FILE-PREVIEW-FIX-FA.md` and `FILE-PREVIEW-VERIFICATION.txt` for deployment scope and limitations.

## Repeat from the source root

Requires .NET 8 SDK. Normal commands (PowerShell or shell):

```text
dotnet restore inventory/Inventory.sln
dotnet build inventory/Inventory.sln -c Release
dotnet restore inventory/tests/AttachmentPreview/AttachmentPreview.csproj
dotnet build inventory/tests/AttachmentPreview/AttachmentPreview.csproj -c Release
dotnet run --project inventory/tests/AttachmentPreview -c Release --no-build -- --verify

dotnet build inventory/tests/DocArchiveSecurity/DocArchiveSecurity.csproj -c Release
dotnet run --project inventory/tests/DocArchiveSecurity -c Release --no-build
```

The fixtures are included; regeneration is optional. The test host refuses to start without an explicit gate, but `--verify` does not need a web host:

```powershell
$env:PREVIEW_TEST_HOST = "1"
# Optional if running from outside the extracted source tree:
# $env:PREVIEW_SOURCE_ROOT = "C:\\path\\TotallCollectionSoftware-fixed"
dotnet run --project inventory/tests/AttachmentPreview -c Release --no-build -- --urls http://0.0.0.0:5127
```

In a **separate** terminal:

```text
python -m pip install playwright
python -m playwright install chromium
python inventory/tests/AttachmentPreview/browser_test.py http://127.0.0.1:5127 results.json
```

On Linux, Chromium's native libraries may also be required (`python -m playwright install --with-deps chromium`). Shell host equivalent: `PREVIEW_TEST_HOST=1 dotnet run --project inventory/tests/AttachmentPreview -c Release --no-build -- --urls http://0.0.0.0:5127`.

On a memory-limited machine, use serial builds, `-m:1 -p:UseSharedCompilation=false -nr:false`, and workstation GC. No production Compile/Razor exclusions are used.

## Fixture coverage and safety

There are **26 files / 27 attachment records** in `Fixtures/manifest.json` (one Office file is reused for a watermarked record). Ordinary records use document 1/version 101; confidential records document 2/version 102; non-confidential watermark document 3/version 103. Users: 101 Read/no Download, 102 Read + separate Download, 103 View-only; author 900.

Sample passwords: confidential confirmation **`fixture-password`**; encrypted PDF **`pdf-fixture`**. These are public synthetic test credentials, not production secrets.

JPEG/PNG/GIF/WebP/BMP/TIFF/SVG, multi-page and encrypted PDF, DOCX/XLSX/PPTX with Persian text/images/tables, quoted CSV, plain text, misleading MIME, legacy Office, corrupt Office/PDF/image, HEIC and a limited worksheet are included. Script-looking content checks escaping and nonexecution. `/uploads` is denied even in the test host. `/__test/token`, `/__test/confirm`, `/__test/expire` and `/__test/logs` exist **only in this gated test project**; never expose it to untrusted users.

Fixture regeneration (test tools only):

```text
python -m pip install Pillow PyMuPDF python-docx openpyxl python-pptx
python inventory/tests/AttachmentPreview/generate_fixtures.py
```

The generator refreshes `Fixtures/manifest.json`. Its sample PDFs are compressed with `deflate=True`. Binary fixtures and all required PDF.js assets are included both in the full source archive and in the binary-capable v3 patches.

The harness normally deletes its scratch database and scratch file directory on shutdown. If forcibly killed, only those `attachment-preview-*` temporary paths may need cleanup; never delete the application's real uploads.
