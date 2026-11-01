using System.Security.Claims;
using System.Text;
using Inventory.Api.Controllers.DocArchive;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using User = Inventory.Api.Data.User;

// تست واقعی روی کد تولیدی: FileStore، DocImportService (ورود انبوه) با SQLite و DocumentsController.Purge.
var failures = 0;
void Check(bool ok, string name)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (!ok) failures++;
}

var work = Path.Combine(Path.GetTempPath(), "docarchive-storage-" + Guid.NewGuid().ToString("N"));
var app = Path.Combine(work, "app");                 // پوشهٔ برنامه (مثل C:\inetpub\inventory)
var external = Path.Combine(work, "data", "DocArchive"); // پوشهٔ ثابت بیرون از برنامه
Directory.CreateDirectory(app);
IConfiguration Cfg(string? root) => new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { [FileStore.DocArchiveStorageRootKey] = root }).Build();
var env = new TestEnvironment(app);
var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 test");

// ---------- ۱) بدون تنظیم: رفتار قبلی دست‌نخورده ----------
{
    var store = new FileStore(env, Cfg(null));
    var rel = await store.SaveAsync("DocVersion", 5, new MemoryStream(bytes), "a.pdf");
    Check(rel.StartsWith("DocVersion/5/") && File.Exists(Path.Combine(app, "wwwroot", "uploads", "DocVersion", "5", rel.Split('/')[2])),
        "بدون StorageRoot فایل مثل قبل در wwwroot/uploads/DocVersion ذخیره می‌شود");
    Check(store.ReadBytes(rel)?.SequenceEqual(bytes) == true, "خواندن همان فایل");
    var legacy = new FileStore(env); // سازندهٔ قدیمی (تست‌ها و کد قبلی)
    Check(legacy.ReadBytes(rel)?.SequenceEqual(bytes) == true, "سازندهٔ تک‌پارامتری قدیمی همچنان کار می‌کند");
    var other = await store.SaveAsync("InnerLetters", 3, new MemoryStream(bytes), "n.pdf");
    Check(other.StartsWith("office/innerletter/3/"), "ماژول‌های دیگر دست‌نخورده‌اند");
}

// ---------- ۲) با StorageRoot بیرونی ----------
string legacyRel;
{
    // فایل قدیمی که قبل از تنظیم مسیر جدید در wwwroot ذخیره شده بود
    var dir = Path.Combine(app, "wwwroot", "uploads", "DocVersion", "7");
    Directory.CreateDirectory(dir);
    File.WriteAllBytes(Path.Combine(dir, "old_legacy.pdf"), bytes);
    legacyRel = "DocVersion/7/old_legacy.pdf";

    var store = new FileStore(env, Cfg(external));
    Check(store.HasExternalDocArchiveRoot && store.DocArchiveRootPath == Path.GetFullPath(external), "مسیر بیرونی شناخته شد");
    var rel = await store.SaveAsync("DocVersion", 9, new MemoryStream(bytes), "قرارداد.pdf");
    var name = rel.Split('/')[2];
    Check(rel.StartsWith("DocVersion/9/"), "مسیر نسبی ثبت‌شده در دیتابیس همان قالب قبلی است: " + rel);
    Check(File.Exists(Path.Combine(external, "9", name)), "فایل در پوشهٔ بیرونی نشست");
    Check(!Directory.Exists(Path.Combine(app, "wwwroot", "uploads", "DocVersion", "9")), "در wwwroot چیزی نوشته نشد");
    Check(!Directory.EnumerateFiles(external, "*.part", SearchOption.AllDirectories).Any(), "فایل موقت .part باقی نماند");

    Check(store.ReadBytes(legacyRel)?.SequenceEqual(bytes) == true, "فایل‌های قدیمی wwwroot/uploads/DocVersion همچنان پیدا می‌شوند");

    // شبیه‌سازی انتشار مجدد روی IIS: wwwroot پوشهٔ برنامه کامل پاک و دوباره ساخته می‌شود
    Directory.Delete(Path.Combine(app, "wwwroot"), true);
    var afterRedeploy = new FileStore(new TestEnvironment(app), Cfg(external));
    Check(afterRedeploy.ReadBytes(rel)?.SequenceEqual(bytes) == true, "بعد از پاک شدن wwwroot با انتشار مجدد، فایل از پوشهٔ بیرونی خوانده می‌شود");
    Check(store.Size(rel) == bytes.Length, "اندازهٔ فایل");
    store.Delete(rel);
    Check(!File.Exists(Path.Combine(external, "9", name)), "حذف فایل از پوشهٔ بیرونی");

    var raw = await store.SaveRawAsync("DocVersion", bytes, "png", 11);
    Check(raw.StartsWith("DocVersion/11/") && store.ReadBytes(raw) != null, "SaveRawAsync هم به پوشهٔ بیرونی می‌رود");

    var nonDoc = await store.SaveAsync("chat", 1, new MemoryStream(bytes), "x.txt");
    Check(File.Exists(Path.Combine(app, "wwwroot", "uploads", nonDoc)), "ماژول‌های غیر آرشیو همچنان در wwwroot/uploads");
}

