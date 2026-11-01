using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.FileProviders;

// Offline regressions: only temporary local SQLite databases; no SQL Server connection or web server.
var root = Path.Combine(Path.GetTempPath(), "totall-deployment-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var originalDirectory = Directory.GetCurrentDirectory();
var originalConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
var originalHttpsDir = Environment.GetEnvironmentVariable("HTTPS_DATA_DIR");
var originalHttpsPort = Environment.GetEnvironmentVariable("HTTPS_PORT");
try
{
    var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Inventory.Api"));
    Directory.SetCurrentDirectory(project);
    Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Server=env-test;Database=OtherDb;Integrated Security=True");
    using (var db = new Inventory.Api.Data.DesignTimeDbContextFactory().CreateDbContext(new[] { "--Database:Provider", "SqlServer" }))
        Check(db.Database.GetConnectionString()!.Contains("env-test"), "factory reads deployment environment instead of a fixed database");
    var overrides = new[] { "--Database:Provider", "SqlServer", "--ConnectionStrings:Default", "Server=cli-test;Database=ThirdDb;Integrated Security=True" };
    using (var db = new Inventory.Api.Data.DesignTimeDbContextFactory().CreateDbContext(overrides))
    {
        Check(db.Database.GetConnectionString()!.Contains("cli-test"), "CLI overrides environment");
        const string organizationMigration = "20261021120000_AddOrganizationCompanyAndUserOrganizationId";
        Check(db.Database.GetMigrations().Contains(organizationMigration), "organization migration is discoverable by EF");
        var script = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .GenerateScript("20261011080837_AddMoadianV1", organizationMigration);
        Check(script.Contains("ADD [CompanyId] int NULL") && script.Contains("ADD [OrganizationId] int NULL"),
            "SQL Server migration script upgrades both required columns");
        var snapshot = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>().ModelSnapshot!.Model;
        Check(snapshot.FindEntityType("Inventory.Api.Data.Organization")!.FindProperty("CompanyId") is not null
            && snapshot.FindEntityType("Inventory.Api.Data.SystemUser")!.FindProperty("OrganizationId") is not null,
            "snapshot records both organization relationships");

        const string moadianV2Migration = "20261101120000_ReplaceLegacyMoadianWithCertSdkV2";
        Check(db.Database.GetMigrations().Contains(moadianV2Migration), "Moadian SDK 2.0.29 migration is discoverable by EF");
        var moadianScript = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .GenerateScript(organizationMigration, moadianV2Migration);
        Check(moadianScript.Contains("DROP TABLE IF EXISTS [dbo].[InvoiceHeader]")
            && moadianScript.Contains("CREATE TABLE [dbo].[MdnInvoices]"),
            "Moadian SDK 2.0.29 SQL Server migration drops legacy tables and creates Mdn* tables");
        Check(snapshot.FindEntityType("Inventory.Api.Data.MdnInvoice") is not null
            && snapshot.FindEntityType("Inventory.Api.Data.MoadianInvoiceHeader") is null,
            "snapshot replaces legacy Moadian entities with Mdn* SDK 2.0.29 entities");
    }

    foreach (var state in new[] { "fresh", "legacy", "partial" })
    {
        var sqliteConnection = $"Data Source={Path.Combine(root, state + ".db")};Pooling=False";
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(sqliteConnection))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE SystemCompanies (Id INTEGER NOT NULL PRIMARY KEY);
INSERT INTO SystemCompanies VALUES (1);
CREATE TABLE SystemUsers (Id INTEGER NOT NULL PRIMARY KEY);
INSERT INTO SystemUsers VALUES (1);";
            await command.ExecuteNonQueryAsync();
            if (state != "fresh")
            {
                command.CommandText = @"
CREATE TABLE Organizations (
    OrganizationId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    NameUnit TEXT NOT NULL, NameUniq TEXT NOT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1,
    IsDelete INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);
INSERT INTO Organizations (NameUnit, NameUniq, IsDefault, CreatedAt)
VALUES ('Original unit', 'MQ', 1, '2026-10-22 00:00:00');";
                await command.ExecuteNonQueryAsync();
                if (state == "partial")
                {
                    command.CommandText = @"
ALTER TABLE Organizations ADD COLUMN CompanyId INTEGER NULL;
UPDATE Organizations SET CompanyId = 1;";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        await using var db = new Inventory.Api.Data.AppDbContext(
            new DbContextOptionsBuilder<Inventory.Api.Data.AppDbContext>().UseSqlite(sqliteConnection).Options);
        await Inventory.Api.Data.OrganizationSchemaV1.EnsureAsync(db);
        await Inventory.Api.Data.OrganizationSchemaV1.EnsureAsync(db);
        var defaultOrg = db.Organizations.FirstOrDefault(o => !o.IsDelete && o.IsActive && o.IsDefault)
            ?? db.Organizations.FirstOrDefault(o => !o.IsDelete && o.IsActive);
        Check(state == "fresh" ? defaultOrg is null : defaultOrg?.NameUniq == "MQ",
            state + ": reported FirstOrDefault query works after repeated upgrade; existing rows preserved");
        Check(db.SystemUsers.Select(u => u.OrganizationId).Single() is null,
            state + ": missing user organization column repaired without assigning a unit");
        if (state == "partial")
            Check(defaultOrg!.CompanyId == 1, "partial: existing company assignment is not overwritten");
    }
    using (var db = new RadisHr.Api.Data.DesignTimeDbContextFactory().CreateDbContext(overrides))
    {
        Check(db.Database.GetConnectionString()!.Contains("cli-test"), "RADIS-HR uses the same selected connection");
        var script = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository>().GetCreateScript();
        Check(script.Contains("__EFMigrationsHistory_RadisHr"), "RADIS-HR retains separate history");
    }

    var socket = new TcpListener(IPAddress.Loopback, 0);
    socket.Start();
    var port = ((IPEndPoint)socket.LocalEndpoint).Port;
    socket.Stop();
    Environment.SetEnvironmentVariable("HTTPS_DATA_DIR", null);
    Environment.SetEnvironmentVariable("HTTPS_PORT", null);
    var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Https:Enabled"] = "true",
        ["Https:DataDir"] = Path.Combine(root, "certs"),
        ["Https:Port"] = port.ToString()
    }).Build();
    var env = new TestEnvironment { ContentRootPath = root };
    var skipped = HttpsCertificates.Prepare(env, cfg, skipPreparation: true);
    Check(!skipped.Enabled && !Directory.Exists(cfg["Https:DataDir"]), "maintenance skips certificate side effects");

    var first = HttpsCertificates.Prepare(env, cfg);
    Check(first.Certificate is { HasPrivateKey: true }, "initial certificate creation");
    var fingerprint = first.ReadMeta()!.CaFingerprint;
    var keyPath = Path.Combine(first.DataDir, "ca.key");
    Check(File.Exists(keyPath), "CA private key persisted");
    if (!OperatingSystem.IsWindows())
        Check(File.GetUnixFileMode(keyPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "CA key permissions are owner-only");
    using (var ca = X509Certificate2.CreateFromPemFile(first.CaPath, keyPath))
        Check(ca.HasPrivateKey, "CA can be reloaded with signing key");
    first.Certificate?.Dispose();

    var renewed = HttpsCertificates.Regenerate(env, cfg, newCa: false);
    Check(renewed.Certificate is { HasPrivateKey: true }, "renewal succeeds after restart");
    Check(renewed.ReadMeta()!.CaFingerprint == fingerprint, "renewal preserves trusted CA");
    renewed.Certificate?.Dispose();

    File.Delete(keyPath); // Simulate an older deployment which saved only ca.crt.
    var legacy = HttpsCertificates.Prepare(env, cfg);
    Check(legacy.Certificate is not null && legacy.ReadMeta()!.CaFingerprint == fingerprint,
        "working legacy certificate is not unnecessarily rotated");
    legacy.Certificate?.Dispose();
    var recovered = HttpsCertificates.Regenerate(env, cfg, newCa: false);
    Check(recovered.Certificate is { HasPrivateKey: true }, "legacy installation recovers when renewal is required");
    Check(recovered.ReadMeta()!.CaFingerprint != fingerprint, "missing legacy key requires a new CA");
    Check(recovered.Messages.Any(m => m.Contains("دوباره نصب")), "CA rotation informs administrator about device reinstallation");
    Check(Directory.GetFiles(recovered.DataDir, "ca.crt.*.bak").Length == 1, "legacy public CA backed up");
    recovered.Certificate?.Dispose();
    Console.WriteLine("Database deployment regression checks passed.");
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    Environment.SetEnvironmentVariable("ConnectionStrings__Default", originalConnection);
    Environment.SetEnvironmentVariable("HTTPS_DATA_DIR", originalHttpsDir);
    Environment.SetEnvironmentVariable("HTTPS_PORT", originalHttpsPort);
    Directory.Delete(root, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "DatabaseDeploymentTests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
