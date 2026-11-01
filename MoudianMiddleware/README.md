# MoudianMiddleware

.NET 8 multi-tenant invoice management and Moadian integration, organized as `Contracts`, `DataLayer`, `CoreLayer`, `Api`, and a Blazor WebAssembly `Client`. The Client references **only** `MoudianMiddleware.Contracts`.

## SDK restore

`TaxCollectData.Library` 2.0.29 is not published on nuget.org. From this directory, run the pinned bootstrap script, then restore/build/test:

```powershell
.\scripts\Restore-TaxCollectDataSdk.ps1
dotnet restore .\MoudianMiddleware.sln
dotnet build .\MoudianMiddleware.sln
dotnet test .\MoudianMiddleware.sln
```

The script downloads the package from a pinned GitHub revision, checks the Git blob SHA-1, and writes it to ignored `.nuget-sdk`. Do not substitute SDK 0.0.34: its API is incompatible. The SDK mapper is `src/MoudianMiddleware.CoreLayer/Infrastructure/Moadian/MoadianInvoiceMapper.cs`; it is the sole Entity-to-SDK numeric boundary (`long` rial amounts remain `long`, `decimal` quantity/rates remain `decimal`).

## Required environment

Secrets and the SQL connection string are read from environment variables only; do not place them in appsettings or tracked files.

- `MOUDIAN__CONNECTION_STRING`: SQL Server connection string.
- `MOUDIAN__JWT_SIGNING_KEY`: Base64 encoding of at least 32 random bytes in non-Development environments. The checked-in API launch profile sets `ASPNETCORE_ENVIRONMENT=Development`; only in that environment, if this variable is absent, the API creates an ephemeral 32-byte key at startup and logs a warning. Tokens become invalid when the API restarts. Set a stable key for persistent local sessions; production always requires this variable.
- `MOUDIAN__JWT_ISSUER`, `MOUDIAN__JWT_AUDIENCE`: optional JWT identity values.
- `MOUDIAN__MASTER_KEY`: Base64 32-byte KEK v1, or `MOUDIAN__MASTER_KEY_V1`, `MOUDIAN__MASTER_KEY_V2`, … and `MOUDIAN__MASTER_KEY_ACTIVE_VERSION` for rotation.
- `MOUDIAN__CLIENT_ORIGIN`: exact HTTPS client origin allowed by CORS (loopback HTTP is allowed for local development).
- `MOUDIAN__FORWARDED_HEADERS_KNOWN_PROXIES`: comma-separated trusted proxy IPs. Forwarded headers from other peers are not trusted.

For a design-time tenant migration, set `MOUDIAN__TENANT_ID`. Workers/seeding use a scoped `ITenantScope.Enter(tenantId)`; create one DI scope per tenant operation.

## Client security and local storage policy

Access and refresh tokens are kept only in the Client's in-memory `TokenStore`; neither `localStorage` nor `sessionStorage` is used. This limits persistence and reduces exposure to token theft after a browser profile is closed, at the cost of requiring a new login after reload. A same-origin BFF with `HttpOnly; Secure; SameSite` cookies would reduce JavaScript token exposure further, but is not implemented by this bearer-token API.

Certificate/private-key files are sent only over HTTPS. The Client keeps file bytes only transiently, clears byte arrays and file controls after upload, and never logs credentials. The browser necessarily creates a short-lived managed string for the PEM JSON request; JavaScript/.NET strings cannot be reliably zeroed, so environments requiring stronger key custody should use a BFF or hardware-backed signing flow instead of browser upload.

## Integration tests using SQL Server

SQLite in-memory remains the fast default for relational tests. The opt-in SQL Server concurrency tests create a uniquely named temporary database and delete only that database after completion. Set `MOUDIAN_TEST_SQLSERVER` to a SQL Server connection string whose login is allowed to create/drop databases. Tests are skipped when it is unset. Never point this variable at a production server/account.
