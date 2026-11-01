using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// =====================================================================
// اسکیمای مدیریت آی‌پی (IpSubnets + IpAddressEntries)
// بدون نیاز به migration — idempotent روی SQL Server و SQLite.
// =====================================================================
public static class IpamSchemaV1
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<IpSubnet>().HasIndex(s => new { s.CompanyId, s.NetworkNum });
        model.Entity<IpAddressEntry>().HasIndex(e => new { e.SubnetId, e.IpNum }).IsUnique();
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS IpSubnets (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL,
    Name TEXT NOT NULL DEFAULT '',
    Cidr TEXT NOT NULL DEFAULT '',
    NetworkNum INTEGER NOT NULL DEFAULT 0,
    Prefix INTEGER NOT NULL DEFAULT 24,
    Gateway TEXT NULL, Vlan INTEGER NULL, DnsServers TEXT NULL, DhcpRange TEXT NULL,
    Location TEXT NULL, Description TEXT NULL,
    AutoScanMinutes INTEGER NOT NULL DEFAULT 0,
    LastScanAt TEXT NULL,
    LastScanAliveCount INTEGER NOT NULL DEFAULT 0,
    LastScanDurationMs INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TEXT NULL, UpdatedByName TEXT NULL
);");
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS IpAddressEntries (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SubnetId INTEGER NOT NULL,
    Ip TEXT NOT NULL DEFAULT '',
    IpNum INTEGER NOT NULL DEFAULT 0,
    IsAlive INTEGER NOT NULL DEFAULT 0,
    LastSeenAt TEXT NULL, FirstSeenAt TEXT NULL, LastCheckedAt TEXT NULL,
    Mac TEXT NULL, Hostname TEXT NULL, Method TEXT NULL,
    IsReserved INTEGER NOT NULL DEFAULT 0,
    ReservedTitle TEXT NULL, ReservedType TEXT NULL, ReservedOwner TEXT NULL, Note TEXT NULL,
    UpdatedAt TEXT NULL, UpdatedByName TEXT NULL
);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_IpSubnets_CompanyId_NetworkNum ON IpSubnets (CompanyId, NetworkNum);");
                await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_IpAddressEntries_SubnetId_IpNum ON IpAddressEntries (SubnetId, IpNum);");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.IpSubnets', N'U') IS NULL
BEGIN
    CREATE TABLE [IpSubnets](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_IpSubnets] PRIMARY KEY,
        [CompanyId] int NOT NULL,
        [Name] nvarchar(150) NOT NULL CONSTRAINT [DF_IpSubnets_Name] DEFAULT(N''),
        [Cidr] nvarchar(40) NOT NULL CONSTRAINT [DF_IpSubnets_Cidr] DEFAULT(N''),
        [NetworkNum] bigint NOT NULL CONSTRAINT [DF_IpSubnets_NetworkNum] DEFAULT(0),
        [Prefix] int NOT NULL CONSTRAINT [DF_IpSubnets_Prefix] DEFAULT(24),
        [Gateway] nvarchar(40) NULL,
        [Vlan] int NULL,
        [DnsServers] nvarchar(200) NULL,
        [DhcpRange] nvarchar(80) NULL,
        [Location] nvarchar(250) NULL,
        [Description] nvarchar(1000) NULL,
        [AutoScanMinutes] int NOT NULL CONSTRAINT [DF_IpSubnets_AutoScanMinutes] DEFAULT(0),
        [LastScanAt] datetime2 NULL,
        [LastScanAliveCount] int NOT NULL CONSTRAINT [DF_IpSubnets_LastScanAliveCount] DEFAULT(0),
        [LastScanDurationMs] int NOT NULL CONSTRAINT [DF_IpSubnets_LastScanDurationMs] DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_IpSubnets_CreatedAt] DEFAULT(GETDATE()),
        [UpdatedAt] datetime2 NULL,
        [UpdatedByName] nvarchar(200) NULL
    );
    CREATE INDEX [IX_IpSubnets_CompanyId_NetworkNum] ON [IpSubnets]([CompanyId], [NetworkNum]);
END

IF OBJECT_ID(N'dbo.IpAddressEntries', N'U') IS NULL
BEGIN
    CREATE TABLE [IpAddressEntries](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_IpAddressEntries] PRIMARY KEY,
        [SubnetId] int NOT NULL,
        [Ip] nvarchar(40) NOT NULL CONSTRAINT [DF_IpAddressEntries_Ip] DEFAULT(N''),
        [IpNum] bigint NOT NULL CONSTRAINT [DF_IpAddressEntries_IpNum] DEFAULT(0),
        [IsAlive] bit NOT NULL CONSTRAINT [DF_IpAddressEntries_IsAlive] DEFAULT(0),
        [LastSeenAt] datetime2 NULL,
        [FirstSeenAt] datetime2 NULL,
        [LastCheckedAt] datetime2 NULL,
        [Mac] nvarchar(30) NULL,
        [Hostname] nvarchar(200) NULL,
        [Method] nvarchar(10) NULL,
        [IsReserved] bit NOT NULL CONSTRAINT [DF_IpAddressEntries_IsReserved] DEFAULT(0),
        [ReservedTitle] nvarchar(200) NULL,
        [ReservedType] nvarchar(30) NULL,
        [ReservedOwner] nvarchar(200) NULL,
        [Note] nvarchar(500) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedByName] nvarchar(200) NULL
    );
    CREATE UNIQUE INDEX [IX_IpAddressEntries_SubnetId_IpNum] ON [IpAddressEntries]([SubnetId], [IpNum]);
END
");
            Console.WriteLine("[DB] ✔ جدول‌های مدیریت آی‌پی (IpSubnets) آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ساخت جدول‌های مدیریت آی‌پی ناموفق بود: {ex.Message}");
        }
    }
}
