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
    var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TotallSoftwareForoughAriya.Api"));
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

        const string usersOrgMigration = "20261102120000_AddUsersOrganizationId";
        Check(db.Database.GetMigrations().Contains(usersOrgMigration), "Users.OrganizationId migration is discoverable by EF");
        var usersOrgScript = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .GenerateScript(moadianV2Migration, usersOrgMigration);
        Check(usersOrgScript.Contains("ALTER TABLE [dbo].[Users] ADD [OrganizationId] int NULL"),
            "Users.OrganizationId SQL Server migration adds OrganizationId to dbo.Users");
        Check(snapshot.FindEntityType("Inventory.Api.Data.User")!.FindProperty("OrganizationId") is not null,
            "snapshot records User.OrganizationId");

        const string article9Migration = "20261103120000_AddMoadianArticle9V79";
        Check(db.Database.GetMigrations().Contains(article9Migration), "Moadian Article 9 V7.9 migration is discoverable by EF");
        var article9Script = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .GenerateScript(usersOrgMigration, article9Migration);
        Check(article9Script.Contains("Article9SendDeadlineDays")
            && article9Script.Contains("[IsArticle9]")
            && article9Script.Contains("[SendRule]")
            && article9Script.Contains("[RegistrationDate]")
            && article9Script.Contains("[Note1]")
            && article9Script.Contains("[Note2]")
            && article9Script.Contains("IX_MdnInvoices_CompanyId_IsArticle9"),
            "Moadian Article 9 V7.9 SQL Server migration adds Article 9 & V7.9 columns and index");
        var mdnProfileSnap = snapshot.FindEntityType("Inventory.Api.Data.MdnTaxpayerProfile")!;
        var mdnInvoiceSnap = snapshot.FindEntityType("Inventory.Api.Data.MdnInvoice")!;
        Check(mdnProfileSnap.FindProperty("Article9SendDeadlineDays") is not null
            && mdnInvoiceSnap.FindProperty("IsArticle9") is not null
            && mdnInvoiceSnap.FindProperty("SendRule") is not null
            && mdnInvoiceSnap.FindProperty("RegistrationDate") is not null
            && mdnInvoiceSnap.FindProperty("Note1") is not null
            && mdnInvoiceSnap.FindProperty("Note2") is not null,
            "snapshot records MdnTaxpayerProfile.Article9SendDeadlineDays and MdnInvoice Article 9 / V7.9 properties");
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
INSERT INTO SystemUsers VALUES (1);
CREATE TABLE Users (Id INTEGER NOT NULL PRIMARY KEY, Username TEXT NOT NULL DEFAULT 'admin', PasswordHash TEXT NOT NULL DEFAULT '', Role TEXT NOT NULL DEFAULT 'Admin', MobileVerified INTEGER NOT NULL DEFAULT 0, EmailVerified INTEGER NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL DEFAULT '2026-11-02 00:00:00');
INSERT INTO Users (Id, Username) VALUES (1, 'admin');";
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
        await Inventory.Api.Data.MoadianCertSchemaV2.EnsureAsync(db);
        await Inventory.Api.Data.MoadianCertSchemaV2.EnsureAsync(db);
        Check(await db.MdnTaxpayerProfiles.Select(p => p.Article9SendDeadlineDays).CountAsync() == 0,
            state + ": MdnTaxpayerProfiles.Article9SendDeadlineDays queryable after idempotent EnsureAsync");
        Check(await db.MdnInvoices.Where(i => i.IsArticle9 && i.SendRule == 1 && i.Note1 != null && i.Note2 != null && i.RegistrationDate != null).CountAsync() == 0,
            state + ": MdnInvoices Article 9 & V7.9 columns queryable after idempotent EnsureAsync");
        var defaultOrg = db.Organizations.FirstOrDefault(o => !o.IsDelete && o.IsActive && o.IsDefault)
            ?? db.Organizations.FirstOrDefault(o => !o.IsDelete && o.IsActive);
        Check(state == "fresh" ? defaultOrg is null : defaultOrg?.NameUniq == "MQ",
            state + ": reported FirstOrDefault query works after repeated upgrade; existing rows preserved");
        Check(db.SystemUsers.Select(u => u.OrganizationId).Single() is null,
            state + ": missing system user organization column repaired without assigning a unit");
        Check(db.Users.Select(u => u.OrganizationId).Single() is null,
            state + ": missing core user organization column repaired without assigning a unit");
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
    {
        Check(ca.HasPrivateKey, "CA can be reloaded with signing key");
        var caPem = File.ReadAllText(first.CaPath);
        var keyPem = File.ReadAllText(keyPath);
        var rawB64NoHeaders = Convert.ToBase64String(ca.RawData);

        // 1. OpenSSL Bag Attributes + UTF-8 BOM + ZWNJ
        var bagCert = "\uFEFF\u200CBag Attributes\n    friendlyName: Taxpayer\nsubject=CN=Test\n" + caPem;
        var parsed1 = Inventory.Api.Services.Moadian.MoadianSecretVault.ParseAndValidatePkcs8Material(keyPem, bagCert, null, null);
        Check(parsed1.Thumbprint == ca.Thumbprint, "Moadian vault parses PEM with BOM, ZWNJ, and Bag Attributes");

        // 2. Double-Base64 (raw Base64 .cer file with UTF-8 BOM uploaded via InputFile)
        var bomRawFileBytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(rawB64NoHeaders)).ToArray();
        var inputFileWrapped = Convert.ToBase64String(bomRawFileBytes);
        var parsed2 = Inventory.Api.Services.Moadian.MoadianSecretVault.ParseAndValidatePkcs8Material(keyPem, inputFileWrapped, null, null);
        Check(parsed2.Thumbprint == ca.Thumbprint, "Moadian vault parses raw Base64 .cer file with UTF-8 BOM uploaded via InputFile");

        // 3. Swapped key & certificate inputs
        var parsed3 = Inventory.Api.Services.Moadian.MoadianSecretVault.ParseAndValidatePkcs8Material(caPem, keyPem, null, null);
        Check(parsed3.Thumbprint == ca.Thumbprint, "Moadian vault auto-corrects swapped key and certificate inputs");

        // 4. CSR file gives clear Persian message instead of FormatException
        var csrError = "";
        try
        {
            Inventory.Api.Services.Moadian.MoadianSecretVault.ParseAndValidatePkcs8Material(
                keyPem,
                "-----BEGIN CERTIFICATE REQUEST-----\nMIIBMTCB2gIBADASMRAwDgYDVQQDDAdUZXN0Q1NS\n-----END CERTIFICATE REQUEST-----",
                null,
                null);
        }
        catch (InvalidOperationException ex) { csrError = ex.Message; }
        Check(csrError.Contains("CSR") && !csrError.Contains("Base-64"), "Moadian vault explains CSR mistake in Persian");

        // 5. Key/cert mismatch reports exact RSA modulus fingerprints and CA certificate role
        using var unrelatedRsa = System.Security.Cryptography.RSA.Create(2048);
        var mismatchError = "";
        try
        {
            Inventory.Api.Services.Moadian.MoadianSecretVault.ParseAndValidatePkcs8Material(
                unrelatedRsa.ExportPkcs8PrivateKeyPem(),
                caPem,
                null,
                null);
        }
        catch (InvalidOperationException ex) { mismatchError = ex.Message; }
        Check(mismatchError.Contains("شناسه ماژول کلید") && mismatchError.Contains("مرکز صدور گواهی"),
            "Moadian vault reports exact RSA modulus fingerprints and CA role on mismatch");

        // 6. PKCS#11 auto-discovery safely scans without throwing when no hardware token is attached
        var pkcs11Scan = Inventory.Api.Services.Moadian.MoadianPkcs11Inspector.Discover(null, null);
        Check(!string.IsNullOrWhiteSpace(pkcs11Scan.Summary), "PKCS#11 auto-discovery returns summary cleanly");

        // 7. Windows Certificate Store inspector normalizes invisible Unicode LTR marks & scans cleanly
        var normalizedThumb = Inventory.Api.Services.Moadian.MoadianWindowsStoreInspector.NormalizeThumbprint("\u200E ab:cd 12-34 \u200F");
        Check(normalizedThumb == "ABCD1234", "Windows Store thumbprint normalizer strips certmgr invisible Unicode marks and separators");
        var winStoreScan = Inventory.Api.Services.Moadian.MoadianWindowsStoreInspector.Discover();
        Check(!string.IsNullOrWhiteSpace(winStoreScan.Summary), "Windows Store discovery returns summary cleanly");

        // 8. Moadian invoice builder fixes 0300101 (midnight UTC day shift), 1301401 (blank bpc), and 14003 (indati2m)
        var midnightDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var normalizedOffset = Inventory.Api.Services.Moadian.MoadianSdkGateway.NormalizeIssueDateForMoadian(midnightDate);
        Check(normalizedOffset.UtcDateTime.Date == new DateTime(2026, 11, 1),
            "Midnight Shamsi/date-picker date stays on same UTC & Iran calendar day (fixes 0300101)");
        Check(Inventory.Api.Services.Moadian.MoadianSdkGateway.NormalizeAsciiDigits(" ۱۲۳۴۵۶۷۸۹۰ ") == "1234567890",
            "Persian digits normalize cleanly to ASCII");

        // 9. Moadian V7.9 Article 9 (insr=1, indati2m, nti1, nti2) & SDK PacketProvider serialization regression checks
        var vault = new Inventory.Api.Services.Moadian.MoadianSecretVault(cfg, env);
        var gw = new Inventory.Api.Services.Moadian.MoadianSdkGateway(vault);
        var testProfile = new Inventory.Api.Data.MdnTaxpayerProfile
        {
            CompanyId = 1,
            MemoryId = "A11226",
            EconomicCode = "10100000000",
            Article9SendDeadlineDays = 12
        };
        var baseLine = new Inventory.Api.Data.MdnInvoiceLine
        {
            RowNumber = 1,
            Sstid = "2720000000000",
            Description = "کالای آزمون",
            Quantity = 1,
            UnitPrice = 1_000_000,
            PriceBeforeDiscount = 1_000_000,
            DiscountAmount = 0,
            PriceAfterDiscount = 1_000_000,
            VatRate = 10,
            VatAmount = 100_000,
            TotalLineAmount = 1_100_000
        };

        // 9a. Normal within-deadline invoice -> insr and indati2m MUST both be omitted (prevents 14003 & Table 86 out-of-pattern)
        var normalInv = new Inventory.Api.Data.MdnInvoice
        {
            SerialDecimal = 101,
            IssueDate = DateTime.UtcNow.AddDays(-2),
            IsArticle9 = false,
            InvoiceType = Inventory.Shared.Dtos.MdnInvoiceType.Type1,
            Pattern = Inventory.Shared.Dtos.MdnInvoicePattern.Sale,
            Subject = Inventory.Shared.Dtos.MdnInvoiceSubject.Original,
            SettlementMethod = Inventory.Shared.Dtos.MdnSettlementMethod.Cash,
            BuyerType = Inventory.Shared.Dtos.MdnTaxpayerType.Legal,
            BuyerEconomicCode = "10100000001",
            BuyerNationalId = "10100000001",
            TotalBeforeDiscount = 1_000_000,
            TotalAfterDiscount = 1_000_000,
            TotalVat = 100_000,
            TotalAmount = 1_100_000,
            CashAmount = 1_000_000,
            Note1 = "قرارداد ۱۴۰۵",
            Note2 = "فاز اول",
            Lines = new List<Inventory.Api.Data.MdnInvoiceLine> { baseLine }
        };
        var normalDto = gw.BuildSdkInvoiceDto(testProfile, normalInv);
        var normalPreviewJson = gw.FormatInvoiceJson(normalDto);
        var v79Serializer = new Inventory.Api.Services.Moadian.MoadianSdkGateway.V79MoadianSerializer();
        var normalWireJson = v79Serializer.Serialize(normalDto);
        using (var normalDoc = System.Text.Json.JsonDocument.Parse(normalWireJson))
        {
            var hdr = normalDoc.RootElement.GetProperty("header");
            Check(!hdr.TryGetProperty("insr", out _), "Normal invoice omits insr on wire (V7.9 Table 86)");
            Check(hdr.TryGetProperty("indati2m", out var ind2) && ind2.ValueKind == System.Text.Json.JsonValueKind.Null,
                "Normal invoice keeps indati2m null on wire (V7.9 Table 5 / fixes 14003)");
            Check(!normalPreviewJson.Contains("\"insr\"") && !normalPreviewJson.Contains("\"indati2m\""),
                "Normal invoice JSON preview omits both insr and indati2m");
            Check(hdr.GetProperty("nti1").GetString() == "قرارداد ۱۴۰۵" && hdr.GetProperty("nti2").GetString() == "فاز اول",
                "V7.9 nti1 and nti2 serialize cleanly in header");
        }

        // 9b. Article 9 past-deadline invoice -> insr = 1 AND indati2m populated with indatim <= indati2m <= now
        var art9Inv = new Inventory.Api.Data.MdnInvoice
        {
            SerialDecimal = 102,
            IssueDate = DateTime.UtcNow.AddDays(-25),
            RegistrationDate = DateTime.UtcNow,
            IsArticle9 = true,
            SendRule = 1,
            InvoiceType = Inventory.Shared.Dtos.MdnInvoiceType.Type1,
            Pattern = Inventory.Shared.Dtos.MdnInvoicePattern.Sale,
            Subject = Inventory.Shared.Dtos.MdnInvoiceSubject.Original,
            SettlementMethod = Inventory.Shared.Dtos.MdnSettlementMethod.Cash,
            BuyerType = Inventory.Shared.Dtos.MdnTaxpayerType.Legal,
            BuyerEconomicCode = "10100000001",
            BuyerNationalId = "10100000001",
            TotalBeforeDiscount = 1_000_000,
            TotalAfterDiscount = 1_000_000,
            TotalVat = 100_000,
            TotalAmount = 1_100_000,
            CashAmount = 1_000_000,
            Lines = new List<Inventory.Api.Data.MdnInvoiceLine> { baseLine }
        };
        var art9Dto = gw.BuildSdkInvoiceDto(testProfile, art9Inv);
        var art9WireJson = v79Serializer.Serialize(art9Dto);
        using (var art9Doc = System.Text.Json.JsonDocument.Parse(art9WireJson))
        {
            var hdr = art9Doc.RootElement.GetProperty("header");
            var indatimVal = hdr.GetProperty("indatim").GetInt64();
            var indati2mVal = hdr.GetProperty("indati2m").GetInt64();
            Check(hdr.GetProperty("insr").GetInt32() == 1, "Article 9 invoice emits header.insr = 1 on wire");
            Check(indati2mVal >= indatimVal, "Article 9 invoice emits header.indati2m >= indatim (V7.9 Table 5)");
        }

        // 9c. Article 9 backdated Cancellation (ins=3) -> insr = 1, indati2m populated, Body empty, tprdis..tbill null
        var art9CancelInv = new Inventory.Api.Data.MdnInvoice
        {
            SerialDecimal = 103,
            IssueDate = DateTime.UtcNow.AddDays(-20),
            RegistrationDate = DateTime.UtcNow,
            IsArticle9 = true,
            SendRule = 1,
            Subject = Inventory.Shared.Dtos.MdnInvoiceSubject.Cancellation,
            ReferenceTaxId = normalDto.Header.taxid
        };
        var art9CancelDto = gw.BuildSdkInvoiceDto(testProfile, art9CancelInv);
        var art9CancelWireJson = v79Serializer.Serialize(art9CancelDto);
        using (var cancelDoc = System.Text.Json.JsonDocument.Parse(art9CancelWireJson))
        {
            var hdr = cancelDoc.RootElement.GetProperty("header");
            Check(hdr.GetProperty("ins").GetInt32() == 3 && hdr.GetProperty("insr").GetInt32() == 1,
                "Backdated Article 9 Cancellation emits ins = 3 and insr = 1");
            Check(hdr.GetProperty("indati2m").ValueKind == System.Text.Json.JsonValueKind.Number,
                "Backdated Article 9 Cancellation populates indati2m");
            Check(hdr.GetProperty("tbill").ValueKind == System.Text.Json.JsonValueKind.Null
                && cancelDoc.RootElement.GetProperty("body").GetArrayLength() == 0,
                "Backdated Article 9 Cancellation keeps amount totals null and body empty (prevents 14022..14028)");
        }

        // 9d. Verify SDK PacketProvider actually signs the serialized JSON containing insr=1 via V79MoadianSerializer
        var capturedPayloads = new List<string>();
        var packetProvider = new TaxCollectData.Library.Providers.PacketProvider(
            new TaxCollectData.Library.Properties.TaxProperties("A11226"),
            new CapturingSignatory(capturedPayloads),
            new TaxCollectData.Library.Cryptography.EmptyEncryptor(),
            v79Serializer);
        var packet = packetProvider.CreateInvoicePacket(art9Dto);
        Check(capturedPayloads.Count > 0 && capturedPayloads[0].Contains("\"insr\":1"),
            "SDK PacketProvider signs payload containing header.insr=1 via V79MoadianSerializer");
        Check(packet.Payload is string pktStr && pktStr.Contains("\"insr\":1"),
            "SDK PacketDto preserves header.insr=1 through encryption pipeline");
    }
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

sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "DatabaseDeploymentTests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class CapturingSignatory : TaxCollectData.Library.Abstraction.Cryptography.ISignatory
{
    private readonly List<string> _captured;
    public CapturingSignatory(List<string> captured) => _captured = captured;

    public string Sign(string text)
    {
        _captured.Add(text);
        return text;
    }

    public string Sign(object data)
    {
        var s = data?.ToString() ?? string.Empty;
        _captured.Add(s);
        return s;
    }
}