// ---------- ۳) مسیر نسبی و متغیر محیطی ----------
{
    var rel = new FileStore(env, Cfg("App_Data/DocArchive"));
    Check(rel.DocArchiveRootPath == Path.GetFullPath(Path.Combine(app, "App_Data", "DocArchive")), "مسیر نسبی نسبت به پوشهٔ برنامه حل می‌شود");
    Environment.SetEnvironmentVariable("DOCSTORE_TEST", work);
    var ev = new FileStore(env, Cfg(OperatingSystem.IsWindows() ? "%DOCSTORE_TEST%\\ev" : "%DOCSTORE_TEST%/ev"));
    Check(ev.DocArchiveRootPath == Path.GetFullPath(Path.Combine(work, "ev")), "متغیر محیطی در مسیر پشتیبانی می‌شود");
}

// ---------- ۴) نام فایل‌ها و امنیت مسیر ----------
{
    var store = new FileStore(env, Cfg(external));
    var dots = await store.SaveAsync("DocVersion", 20, new MemoryStream(bytes), "نامه..نهایی.pdf");
    Check(store.ReadBytes(dots) != null, "نام دارای «..» ذخیره و دوباره خوانده می‌شود (قبلاً «فایل در دسترس نیست»)");
    Check(store.ReadBytes("DocVersion/20/x..y.pdf") == null && store.ToFull("DocVersion/20/x..y.pdf") != null, "«..» داخل نام فایل traversal حساب نمی‌شود");
    var longName = new string('ب', 120) + ".docx";
    var lr = await store.SaveAsync("DocVersion", 21, new MemoryStream(bytes), longName);
    Check(lr.EndsWith(".docx") && lr.Split('/')[2].Length <= 33 + 80, "نام بلند کوتاه شد ولی پسوند ماند: " + lr.Split('/')[2][^12..]);
    var bad = await store.SaveAsync("DocVersion", 22, new MemoryStream(bytes), "a:b?c*<d>|e\".pdf. ");
    Check(!bad.Split('/')[2].Any(c => ":?*<>|\"".Contains(c)) && bad.EndsWith(".pdf"), "کاراکترهای غیرمجاز ویندوز حذف شدند: " + bad.Split('/')[2][33..]);
    Check(FileStore.SanitizeName("C:\\fakepath\\scan.pdf") == "scan.pdf", "مسیر ویندوزی مرورگر از نام حذف می‌شود");
    Check(store.ToFull("../../etc/passwd") == null, "traversal با «..» رد می‌شود");
    Check(store.ToFull("DocVersion/../../../secret") == null, "traversal داخل DocVersion رد می‌شود");
    var abs = store.ToFull(OperatingSystem.IsWindows() ? "C:/Windows/win.ini" : "/etc/passwd");
    Check(abs == null || abs.StartsWith(Path.GetFullPath(Path.Combine(app, "wwwroot", "uploads"))), "مسیر مطلق به بیرون از ریشه نمی‌رود");
}

