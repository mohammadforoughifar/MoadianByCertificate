using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Api.Services.Watermark;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using User = Inventory.Api.Data.User;

namespace Inventory.PreviewTests;

public static class PreviewProgram
{
    public static async Task Main(string[] args)
    {
        var source = FindSource();
        var fixtureDir = Path.Combine(source, "inventory/tests/AttachmentPreview/Fixtures");
        PreviewVerification.Run(fixtureDir);
        if (args.Contains("--verify")) return;
        if (Environment.GetEnvironmentVariable("PREVIEW_TEST_HOST") != "1")
            throw new InvalidOperationException("Test-only host. Set PREVIEW_TEST_HOST=1 explicitly; never publish this as the application.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = source, WebRootPath = Path.Combine(source, "inventory/src/Inventory.Client/wwwroot") });
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        var database = Path.Combine(Path.GetTempPath(), "attachment-preview-" + Guid.NewGuid().ToString("N") + ".sqlite");
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=" + database));
        builder.Services.AddMemoryCache();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IDocDownloadConfirmService, DocDownloadConfirmService>();
        builder.Services.AddScoped<IDocAccessService, DocAccessService>();
        builder.Services.AddScoped<IAttachmentGuard, AttachmentGuard>();
        builder.Services.AddScoped(sp => new FileStore(new FixtureFileEnvironment(database + "-files")));
        builder.Services.AddScoped<IServerWatermarkService, ServerWatermarkService>();
        builder.Services.AddScoped<IDocIndexService, NoBackgroundIndex>();
        builder.Services.AddScoped<IDocumentService, DocumentService>();
        builder.Services.AddScoped<INotifyService, NoNotifications>();
        builder.Services.AddSingleton<DocExpiryWatcher>(); // Not registered as a hosted worker.
        builder.Services.AddScoped<FixtureAuthState>();
        builder.Services.AddScoped<IAuthState>(sp => sp.GetRequiredService<FixtureAuthState>());
        builder.Services.AddScoped<IToastService, ToastService>();
        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().BaseUri) });
        builder.Services.AddScoped(sp => new ApiOptions { BaseUrl = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().BaseUri.TrimEnd('/') });
        builder.Services.AddScoped<IApiClient, ApiClient>();
        builder.Services.AddScoped<IDocArchiveService, DocArchiveService>();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddControllers().AddApplicationPart(typeof(AttachmentsController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = FixtureAuthState.Issuer, ValidAudience = FixtureAuthState.Issuer,
                IssuerSigningKey = FixtureAuthState.Key, ClockSkew = TimeSpan.Zero
            };
        });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        await Seed(app.Services, fixtureDir);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".bcmap"] = "application/octet-stream"; types.Mappings[".pfb"] = "application/octet-stream";
        types.Mappings[".wasm"] = "application/wasm";
        // The real application's protected-upload behavior: test fixtures cannot be fetched as static files.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/uploads")) { ctx.Response.StatusCode = 403; return; }
            await next();
        });
        app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = types });
        app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
        app.MapControllers();
        // Test-only controls invalidate the SAME real confirmation cache used by production controllers.
        app.MapPost("/__test/expire/{id:int}", (int id, IMemoryCache cache) => { cache.Remove("docdl:101:" + id); return Results.Ok(); });
        app.MapPost("/__test/confirm/{id:int}", (int id, IDocDownloadConfirmService c) => { c.Confirm(101, id); return Results.Ok(); });
        app.MapGet("/__test/token/{id:int}", (int id) =>
        {
            if (id is not 101 and not 102 and not 103) return Results.BadRequest();
            var state = new FixtureAuthState(); state.SetUser(id); return Results.Ok(new { token = state.Token });
        });
        app.MapGet("/__test/logs", async (AppDbContext db) => await db.AppAttachmentAccessLogs.AsNoTracking().Select(x => new { x.AttachmentId, x.Action, x.UserId }).ToListAsync());
        app.MapRazorComponents<PreviewApp>().AddInteractiveServerRenderMode();
        app.Lifetime.ApplicationStopped.Register(() => { try { File.Delete(database); Directory.Delete(database + "-files", true); } catch { } });
        Console.WriteLine("PREVIEW_TEST_READY: real AttachmentBox + FilePreviewModal + attachments API + JWT + SQLite");
        await app.RunAsync();
    }

    private static string FindSource()
    {
        var specified = Environment.GetEnvironmentVariable("PREVIEW_SOURCE_ROOT");
        if (specified != null && Directory.Exists(Path.Combine(specified, "inventory/src"))) return specified;
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "inventory/src"))) return d.FullName;
        throw new DirectoryNotFoundException("Run from the source tree or set PREVIEW_SOURCE_ROOT.");
    }
    private static async Task Seed(IServiceProvider services, string fixtures)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        foreach (var id in new[] { 101, 102, 103, 900 }) db.Users.Add(new User { Id = id, Username = "preview-user-" + id, FirstName = "کاربر", LastName = "آزمایش", Role = "Operator", PasswordHash = AuthService.HashPassword("fixture-password") });
        db.Roles.Add(new Role { Id = 10, Name = "Preview reader", IsActive = true });
        db.Permissions.Add(new Permission { Id = 10, Module = "DocArchive", Action = "Read" });
        db.RolePermissions.Add(new RolePermission { RoleId = 10, PermissionId = 10 });
        foreach (var id in new[] { 101, 102, 103 }) db.UserRoles.Add(new UserRole { UserId = id, RoleId = 10 });
        db.DocFolders.Add(new DocFolder { Id = 10, Name = "Preview fixtures", CreatedByUserId = 900 });
        for (var i = 1; i <= 3; i++)
        {
            db.Documents.Add(new ArchiveDocument { Id = i, FolderId = 10, Title = "Fixture document " + i, Code = "PREVIEW-" + i, CreatedByUserId = 900, CreatedByName = "Fixture author", RequireDownloadConfirm = i == 2, WatermarkPreview = i >= 2 });
            db.DocumentVersions.Add(new DocumentVersion { Id = 100 + i, DocumentId = i, VersionNo = 1, CreatedByUserId = 900, Status = DocVersionStatus.Approved });
            db.DocumentPermissions.AddRange(new DocumentPermission { DocumentId = i, UserId = 101, Level = DocAccessLevel.Read, CanDownload = false },
                new DocumentPermission { DocumentId = i, UserId = 102, Level = DocAccessLevel.Read, CanDownload = true },
                new DocumentPermission { DocumentId = i, UserId = 103, Level = DocAccessLevel.View, CanDownload = false });
        }
        await db.SaveChangesAsync();
        var store = scope.ServiceProvider.GetRequiredService<FileStore>();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "manifest.json")));
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt32(); var file = item.GetProperty("file").GetString()!; var reference = item.GetProperty("version").GetInt32();
            var bytes = File.ReadAllBytes(Path.Combine(fixtures, file));
            string? path = null;
            if (item.GetProperty("storage").GetString() == "disk") { using var stream = new MemoryStream(bytes); path = await store.SaveAsync("DocVersion", reference, stream, file); }
            // Deliberately incorrect MIME exercises repaired suffix classification.
            db.AppAttachments.Add(new AppAttachment { Id = id, Module = "DocVersion", RefId = reference, FileName = file,
                ContentType = id == 1 || id == 8 || id == 11 ? "text/plain" : "application/octet-stream", Data = path == null ? bytes : Array.Empty<byte>(),
                FilePath = path, UploaderUserId = 900, UploaderName = "Fixture author", UploadedAt = new DateTime(2026, 1, 1).AddMinutes(id) });
        }
        await db.SaveChangesAsync();
    }
}

