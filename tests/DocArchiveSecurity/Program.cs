using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Api.Controllers;
using Inventory.Api.Controllers.DocArchive;
using Inventory.Api.Controllers.Export;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Api.Services.Export;
using Inventory.Client.Pages.DocArchive;
using Inventory.Client.Services;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using DocumentService = Inventory.Api.Services.DocArchive.DocumentService;
using User = Inventory.Api.Data.User;

// Relational tests against the REAL AppDbContext and REAL production services/controllers.
// Controller actions are called directly; only notifications, indexing and browser I/O are test doubles.
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}
void Forbidden(IActionResult result, string message) =>
    Check(result is ObjectResult { StatusCode: 403 } || result is ForbidResult, message);
T Value<T>(IActionResult result) => result is OkObjectResult { Value: T value }
    ? value : throw new InvalidOperationException("Expected successful " + typeof(T).Name);
void Stage(string message) => Console.WriteLine($"PASS: {message} ({checks} checks so far)");

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
const int Reader = 101, NoDownloads = 102, InactiveAdmin = 103, Owner = 900, LegacyAdmin = 901, Manager = 902;
db.Users.AddRange(new User { Id = Reader, Username = "department-manager", Role = "Admin" },
    new User { Id = NoDownloads, Username = "reader", Role = "Operator" },
    new User { Id = InactiveAdmin, Username = "disabled-role", Role = "Admin" },
    new User { Id = Owner, Username = "author", Role = "Operator" },
    new User { Id = LegacyAdmin, Username = "legacy-admin", Role = "Admin" },
    new User { Id = Manager, Username = "archive-manager", Role = "Operator" });
db.Roles.AddRange(new Role { Id = 20, Name = "مدیر بخش — فقط خواندن آرشیو" },
    new Role { Id = 30, Name = "inactive-manager", IsActive = false },
    new Role { Id = 40, Name = "archive-management" });
db.Permissions.AddRange(new Permission { Id = 1, Module = "DocArchive", Action = "Read" },
    new Permission { Id = 2, Module = "DocArchive", Action = "Manage" },
    new Permission { Id = 3, Module = "DocArchive", Action = "Export" });
db.RolePermissions.AddRange(new RolePermission { RoleId = 20, PermissionId = 1 },
    new RolePermission { RoleId = 20, PermissionId = 3 },
    new RolePermission { RoleId = 30, PermissionId = 2 }, new RolePermission { RoleId = 40, PermissionId = 2 });
db.UserRoles.AddRange(new UserRole { UserId = Reader, RoleId = 20 },
    new UserRole { UserId = InactiveAdmin, RoleId = 30 }, new UserRole { UserId = Manager, RoleId = 40 });
await db.SaveChangesAsync();
var access = new DocAccessService(db);
var confirm = new DocDownloadConfirmService(new MemoryCache(new MemoryCacheOptions()));
var notify = new SilentNotify();
var index = new RecordingIndex();
var storeDirectory = Path.Combine(AppContext.BaseDirectory, "fixture-files-" + Guid.NewGuid().ToString("N"));
var store = new FileStore(new TestEnvironment(storeDirectory));
var documentService = new DocumentService(db, access, notify);
var guard = new AttachmentGuard(db, access);
var zipService = new DocFolderZipService(db, access, store);
var extractor = new DocTextExtractorService(NullLogger<DocTextExtractorService>.Instance, null!);
var nextFolder = 1000;
var nextDocument = 2000;
DocFolder NewFolderForTest(int? parent = null, bool isPublic = false, bool publicDownload = false)
{
    var folder = new DocFolder { Id = nextFolder++, ParentId = parent, Name = "Folder " + nextFolder,
        CreatedByUserId = Owner, IsPublic = isPublic, PublicCanDownload = publicDownload };
    db.DocFolders.Add(folder);
    return folder;
}
ArchiveDocument NewDocumentForTest(DocFolder folder, int owner = Owner, bool isPublic = false, bool publicDownload = false)
{
    var document = new ArchiveDocument { Id = nextDocument++, FolderId = folder.Id,
        Title = "Document " + nextDocument, Code = "SEC-" + nextDocument, CreatedByUserId = owner,
        CreatedByName = "author", IsPublic = isPublic, PublicCanDownload = publicDownload };
    db.Documents.Add(document);
    return document;
}
void FolderGrant(DocFolder folder, int user, DocAccessLevel level, bool download = false, int role = 0) =>
    db.DocFolderPermissions.Add(new DocFolderPermission { FolderId = folder.Id, UserId = user,
        RoleId = role, Level = level, CanDownload = download });
void DocumentGrant(ArchiveDocument document, int user, DocAccessLevel level, bool download = false, int role = 0) =>
    db.DocumentPermissions.Add(new DocumentPermission { DocumentId = document.Id, UserId = user,
        RoleId = role, Level = level, CanDownload = download });