// ---------- ۵) ورود انبوه واقعی (DocImportService + SQLite) ----------
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
const int Manager = 1;
db.Users.Add(new User { Id = Manager, Username = "archive-manager", Role = "Operator" });
db.Roles.Add(new Role { Id = 40, Name = "archive-management" });
db.Permissions.Add(new Permission { Id = 2, Module = "DocArchive", Action = "Manage" });
db.RolePermissions.Add(new RolePermission { RoleId = 40, PermissionId = 2 });
db.UserRoles.Add(new UserRole { UserId = Manager, RoleId = 40 });
await db.SaveChangesAsync();

var importStore = new FileStore(env, Cfg(external));
var import = new DocImportService(db, Options.Create(new DocImportOptions()), importStore, NullLogger<DocImportService>.Instance);
var batch = await import.CreateBatchAsync(new DocImportBatchCreateDto(), Manager, "archive-manager");
var outcome = await import.ImportFileAsync(batch.Id, "پوشه الف/قرارداد نمونه+TST-001.pdf", new MemoryStream(bytes), bytes.Length, Manager, "archive-manager");
Check(outcome.Status == DocImportResultStatus.Imported, "ورود انبوه: وضعیت Imported (" + outcome.Status + " " + outcome.MessageFa + ")");
var att = await db.AppAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Module == "DocVersion");
Check(att?.FilePath?.StartsWith("DocVersion/") == true, "ورود انبوه: مسیر نسبی در دیتابیس " + att?.FilePath);
Check(att?.FilePath is { } fp && File.Exists(Path.Combine(external, fp["DocVersion/".Length..].Replace('/', Path.DirectorySeparatorChar))),
    "ورود انبوه: فایل در همان پوشهٔ بیرونی سرور نشست (نه new FileStore(env) جدا)");
Check(new FileStore(new TestEnvironment(app), Cfg(external)).ReadBytes(att?.FilePath)?.SequenceEqual(bytes) == true,
    "ورود انبوه: دانلود/پیش‌نمایش همان فایل را پیدا می‌کند");

// خطای نوشتن روی دیسک (مثل نبود دسترسی Modify هویت AppPool روی IIS) → سند نیمه‌کاره و فایل یتیم نمی‌ماند
if (!OperatingSystem.IsWindows() && Environment.UserName != "root")
{
    var locked = Path.Combine(work, "locked");
    Directory.CreateDirectory(locked);
    File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    var lockedStore = new FileStore(env, Cfg(locked));
    Check(!lockedStore.ProbeDocArchiveWrite().Ok, "تست نوشتن راه‌اندازی، پوشهٔ بدون دسترسی را تشخیص می‌دهد");
    var import2 = new DocImportService(db, Options.Create(new DocImportOptions()), lockedStore, NullLogger<DocImportService>.Instance);
    var docsBefore = await db.Documents.CountAsync();
    var failed = await import2.ImportFileAsync(batch.Id, "پوشه الف/نامه+TST-002.pdf", new MemoryStream(bytes), bytes.Length, Manager, "archive-manager");
    Check(failed.Status == DocImportResultStatus.Failed, "خطای دیسک: ردیف Failed شد");
    Check(failed.MessageFa.Contains("دیسک سرور"), "خطای دیسک: پیام روشن فارسی — " + failed.MessageFa);
    Check(await db.Documents.CountAsync() == docsBefore, "خطای دیسک: تراکنش برگشت خورد، سند بدون فایل ساخته نشد");
    db.ChangeTracker.Clear();
    Check((await db.DocImportBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id)).FailedCount == 1, "خطای دیسک: شمارش «ناموفق» نوبت در دیتابیس ثبت شد");
    File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    Check(!Directory.EnumerateFiles(locked, "*", SearchOption.AllDirectories).Any(), "خطای دیسک: فایل یتیم نماند");
}
Check(importStore.ProbeDocArchiveWrite().Ok, "تست نوشتن راه‌اندازی روی پوشهٔ سالم موفق است");