public sealed class FixtureAuthState : IAuthState
{
    internal const string Issuer = "attachment-preview-test-only";
    internal static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(32));
    public int UserId { get; private set; } = 101;
    public string? Token => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Issuer,
        new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Name, "preview-user-" + UserId), new Claim(ClaimTypes.Role, "Operator") },
        expires: DateTime.UtcNow.AddHours(2), signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)));
    public bool IsLoggedIn => true; public bool IsAdmin => false; public bool IsOperator => true; public bool IsReferrer => false;
    public bool CanOperate => true; public string DisplayName => "کاربر آزمایش"; public string Role => "Operator"; public int? ReferrerId => null;
    public bool Has(string permission) => permission == "DocArchive.Read"; public bool HasModule(string module) => module == "DocArchive"; public bool CanSee(string module) => HasModule(module);
    public event Action? Changed;
    public void SetUser(int id) { UserId = id; Changed?.Invoke(); }
    public Task InitializeAsync() => Task.CompletedTask; public Task SignInAsync(LoginResponse login) => Task.CompletedTask; public Task SignOutAsync() => Task.CompletedTask;
}
public sealed class NoNotifications : INotifyService
{
    public Task SendAsync(int id, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task SendManyAsync(IEnumerable<int> ids, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task SendToRoleAsync(string role, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task BroadcastChangedAsync(string scope) => Task.CompletedTask;
}
public sealed class NoBackgroundIndex : IDocIndexService
{
    public Task QueueAttachmentIndexingAsync(int id) => Task.CompletedTask;
    public Task<DocExtractionResult> IndexAttachmentAsync(int id, bool force = false) => throw new NotSupportedException();
    public Task<int> IndexVersionAsync(int id, bool force = false) => throw new NotSupportedException();
    public Task<int> IndexDocumentAsync(int id, bool force = false) => throw new NotSupportedException();
    public Task<DocReindexResultDto> ReindexAllAsync() => throw new NotSupportedException();
}

public sealed class FixtureFileEnvironment(string directory) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "PreviewTestFiles";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = directory;
    public string WebRootPath { get; set; } = Path.Combine(directory, "wwwroot");
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