T As<T>(T controller, int user = Reader, bool rawAdmin = true) where T : ControllerBase
{
    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(ClaimTypes.Name, "test-user"),
        new Claim(ClaimTypes.Role, rawAdmin ? "Admin" : "Operator") }, "test"));
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
    return controller;
}
DocumentsController Documents(int user = Reader, bool rawAdmin = true) => As(new DocumentsController(
    db, access, documentService, null!, confirm, notify), user, rawAdmin);
AttachmentsController Attachments(int user = Reader, bool rawAdmin = true) => As(new AttachmentsController(
    db, store, guard, index, confirm, null!, access), user, rawAdmin);
DocFoldersController Folders(int user = Reader, bool rawAdmin = true) => As(new DocFoldersController(
    db, access, zipService), user, rawAdmin);
DocSearchController Search(int user = Reader, bool rawAdmin = true) => As(new DocSearchController(
    db, access, index, extractor, confirm), user, rawAdmin);

try
{
    // Explicit document Read overrides inherited Full and same-document role Full/download.
    var parent = NewFolderForTest(isPublic: true, publicDownload: true);
    FolderGrant(parent, Reader, DocAccessLevel.Full, true);
    var readDoc = NewDocumentForTest(parent, isPublic: true, publicDownload: true);
    DocumentGrant(readDoc, 0, DocAccessLevel.Full, true, 20);
    DocumentGrant(readDoc, Reader, DocAccessLevel.Read);
    await db.SaveChangesAsync();
    Check(await access.DocumentAccessAsync(Reader, false, readDoc.Id) == (DocAccessLevel.Read, false),
        "personal document Read must cap group, public and parent Full/download");
    Check(!await DocArchiveAuthorization.IsManagerAsync(db, Reader, true), "legacy Admin claim with read-only RBAC is not archive manager");
    Check(await DocArchiveAuthorization.IsManagerAsync(db, LegacyAdmin, true), "legacy admin without RBAC remains exempt");
    Check(await DocArchiveAuthorization.IsManagerAsync(db, Manager, false), "explicit active DocArchive.Manage remains exempt");
    Check(await access.DocumentAccessAsync(Manager, true, readDoc.Id) == (DocAccessLevel.Full, true), "real manager exception");

    // The nearest explicit folder Read wins against parent Full and same-folder role Full.
    var child = NewFolderForTest(parent.Id, isPublic: true, publicDownload: true);
    FolderGrant(child, 0, DocAccessLevel.Full, true, 20);
    FolderGrant(child, Reader, DocAccessLevel.Read);
    var inheritedDoc = NewDocumentForTest(child);
    DocumentGrant(inheritedDoc, 0, DocAccessLevel.Full, true, 20); // personal folder Read beats document role Full too
    var grandchild = NewFolderForTest(child.Id);
    FolderGrant(grandchild, 0, DocAccessLevel.Full, true, 20); // inherited personal Read beats a more local group
    var deepDoc = NewDocumentForTest(grandchild);
    await db.SaveChangesAsync();
    Check(await access.FolderAccessAsync(Reader, false, child.Id) == (DocAccessLevel.Read, false), "child folder Read beats parent and group");
    Check(await access.FolderAccessAsync(Reader, false, grandchild.Id) == (DocAccessLevel.Read, false), "Read-only grant inherits into grandchildren");
    Check(await access.DocumentAccessAsync(Reader, false, inheritedDoc.Id) == (DocAccessLevel.Read, false), "folder Read reaches contained document");
    Check(await access.DocumentAccessAsync(Reader, false, deepDoc.Id) == (DocAccessLevel.Read, false), "folder Read reaches deep document");

    // A specifically granted Write/Read+Download is still useful; downloading never implies writing.
    var writeDoc = NewDocumentForTest(child);
    DocumentGrant(writeDoc, Reader, DocAccessLevel.Write);
    var downloadDoc = NewDocumentForTest(parent);
    DocumentGrant(downloadDoc, Reader, DocAccessLevel.Read, true);
    var viewDoc = NewDocumentForTest(parent);
    DocumentGrant(viewDoc, Reader, DocAccessLevel.View, true);
    var deniedDoc = NewDocumentForTest(parent, isPublic: true, publicDownload: true);
    DocumentGrant(deniedDoc, Reader, DocAccessLevel.None, true);
    var ownerReadDoc = NewDocumentForTest(parent, Reader);
    DocumentGrant(ownerReadDoc, Reader, DocAccessLevel.Read);
    await db.SaveChangesAsync();
    Check(await access.DocumentAccessAsync(Reader, false, writeDoc.Id) == (DocAccessLevel.Write, false), "specific Write overrides folder Read but does not create download");
    Check(await access.DocumentAccessAsync(Reader, false, downloadDoc.Id) == (DocAccessLevel.Read, true), "download is explicitly independent of editing");
    Check(await access.DocumentAccessAsync(Reader, false, viewDoc.Id) == (DocAccessLevel.View, false), "metadata-only View cannot download even with invalid flag");
    Check(await access.DocumentAccessAsync(Reader, false, deniedDoc.Id) == (DocAccessLevel.None, false), "explicit denial cannot be reopened by public or parent");
    Check(await access.DocumentAccessAsync(Reader, false, ownerReadDoc.Id) == (DocAccessLevel.Read, false), "creator fallback cannot override personal Read");
    Check(!(await access.UsersWithFullAccessAsync(ownerReadDoc.Id)).Contains(Reader), "Read-only user is not a Full cartable recipient");
    Check((await access.UsersWithFullAccessAsync(inheritedDoc.Id)).Contains(Owner), "unrestricted creator retains fallback Full");

    // Disabled roles do not grant document access or revive an old Admin claim.
    var inactiveFolder = NewFolderForTest();
    FolderGrant(inactiveFolder, 0, DocAccessLevel.Full, true, 30);
    var inactiveDoc = NewDocumentForTest(inactiveFolder);
    await db.SaveChangesAsync();
    Check(!await DocArchiveAuthorization.IsManagerAsync(db, InactiveAdmin, true), "inactive RBAC cannot fall back to legacy Admin");
    Check(await access.DocumentAccessAsync(InactiveAdmin, false, inactiveDoc.Id) == (DocAccessLevel.None, false), "inactive group grant ignored");

    // Temporary direct access has the same read-only behavior; expiration/revocation is respected.
    var temporaryDoc = NewDocumentForTest(parent);
    var temporary = new DocTemporaryGrant { DocumentId = temporaryDoc.Id, UserId = Reader,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1), GrantedByUserId = Owner };
    db.DocTemporaryGrants.Add(temporary);
    await db.SaveChangesAsync();
    Check(await access.DocumentAccessAsync(Reader, false, temporaryDoc.Id) == (DocAccessLevel.Read, false), "temporary Read caps inherited Full");
    temporary.RevokedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    Check(await access.DocumentAccessAsync(Reader, false, temporaryDoc.Id) == (DocAccessLevel.Full, true), "revoked temporary grant stops applying");
    var expiredFolder = NewFolderForTest();
    var expiredDoc = NewDocumentForTest(expiredFolder);
    db.DocTemporaryGrants.Add(new DocTemporaryGrant { DocumentId = expiredDoc.Id, UserId = NoDownloads,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(-1), CanDownload = true, GrantedByUserId = Owner });
    await db.SaveChangesAsync();
    Check(await access.DocumentAccessAsync(NoDownloads, false, expiredDoc.Id) == (DocAccessLevel.None, false), "expired grant cannot expose document");
    Check(await access.DocumentAccessAsync(0, true, readDoc.Id) == (DocAccessLevel.None, false), "invalid user cannot inherit manager privileges");
    Check(await access.DocumentAccessAsync(Reader, true, int.MaxValue) == (DocAccessLevel.None, false), "nonexistent document is never Full");
    // Group grants still inherit normally if no personal grant exists in the folder chain.
    var roleParent = NewFolderForTest();
    FolderGrant(roleParent, 0, DocAccessLevel.Full, true, 20);
    var roleChild = NewFolderForTest(roleParent.Id);
    FolderGrant(roleChild, 0, DocAccessLevel.Read, false, 20);
    var roleDoc = NewDocumentForTest(roleChild);
    var roleOverrideDoc = NewDocumentForTest(roleChild);
    DocumentGrant(roleOverrideDoc, 0, DocAccessLevel.Write, false, 20);
    var activeTemporaryDoc = NewDocumentForTest(roleParent);
    DocumentGrant(activeTemporaryDoc, 0, DocAccessLevel.Write, true, 20);
    db.DocTemporaryGrants.Add(new DocTemporaryGrant { DocumentId = activeTemporaryDoc.Id, UserId = Reader,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1), GrantedByUserId = Owner });
    db.DocTemporaryGrants.Add(new DocTemporaryGrant { DocumentId = deniedDoc.Id, UserId = Reader,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1), CanDownload = true, GrantedByUserId = Owner });
    await db.SaveChangesAsync();
    Check(await access.DocumentAccessAsync(Reader, false, roleDoc.Id) == (DocAccessLevel.Read, false), "nearest role folder Read caps ancestor role Full");
    Check(await access.DocumentAccessAsync(Reader, false, roleOverrideDoc.Id) == (DocAccessLevel.Write, false), "direct document role beats inherited role, never personal grants");
    Check(await access.DocumentAccessAsync(Reader, false, activeTemporaryDoc.Id) == (DocAccessLevel.Read, false), "active personal temporary grant caps competing roles");
    Check(await access.DocumentAccessAsync(Reader, false, deniedDoc.Id) == (DocAccessLevel.None, false), "temporary grant cannot override a permanent personal denial");
    Stage("permission precedence, separate download, creator/manager exceptions and inactive/temporary roles");

    // SQL and bulk mapping must agree exactly; permission filtering happens before paging/counting.
    foreach (var minimum in new[] { DocAccessLevel.View, DocAccessLevel.Read, DocAccessLevel.Write, DocAccessLevel.Full })
    {
        var q = await DocQuery.AccessibleAsync(db, access, Reader, false, minimum);
        var actual = (await q.Select(d => d.Id).ToListAsync()).OrderBy(id => id).ToArray();
        var ids = await db.Documents.Select(d => d.Id).ToListAsync();
        var map = await access.DocumentAccessMapAsync(Reader, false, ids);
        var expected = map.Where(x => x.Value.Level >= minimum).Select(x => x.Key).OrderBy(id => id).ToArray();
        Check(actual.SequenceEqual(expected), "SQL/bulk parity at " + minimum);
        var sql = q.OrderBy(d => d.Id).Skip(1).Take(2).ToQueryString();
        Check(sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase) && sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase), "access checks and pagination remain relational");
    }
    // SQL Server translation of the actual generated predicate (no SQL Server is running).
    // Rebind ONLY the entity query roots; use the exact runtime ACL expression and folder snapshot.
    await using (var sqlDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=localhost;Database=TranslationOnly;Trusted_Connection=True;TrustServerCertificate=True").Options))
    {
        var migrationAssembly = sqlDb.GetService<IMigrationsAssembly>();
        const string channelMigration = "20261001130000_ItRemoteRequestChannel";
        Check(migrationAssembly.Migrations.Keys.Contains(channelMigration), "repaired IT channel migration is discoverable by EF");
        var channel = migrationAssembly.CreateMigration(migrationAssembly.Migrations[channelMigration], sqlDb.Database.ProviderName!);
        Check(channel.GetType().GetCustomAttributes<DbContextAttribute>().Count() == 1, "migration has exactly one DbContext attribute");
        var contextSnapshots = typeof(AppDbContext).Assembly.GetTypes().Where(t => typeof(ModelSnapshot).IsAssignableFrom(t)
            && t.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(AppDbContext)).ToList();
        Check(contextSnapshots.Count == 1, "production API contains exactly one AppDbContext model snapshot");
        Check(channel.UpOperations.Count > 0, "existing IT-channel Up operations are preserved");
        var migrationScript = sqlDb.GetService<IMigrator>().GenerateScript("20261001120000_ExtraWorkDecision", channelMigration,
            MigrationsSqlGenerationOptions.Idempotent);
        Check(migrationScript.Contains("CREATE TABLE [dbo].[ItClientCompanies]", StringComparison.Ordinal)
            && migrationScript.Contains(channelMigration, StringComparison.Ordinal), "IT-channel idempotent SQL script generates offline");
        var roots = new SqlServerArchiveRoots(sqlDb);
        foreach (var minimum in new[] { DocAccessLevel.View, DocAccessLevel.Read, DocAccessLevel.Write, DocAccessLevel.Full })
        {
            var q = await DocQuery.AccessibleAsync(db, access, Reader, false, minimum);
            var translated = sqlDb.Documents.AsQueryable().Provider.CreateQuery<ArchiveDocument>(roots.Visit(q.Expression)!);
            var sql = translated.OrderBy(d => d.Id).Skip(1).Take(2).ToQueryString();
            Check(sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase), "SQL Server access/paging translation at " + minimum);
        }
    }
    var all = await DocQuery.AccessibleAsync(db, access, Reader, false);
    Check(!await all.AnyAsync(d => d.Id == deniedDoc.Id), "denied document excluded from counts/search");
    var directDto = Value<DocumentDto>(await Documents().Get(readDoc.Id));
    Check(directDto.MyLevel == DocAccessLevelDto.Read && !directDto.MyCanDownload, "detail response is actually Read/no-download");
    var list = Value<List<DocumentListDto>>(await Documents().List(parent.Id));
    Check(list.Single(d => d.Id == readDoc.Id).MyLevel == DocAccessLevelDto.Read
        && !list.Single(d => d.Id == readDoc.Id).MyCanDownload, "list cannot upgrade detail permissions");
    var searched = Value<DocSearchPageDto>(await Search().SearchPage(new DocSearchFilterDto { FolderId = parent.Id, PageSize = 100 }));
    Check(searched.Items.Single(d => d.Id == readDoc.Id).MyLevel == DocAccessLevelDto.Read
        && !searched.Items.Single(d => d.Id == readDoc.Id).MyCanDownload, "search cannot upgrade list permissions");
    Check(!searched.Items.Any(d => d.Id == deniedDoc.Id), "denied document excluded from search page");
    Stage("SQL/bulk/detail/list/search parity and database pagination");

    var readVersion = new DocumentVersion { DocumentId = readDoc.Id, VersionNo = 1, Status = DocVersionStatus.Approved,
        CreatedByUserId = Owner, IsActive = true };
    var downloadVersion = new DocumentVersion { DocumentId = downloadDoc.Id, VersionNo = 1, Status = DocVersionStatus.Approved,
        CreatedByUserId = Owner, IsActive = true };
    var writeVersion = new DocumentVersion { DocumentId = writeDoc.Id, VersionNo = 1, Status = DocVersionStatus.Approved,
        CreatedByUserId = Owner, IsActive = true };
    var viewVersion = new DocumentVersion { DocumentId = viewDoc.Id, VersionNo = 1, CreatedByUserId = Owner };
    db.DocumentVersions.AddRange(readVersion, downloadVersion, writeVersion, viewVersion);
    await db.SaveChangesAsync();
    var attachment = new AppAttachment { Module = "DocVersion", RefId = readVersion.Id, FileName = "protected.txt",
        ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("protected content"), UploaderUserId = Reader };
    var allowedAttachment = new AppAttachment { Module = "DocVersion", RefId = downloadVersion.Id, FileName = "allowed.txt",
        ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("allowed content"), UploaderUserId = Owner };
    var viewAttachment = new AppAttachment { Module = "DocVersion", RefId = viewVersion.Id, FileName = "metadata-only.txt",
        ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("must not be previewed"), UploaderUserId = Owner };
    db.AppAttachments.AddRange(attachment, allowedAttachment, viewAttachment);
    await db.SaveChangesAsync();

    // Direct API requests, not merely hidden buttons, must fail without mutating the database.
    var versionCount = await db.DocumentVersions.CountAsync();
    var attachmentCount = await db.AppAttachments.CountAsync();
    Forbidden(await Documents().Update(readDoc.Id, new DocumentDto { FolderId = parent.Id, Title = "tampered", Code = readDoc.Code }), "direct edit blocked");
    Forbidden(await Documents().CreateVersion(new DocVersionCreateDto { DocumentId = readDoc.Id }), "direct version creation blocked");
    Forbidden(await Documents().Update(inheritedDoc.Id, new DocumentDto()), "inherited Read edit blocked");
    Forbidden(await Documents().CreateVersion(new DocVersionCreateDto { DocumentId = inheritedDoc.Id }), "inherited Read version blocked");
    Forbidden(await Documents().Create(new DocumentDto { FolderId = child.Id }), "read-only folder cannot accept new document");
    Forbidden(await Documents().SavePermissions(readDoc.Id, new DocPermissionsSaveDto { IsPublic = true, PublicCanDownload = true }), "Read cannot grant itself download");
    Forbidden(await Documents().SetActive(readVersion.Id, false), "Read cannot activate/deactivate version");
    Forbidden(await Documents().Delete(readDoc.Id), "Read cannot delete document");
    Forbidden(await Folders().Update(child.Id, new DocFolderDto { Name = "tampered" }), "Read cannot edit folder");
    Forbidden(await Folders().SavePermissions(child.Id, new DocPermissionsSaveDto()), "Read cannot change folder permissions");
    Forbidden(await Folders().Create(new DocFolderDto { ParentId = child.Id, Name = "unauthorized child" }), "Read cannot create subfolder");
    using (var input = new MemoryStream(Encoding.UTF8.GetBytes("new content")))
        Forbidden(await Attachments().Upload("DocVersion", readVersion.Id, new FormFile(input, 0, input.Length, "file", "new.txt")), "Read cannot upload/replace attachment");
    Forbidden(await Attachments().Delete(attachment.Id), "Read cannot delete attachment even if uploader and legacy Admin claim");
    Check(await db.DocumentVersions.CountAsync() == versionCount && await db.AppAttachments.CountAsync() == attachmentCount, "denied calls make no versions or files");
    Check((await db.Documents.FindAsync(readDoc.Id))!.Title == readDoc.Title && !readDoc.IsDeleted, "denied calls preserve original document");
    Check(index.Queued == 0, "denied upload never queues indexing");
    Stage("backend edit/version/delete/upload/permission escalation protections");

    Check(await guard.CheckAsync("DocVersion", readVersion.Id, Reader, true) == AttachmentAccess.PreviewOnly, "attachment guard does not trust raw Admin claim");
    Check(await guard.CheckAsync("DocVersion", viewVersion.Id, Reader, true) == AttachmentAccess.None, "View cannot fetch attachment contents");
    var attachmentResponse = JsonSerializer.SerializeToElement(((OkObjectResult)await Attachments().List("DocVersion", readVersion.Id)).Value);
    Check(!attachmentResponse[0].GetProperty("CanDownload").GetBoolean() && !attachmentResponse[0].GetProperty("CanDelete").GetBoolean(), "attachment DTO has no download/delete");
    Check(await Attachments().Preview(attachment.Id) is FileContentResult, "Read still allows in-app preview");
    Forbidden(await Attachments().Preview(viewAttachment.Id), "metadata-only direct preview denied");
    Forbidden(await Attachments().Download(attachment.Id), "original-file download denied");
    var print = As(new DocPrintController(db, access, confirm, null!, store));
    Forbidden(await print.PrintFile(attachment.Id), "printing cannot bypass separate download flag");
    Forbidden(await Folders().ExportFolderZip(child.Id), "read-only folder ZIP denied");
    FolderGrant(parent, NoDownloads, DocAccessLevel.Read); // cap public download in this fixture too
    var noDownloadFolder = NewFolderForTest();
    FolderGrant(noDownloadFolder, NoDownloads, DocAccessLevel.Read);
    var noDownloadDoc = NewDocumentForTest(noDownloadFolder);
    await db.SaveChangesAsync();
    Forbidden(await Folders(NoDownloads, false).ExportAllZip(), "read-only entire archive ZIP denied");
    var (zipBytes, _) = await zipService.ExportZipAsync(parent.Id, true, false, false, Reader, false);
    using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
    {
        Check(!zip.Entries.Any(e => e.FullName.EndsWith("protected.txt")), "ZIP excludes document explicitly Read/no-download inside downloadable parent");
        Check(zip.Entries.Any(e => e.FullName.EndsWith("allowed.txt")), "ZIP retains independently authorized download");
    }
    var export = new DocArchiveExportService(db, access);
    var exportCatalog = DispatchProxy.Create<IExportService, ExportCatalogProxy>();
    Forbidden(await As(new ExportController(db, exportCatalog, export)).GetReport("doc-history", query: new ExportQuery { Id = readDoc.Id }), "report download cannot bypass Read/no-download");
    var report = await export.BuildAsync("doc-list", new ExportQuery(), Reader, false);
    Check(!report.Rows.Any(r => Equals(r.Values[0], readDoc.Code)), "report excludes read-only document");
    Check(report.Rows.Any(r => Equals(r.Values[0], downloadDoc.Code)), "report retains authorized document");
    db.DocumentLogs.AddRange(new DocumentLog { DocumentId = downloadDoc.Id, Action = "Permissions", Detail = "private access-management data" },
        new DocumentLog { DocumentId = downloadDoc.Id, Action = "Update", Detail = "ordinary history" });
    await db.SaveChangesAsync();
    var history = await export.BuildAsync("doc-history", new ExportQuery { Id = downloadDoc.Id }, Reader, false);
    Check(!history.Rows.Any(r => Equals(r.Values[4], "private access-management data"))
        && history.Rows.Any(r => Equals(r.Values[4], "ordinary history")), "Read+Download history respects Full-only log privacy");
    Check(await Attachments().Download(allowedAttachment.Id) is FileContentResult, "explicit Read+Download can download");
    Forbidden(await Documents().CreateVersion(new DocVersionCreateDto { DocumentId = downloadDoc.Id }), "Read+Download does not imply Write");
    Stage("preview allowed, download/ZIP/print/report blocked without explicit permission");

    // Positive Write test: the fix must not equate upload/edit with permission to download.
    var writeDto = Value<DocumentDto>(await Documents().Get(writeDoc.Id));
    writeDto.Title = "authorized edit";
    writeDto.IsPublic = true;
    writeDto.PublicCanDownload = true;
    Check(await Documents().Update(writeDoc.Id, writeDto) is OkResult, "Write can edit metadata");
    Check(!writeDoc.IsPublic && !writeDoc.PublicCanDownload, "Write cannot change security/public-download flags");
    Check(await Documents().CreateVersion(new DocVersionCreateDto { DocumentId = writeDoc.Id, SendToFlow = false }) is OkObjectResult, "Write can create version");
    using (var input = new MemoryStream(Encoding.UTF8.GetBytes("writer content")))
        Check(await Attachments().Upload("DocVersion", writeVersion.Id, new FormFile(input, 0, input.Length, "file", "writer.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" }) is OkResult,
            "Write/no-download can upload its own file");
    var uploaded = await db.AppAttachments.SingleAsync(a => a.RefId == writeVersion.Id && a.FileName == "writer.txt");
    Forbidden(await Attachments().Download(uploaded.Id), "Write never implicitly grants download");
    Check(await Attachments().Delete(uploaded.Id) is OkResult, "Write can delete own unfrozen attachment without download");
    Check(index.Queued == 1, "authorized upload queues indexing once");
    Stage("authorized Write still works without granting download or security management");

    // Render the REAL Razor components with read-only DTOs, not a fabricated HTML template.
    Check(!new DocAccessRequestApproveDto().CanDownload, "access-request approval defaults to no download");
    var editor = new DocPermissionEditor();
    var privateFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    Check((bool)typeof(DocPermissionEditor).GetField("pickDownload", privateFlags)!.GetValue(editor)! == false, "permission editor defaults to no download");
    var downgraded = new DocPermissionDto { Level = DocAccessLevelDto.Full, CanDownload = true };
    typeof(DocPermissionEditor).GetMethod("SetLevel", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
        new object[] { downgraded, new ChangeEventArgs { Value = "2" } });
    Check(downgraded.Level == DocAccessLevelDto.Read && !downgraded.CanDownload, "downgrading to Read clears old download toggle");
    var pdf = new FilePreviewModal();
    typeof(FilePreviewModal).GetField("kind", privateFlags)!.SetValue(pdf, "pdf");
    typeof(FilePreviewModal).GetField("objectUrl", privateFlags)!.SetValue(pdf, "blob:test");
    typeof(FilePreviewModal).GetField("visible", privateFlags)!.SetValue(pdf, true);
    using var pdfTree = new Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder();
    typeof(FilePreviewModal).GetMethod("BuildRenderTree", privateFlags)!.Invoke(pdf, new object[] { pdfTree });
    var pdfFrames = pdfTree.GetFrames();
    Check(pdfFrames.Array.Take(pdfFrames.Count).Any(f => f.FrameType == Microsoft.AspNetCore.Components.RenderTree.RenderTreeFrameType.Attribute && f.AttributeName == "class" && Equals(f.AttributeValue, "fpv-pdfhost"))
        && !pdfFrames.Array.Take(pdfFrames.Count).Any(f => f.FrameType == Microsoft.AspNetCore.Components.RenderTree.RenderTreeFrameType.Element && f.ElementName == "iframe"),
        "read-only PDF preview renders local canvas host, never the native download/print toolbar");
    var uiDto = Value<DocumentDto>(await Documents().Get(readDoc.Id));
    var uiService = DispatchProxy.Create<IDocArchiveService, UiServiceProxy>();
    ((UiServiceProxy)(object)uiService).Document = uiDto;
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IDocArchiveService>(uiService);
    services.AddSingleton<IAuthState>(new ReadOnlyAuth());
    services.AddSingleton<IToastService, ToastService>();
    services.AddSingleton(new LayoutState());
    services.AddSingleton(new ApiOptions { BaseUrl = "https://test.invalid/" });
    services.AddSingleton<IJSRuntime>(new SilentJs());
    services.AddSingleton<NavigationManager>(new TestNavigation());
    services.AddSingleton(new HttpClient(new AttachmentHttp(attachment.Id)) { BaseAddress = new Uri("https://test.invalid/") });
    await using var provider = services.BuildServiceProvider();
    await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
    var html = await renderer.Dispatcher.InvokeAsync(async () =>
    {
        var rendered = await renderer.RenderComponentAsync<DocumentView>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Id"] = readDoc.Id }));
        // Inline CSS contains selectors such as ".attx-drop {" even if no upload node exists.
        // Inspect rendered markup, never the style/script text, when asserting absent controls.
        var markup = Regex.Replace(rendered.ToHtmlString(), @"<(style|script)\b[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(markup);
    });
    Check(!html.Contains("ورژن جدید"), "Read-only page has no new-version control");
    Check(!html.Contains("ویرایش مدرک"), "Read-only page has no document-edit control");
    Check(!html.Contains("attx-drop ") && !html.Contains("type=\"file\""), "Read-only page has no upload input");
    Check(!html.Contains("title=\"دانلود\"") && !html.Contains("title=\"دانلود فایل\""), "Read-only page has no file-download control");
    Check(html.Contains("protected.txt") && html.Contains("فقط مشاهده"), "Read-only page still renders attachment for preview");
    Check(!html.Contains("bi-printer"), "Read-only page has no print control");
    var explorer = new DocArchiveExplorer();
    typeof(DocArchiveExplorer).GetProperty("Auth", privateFlags)!.SetValue(explorer, new ReadOnlyAuth { StaleManageClaim = true });
    typeof(DocArchiveExplorer).GetField("folders", privateFlags)!.SetValue(explorer, new List<DocFolderDto> {
        new() { Id = child.Id, MyLevel = DocAccessLevelDto.Read, MyCanDownload = false } });
    typeof(DocArchiveExplorer).GetField("selectedFolderId", privateFlags)!.SetValue(explorer, child.Id);
    typeof(DocArchiveExplorer).GetField("folderForm", privateFlags)!.SetValue(explorer, new DocFolderDto { Id = child.Id });
    Check(!(bool)typeof(DocArchiveExplorer).GetProperty("CanDownloadZip", privateFlags)!.GetValue(explorer)!, "stale manager claim cannot display ZIP for current Read/no-download folder");
    Check(!(bool)typeof(DocArchiveExplorer).GetProperty("CanWriteHere", privateFlags)!.GetValue(explorer)!, "current Read folder stays read-only in Explorer despite stale claim");
    Check(!(bool)typeof(DocArchiveExplorer).GetProperty("CanManageFolderAccess", privateFlags)!.GetValue(explorer)!, "current Read folder has no permission editor despite stale claim");
    Stage("real Razor read-only controls and fail-closed defaults");

    var itController = As(new ItRequestsController(db, notify, store, null!), LegacyAdmin, true);
    var missingCompany = await itController.SaveClientCompany(new ItRequestsController.ClientCompanyDto {
        Id = int.MaxValue, Code = "MISSING", Name = "Missing company" });
    Check(missingCompany is NotFoundObjectResult, "missing company edit returns HTTP 404, not an undefined exception");
    Check(await itController.ManagerInbox(onlyExternal: true, companyId: int.MaxValue, q2: "empty") is OkObjectResult,
        "repaired IQueryable supports combined IT manager filters");
    Stage("migration discovery/idempotent SQL and repaired IT controller endpoints");

    Console.WriteLine($"ALL {checks} SECURITY REGRESSION CHECKS PASSED.");
}
finally
{
    if (Directory.Exists(storeDirectory)) Directory.Delete(storeDirectory, true);
}