// ---------- ۶) حذف قطعی مدرک، فایل فیزیکی را هم پاک می‌کند ----------
{
    var doc = await db.Documents.FirstAsync();
    doc.IsDeleted = true;
    await db.SaveChangesAsync();
    var full = importStore.ToFull(att!.FilePath)!;
    var services = new ServiceCollection().AddSingleton(importStore).BuildServiceProvider();
    var controller = new DocumentsController(db, new DocAccessService(db),
        new DocumentService(db, new DocAccessService(db), new SilentNotify()), null!,
        new DocDownloadConfirmService(new MemoryCache(new MemoryCacheOptions())), new SilentNotify());
    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, Manager.ToString()), new Claim(ClaimTypes.Name, "archive-manager") }, "test"));
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal, RequestServices = services } };
    var result = await controller.Purge(doc.Id);
    Check(result is OkObjectResult, "حذف قطعی موفق");
    Check(!File.Exists(full), "حذف قطعی: فایل فیزیکی از دیسک سرور پاک شد");
}

// ---------- ۷) آدرس‌دهی کلاینت زیر مسیر پایهٔ IIS ----------
{
    var sub = new PathBaseHandler(new Uri("https://erp.example.ir/erp/"));
    Check(sub.Rewrite(new Uri("https://erp.example.ir/api/attachments/DocVersion/5"))!.ToString() == "https://erp.example.ir/erp/api/attachments/DocVersion/5",
        "«/api/…» زیر Application «erp» به «/erp/api/…» می‌رود");
    var already = new Uri("https://erp.example.ir/erp/api/x?a=%D8%A7");
    Check(ReferenceEquals(sub.Rewrite(already), already), "آدرس درست دوباره پیشوند نمی‌گیرد");
    Check(sub.Rewrite(new Uri("https://erp.example.ir/api/doc-archive/search?q=%D8%A7"))!.AbsoluteUri == "https://erp.example.ir/erp/api/doc-archive/search?q=%D8%A7",
        "کوئری فارسی (escape شده) هنگام افزودن مسیر پایه سالم می‌ماند");
    Check(sub.Rewrite(new Uri("https://other.host/api/x"))!.Host == "other.host" && sub.Rewrite(new Uri("https://other.host/api/x"))!.AbsolutePath == "/api/x", "میزبان دیگر دست نمی‌خورد");
    Check(sub.Rewrite(new Uri("https://erp.example.ir/erpx/api"))!.AbsolutePath == "/erp/erpx/api", "مسیر مشابه («/erpx») اشتباه گرفته نمی‌شود");
    var root = new PathBaseHandler(new Uri("http://192.168.1.10/"));
    var u = new Uri("http://192.168.1.10/api/attachments/1");
    Check(ReferenceEquals(root.Rewrite(u), u), "روی ریشهٔ سایت هیچ تغییری نمی‌دهد");
}

try { Directory.Delete(work, true); } catch { }
Console.WriteLine(failures == 0 ? "\nALL PASSED" : $"\n{failures} FAILED");
return failures == 0 ? 0 : 1;

public sealed class TestEnvironment : IWebHostEnvironment
{
    public TestEnvironment(string path) { ContentRootPath = path; WebRootPath = Path.Combine(path, "wwwroot"); }
    public string ApplicationName { get; set; } = "Inventory.Api";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Production";
    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
public sealed class SilentNotify : INotifyService
{
    public Task SendAsync(int id, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task SendManyAsync(IEnumerable<int> ids, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task SendToRoleAsync(string role, string title, string? body, string from, string form, string? link) => Task.CompletedTask;
    public Task BroadcastChangedAsync(string scope) => Task.CompletedTask;
}