public sealed class SilentNotify : INotifyService
{
    public Task SendAsync(int id, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task SendManyAsync(IEnumerable<int> ids, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task SendToRoleAsync(string role, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task BroadcastChangedAsync(string scope) => Task.CompletedTask;
}
public sealed class RecordingIndex : IDocIndexService
{
    public int Queued { get; private set; }
    public Task QueueAttachmentIndexingAsync(int id) { Queued++; return Task.CompletedTask; }
    public Task<DocExtractionResult> IndexAttachmentAsync(int id, bool force = false) => throw new NotSupportedException();
    public Task<int> IndexVersionAsync(int id, bool force = false) => throw new NotSupportedException();
    public Task<int> IndexDocumentAsync(int id, bool force = false) => throw new NotSupportedException();
    public Task<DocReindexResultDto> ReindexAllAsync() => throw new NotSupportedException();
}
public sealed class TestEnvironment : IWebHostEnvironment
{
    public TestEnvironment(string path) { ContentRootPath = path; WebRootPath = Path.Combine(path, "wwwroot"); }
    public string ApplicationName { get; set; } = "SecurityTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; }
    public string WebRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
public class ExportCatalogProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "Find"
        ? new ExportReport((string)args![0]!, "History", "Archive", "DocArchive") : throw new NotSupportedException();
}
public class UiServiceProxy : DispatchProxy
{
    public DocumentDto Document { get; set; } = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "GetDocumentAsync" => Task.FromResult(Document),
        "GetLookupsAsync" => Task.FromResult(new DocArchiveLookups()),
        "GetFoldersAsync" => Task.FromResult(new List<DocFolderDto>()),
        "GetExtractedTextsAsync" => Task.FromResult(new List<DocExtractedTextDto>()),
        "GetMyAccessRequestsAsync" or "GetAccessRequestsAsync" => Task.FromResult(new List<DocAccessRequestDto>()),
        "GetTagsAsync" => Task.FromResult(new List<DocTagDto>()),
        _ => throw new NotSupportedException(method?.Name)
    };
}
public sealed class ReadOnlyAuth : IAuthState
{
    public bool IsLoggedIn => true;
    public bool IsAdmin => true; // legacy claim MUST NOT upgrade item-level Read
    public bool IsOperator => false;
    public bool IsReferrer => false;
    public bool CanOperate => true;
    public string? Token => "test-token";
    public int UserId => 101;
    public string DisplayName => "read-only user";
    public string Role => "Admin";
    public int? ReferrerId => null;
    public bool StaleManageClaim { get; init; }
    public bool Has(string permission) => permission is "DocArchive.Read" or "DocArchive.Export"
        || (StaleManageClaim && permission == "DocArchive.Manage");
    public bool HasModule(string module) => module == "DocArchive";
    public bool CanSee(string module) => HasModule(module);
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse response) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
public sealed class SilentJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
}
public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://test.invalid/", "https://test.invalid/doc-archive");
    protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
}
public sealed class AttachmentHttp(int attachmentId) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.RequestUri!.AbsolutePath.Contains("/api/attachments/")
            ? JsonSerializer.Serialize(new[] { new { Id = attachmentId, FileName = "protected.txt", ContentType = "text/plain",
                UploaderName = "author", UploaderUserId = 101, UploadedAt = DateTime.Now, CanPreview = true, CanDownload = false, CanDelete = false } }) : "[]";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

// Provider-only rebind for offline SQL Server translation. No connection or SQL execution.
public sealed class SqlServerArchiveRoots(AppDbContext db) : ExpressionVisitor
{
    private readonly Dictionary<Type, IQueryable> roots = new()
    {
        [typeof(ArchiveDocument)] = db.Documents,
        [typeof(DocumentPermission)] = db.DocumentPermissions,
        [typeof(DocTemporaryGrant)] = db.DocTemporaryGrants
    };
    protected override Expression VisitExtension(Expression node) => node is EntityQueryRootExpression root
        && roots.TryGetValue(root.EntityType.ClrType, out var target) ? target.Expression : base.VisitExtension(node);
    protected override Expression VisitMember(MemberExpression node)
    {
        if (typeof(IQueryable).IsAssignableFrom(node.Type)
            && Expression.Lambda<Func<object?>>(Expression.Convert(node, typeof(object))).Compile()() is IQueryable q)
            return Visit(q.Expression);
        return base.VisitMember(node);
    }
}
