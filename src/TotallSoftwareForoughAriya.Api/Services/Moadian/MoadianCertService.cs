using System.Text.Encodings.Web;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using TaxCollectData.Library.Enums;

namespace Inventory.Api.Services.Moadian;

public interface IMoadianCertService
{
    // پروفایل مودی، گواهی امضا و تشخیص اتصال
    Task<MdnProfileDto> GetProfileAsync();
    Task<MdnProfileDto> SaveProfileAsync(MdnSaveProfileRequest req, string username);
    Task<MdnProfileDto> UploadCertificateAsync(MdnUploadCertificateRequest req, string username);
    Task<MdnProfileDto> ClearCertificateAsync(string username);
    Task<MdnPkcs11DiscoverResultDto> DiscoverPkcs11Async(MdnPkcs11DiscoverRequest req);
    Task<MdnWindowsStoreDiscoverResultDto> DiscoverWindowsStoreAsync();
    Task<MdnConnectionDiagnosticsDto> TestConnectionAsync(string username);
    Task<MdnTaxpayerLookupDto> LookupTaxpayerAsync(string economicCode);
    Task<MdnArticle6CheckResultDto> CheckArticle6StatusAsync(MdnArticle6CheckRequest req);

    // اطلاعات پایه و همگام‌سازی با ERP
    Task<List<MdnBuyerDto>> GetBuyersAsync(string? search = null);
    Task<MdnBuyerDto> SaveBuyerAsync(MdnSaveBuyerRequest req);
    Task DeleteBuyerAsync(int id);
    Task<MdnSyncResultDto> SyncBuyersFromPartiesAsync();

    Task<List<MdnCatalogItemDto>> GetCatalogItemsAsync(string? search = null);
    Task<MdnCatalogItemDto> SaveCatalogItemAsync(MdnSaveCatalogItemRequest req);
    Task DeleteCatalogItemAsync(int id);
    Task<MdnSyncResultDto> SyncCatalogFromProductsAsync();

    Task<List<MdnMeasureUnitDto>> GetMeasureUnitsAsync();
    Task<MdnMeasureUnitDto> SaveMeasureUnitAsync(MdnSaveMeasureUnitRequest req);

    // داشبورد و صورتحساب‌ها
    Task<MdnDashboardDto> GetDashboardAsync();
    Task<MdnInvoiceListResultDto> GetInvoicesAsync(MdnInvoiceFilterRequest filter);
    Task<MdnInvoiceDetailDto> GetInvoiceDetailAsync(int id);
    Task<MdnInvoiceDetailDto> SaveInvoiceAsync(MdnSaveInvoiceRequest req, int userId, string username);
    Task DeleteInvoiceAsync(int id);
    Task<MdnInvoiceJsonPreviewDto> PreviewInvoiceJsonAsync(int id);
    Task<MdnInvoiceDetailDto> CreateDerivativeInvoiceAsync(int sourceInvoiceId, MdnCreateDerivativeRequest req, int userId, string username);

    // اتصال به فاکتورهای فروش سیستم
    Task<List<MdnErpFacInvoicePickDto>> GetReceivableErpInvoicesAsync();
    Task<MdnInvoiceDetailDto> ImportFromErpInvoiceAsync(int facInvoiceId, int userId, string username);

    // ارسال، استعلام وضعیت پردازش، استعلام کارپوشه و ثبت پرداخت (SDK 2.0.29)
    Task<MdnBatchActionResultDto> SendInvoicesAsync(List<int> invoiceIds, string username);
    Task<MdnBatchActionResultDto> InquirySubmissionsAsync(List<int> invoiceIds, string username);
    Task<MdnBatchActionResultDto> InquiryFolderStatusesAsync(List<int> invoiceIds, string username);
    Task<MdnBatchActionResultDto> RefreshAllStatusesAsync(List<int> invoiceIds, string username);
    Task<List<MdnTimeRangeInquiryItemDto>> InquiryByTimeRangeAsync(MdnTimeRangeInquiryRequest req, string username);

    Task<MdnInvoicePaymentDto> RegisterPaymentAsync(int invoiceId, MdnRegisterPaymentRequest req, string username);
    Task<MdnInvoicePaymentDto> RetryPaymentSyncAsync(int invoiceId, int paymentId, string username);
    Task DeletePaymentAsync(int invoiceId, int paymentId);
}

public sealed class MoadianCertService : IMoadianCertService
{
    private static readonly JsonSerializerOptions LogJsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly AppDbContext _db;
    private readonly MoadianSecretVault _vault;
    private readonly IMoadianSdkGateway _gateway;

    public MoadianCertService(AppDbContext db, MoadianSecretVault vault, IMoadianSdkGateway gateway)
    {
        _db = db;
        _vault = vault;
        _gateway = gateway;
    }

    // ==================== تعیین شرکت فعال ====================

    private async Task<(int CompanyId, string CompanyName)> RequireActiveCompanyAsync()
    {
        var active = _db.ActiveCompanyId;
        if (active == 0)
            throw new InvalidOperationException("شما به هیچ شرکت فعالی دسترسی ندارید. ابتدا شرکت فعال را انتخاب کنید.");

        var companyId = active ?? _db.ResolveDefaultCompanyId() ?? 0;
        if (companyId <= 0)
            throw new InvalidOperationException("ابتدا حداقل یک شرکت فعال در بخش شرکت‌های سیستم تعریف کنید.");

        var name = await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync() ?? $"شرکت #{companyId}";

        return (companyId, name);
    }

    private async Task<(MdnTaxpayerProfile Profile, string CompanyName)> GetOrCreateProfileEntityAsync()
    {
        var (companyId, companyName) = await RequireActiveCompanyAsync();
        var profile = await _db.MdnTaxpayerProfiles.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (profile is not null)
            return (profile, companyName);

        profile = new MdnTaxpayerProfile
        {
            CompanyId = companyId,
            TradeName = companyName,
            TaxpayerType = MdnTaxpayerType.Legal,
            Environment = MdnEnvironment.Production,
            ProductionBaseUrl = "https://tp.tax.gov.ir/requestsmanager",
            SandboxBaseUrl = "https://rc.tax.gov.ir/requestsmanager",
            SignatureMode = MdnSignatureMode.Pkcs8Certificate,
            NextSerial = 1,
            DefaultVatRate = 10,
            IsActive = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.MdnTaxpayerProfiles.Add(profile);
        await _db.SaveChangesAsync();
        return (profile, companyName);
    }

    // ==================== پروفایل و گواهی امضا ====================

    public async Task<MdnProfileDto> GetProfileAsync()
    {
        var (profile, companyName) = await GetOrCreateProfileEntityAsync();
        return MapProfileDto(profile, companyName);
    }

    public async Task<MdnProfileDto> SaveProfileAsync(MdnSaveProfileRequest req, string username)
    {
        var (profile, companyName) = await GetOrCreateProfileEntityAsync();

        var memoryId = (req.MemoryId ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(memoryId) && memoryId.Length != 6)
            throw new InvalidOperationException("شناسه یکتای حافظه مالیاتی باید دقیقاً ۶ کاراکتر انگلیسی/عددی باشد (مثال: A11226).");

        var economicCode = (req.EconomicCode ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(economicCode) && (economicCode.Length < 10 || !economicCode.All(char.IsDigit)))
            throw new InvalidOperationException("شماره اقتصادی / کد ملی مودی باید فقط شامل ارقام (۱۰ تا ۱۴ رقم) باشد.");

        profile.TradeName = string.IsNullOrWhiteSpace(req.TradeName) ? companyName : req.TradeName.Trim();
        profile.TaxpayerType = req.TaxpayerType;
        profile.EconomicCode = economicCode;
        profile.NationalId = NullIfBlank(req.NationalId);
        profile.PostalCode = NullIfBlank(req.PostalCode);
        profile.BranchCode = NullIfBlank(req.BranchCode);
        profile.Address = NullIfBlank(req.Address);
        profile.MemoryId = memoryId;
        profile.Environment = req.Environment;
        profile.ProductionBaseUrl = NullIfBlank(req.ProductionBaseUrl) ?? "https://tp.tax.gov.ir/requestsmanager";
        profile.SandboxBaseUrl = NullIfBlank(req.SandboxBaseUrl) ?? "https://rc.tax.gov.ir/requestsmanager";
        profile.SignatureMode = req.SignatureMode;
        if (req.NextSerial > 0) profile.NextSerial = req.NextSerial;
        profile.DefaultVatRate = Math.Clamp(req.DefaultVatRate, 0, 100);
        profile.IsActive = req.IsActive;
        profile.UpdatedAt = DateTime.Now;

        AddLog(null, "PROFILE_UPDATE", true, "SAVED", null, null, null,
            $"به‌روزرسانی پرونده مالیاتی شرکت «{profile.TradeName}» (حافظه: {profile.MemoryId}).", null, username);

        await _db.SaveChangesAsync();
        return MapProfileDto(profile, companyName);
    }

    public async Task<MdnProfileDto> UploadCertificateAsync(MdnUploadCertificateRequest req, string username)
    {
        var (profile, companyName) = await GetOrCreateProfileEntityAsync();
        var companyId = profile.CompanyId ?? 0;

        profile.SignatureMode = req.SignatureMode;

        if (req.SignatureMode == MdnSignatureMode.Pkcs8Certificate)
        {
            var parsed = MoadianSecretVault.ParseAndValidatePkcs8Material(
                req.PrivateKeyPem,
                req.CertificateContent,
                req.PfxBase64,
                req.PfxPassword,
                req.PrivateKeyPassword);

            profile.EncryptedPrivateKeyPem = _vault.Encrypt(parsed.Pkcs8PrivateKeyPem, companyId, "pkcs8-key");
            profile.EncryptedCertificateBase64 = _vault.Encrypt(parsed.CertificateDerBase64, companyId, "x509-cert");
            profile.CertificateSubject = Truncate(parsed.Subject, 400);
            profile.CertificateIssuer = Truncate(parsed.Issuer, 400);
            profile.CertificateSerial = Truncate(parsed.SerialNumber, 120);
            profile.CertificateThumbprint = Truncate(parsed.Thumbprint, 120);
            profile.CertificateNotBefore = parsed.NotBeforeUtc;
            profile.CertificateNotAfter = parsed.NotAfterUtc;
            profile.UpdatedAt = DateTime.Now;

            AddLog(null, "CERTIFICATE_UPLOAD", true, "CERT_VERIFIED", null, null, null,
                $"گواهی امضای الکترونیکی X.509 با موفقیت تطبیق و رمزنگاری شد (اعتبار تا {parsed.NotAfterUtc.ToLocalTime():yyyy/MM/dd}).",
                null, username);
        }
        else if (req.SignatureMode == MdnSignatureMode.Pkcs11Token)
        {
            if (string.IsNullOrWhiteSpace(req.Pkcs11LibraryPath))
                throw new InvalidOperationException("مسیر فایل کتابخانه درایور PKCS#11 الزامی است (می‌توانید از دکمهٔ «جستجوی خودکار درایور و گواهی‌های توکن» استفاده کنید).");

            string effectivePin;
            if (!string.IsNullOrWhiteSpace(req.Pkcs11Pin))
            {
                effectivePin = req.Pkcs11Pin;
            }
            else if (!string.IsNullOrWhiteSpace(profile.EncryptedPkcs11Pin))
            {
                var pinBytes = _vault.DecryptToBytes(profile.EncryptedPkcs11Pin, companyId, "pkcs11-pin");
                try { effectivePin = System.Text.Encoding.UTF8.GetString(pinBytes); }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(pinBytes); }
            }
            else
            {
                throw new InvalidOperationException("رمز پین (PIN) توکن سخت‌افزاری باید وارد شود.");
            }

            // بررسی زندهٔ درایور، پین، اتصال توکن، تطبیق Label گواهی و تست امضای سخت‌افزاری
            var validated = MoadianPkcs11Inspector.ValidateAndInspectSelection(
                req.Pkcs11LibraryPath,
                req.Pkcs11CertificateAlias,
                effectivePin);

            profile.Pkcs11LibraryPath = validated.LibraryPath;
            profile.Pkcs11CertificateAlias = validated.ExactCertificateAlias;
            if (!string.IsNullOrWhiteSpace(req.Pkcs11Pin))
                profile.EncryptedPkcs11Pin = _vault.Encrypt(req.Pkcs11Pin, companyId, "pkcs11-pin");

            profile.CertificateSubject = Truncate(validated.Subject, 400);
            profile.CertificateIssuer = Truncate(validated.Issuer, 400);
            profile.CertificateSerial = Truncate(validated.SerialNumber, 120);
            profile.CertificateThumbprint = Truncate(validated.Thumbprint, 120);
            profile.CertificateNotBefore = validated.NotBeforeUtc;
            profile.CertificateNotAfter = validated.NotAfterUtc;
            profile.UpdatedAt = DateTime.Now;

            AddLog(null, "PKCS11_CONFIG", true, "TOKEN_VERIFIED", null, null, null,
                $"توکن سخت‌افزاری PKCS#11 با موفقیت بررسی، تست امضا و ثبت شد (Label: {profile.Pkcs11CertificateAlias}، اعتبار تا {validated.NotAfterUtc.ToLocalTime():yyyy/MM/dd}).",
                null, username);
        }
        else if (req.SignatureMode == MdnSignatureMode.WindowsStore)
        {
            var validated = MoadianWindowsStoreInspector.ValidateAndInspectSelection(
                !string.IsNullOrWhiteSpace(req.WindowsStoreThumbprint) ? req.WindowsStoreThumbprint : profile.CertificateThumbprint,
                req.WindowsStoreLocation);

            profile.CertificateSubject = Truncate(validated.Subject, 400);
            profile.CertificateIssuer = Truncate(validated.Issuer, 400);
            profile.CertificateSerial = Truncate(validated.SerialNumber, 120);
            profile.CertificateThumbprint = Truncate(validated.Thumbprint, 120);
            profile.CertificateNotBefore = validated.NotBeforeUtc;
            profile.CertificateNotAfter = validated.NotAfterUtc;
            profile.UpdatedAt = DateTime.Now;

            AddLog(null, "WINDOWS_STORE_CONFIG", true, "STORE_CERT_VERIFIED", null, null, null,
                $"گواهی امضای الکترونیکی از مخزن ویندوز ({validated.StoreLocation}) با موفقیت بررسی، تست امضا و ثبت شد (Thumbprint: {validated.Thumbprint}، اعتبار تا {validated.NotAfterUtc.ToLocalTime():yyyy/MM/dd}).",
                null, username);
        }

        await _db.SaveChangesAsync();
        return MapProfileDto(profile, companyName);
    }

    public Task<MdnPkcs11DiscoverResultDto> DiscoverPkcs11Async(MdnPkcs11DiscoverRequest req)
        => Task.FromResult(MoadianPkcs11Inspector.Discover(req.LibraryPath, req.Pin));

    public Task<MdnWindowsStoreDiscoverResultDto> DiscoverWindowsStoreAsync()
        => Task.FromResult(MoadianWindowsStoreInspector.Discover());

    public async Task<MdnProfileDto> ClearCertificateAsync(string username)
    {
        var (profile, companyName) = await GetOrCreateProfileEntityAsync();
        profile.EncryptedPrivateKeyPem = null;
        profile.EncryptedCertificateBase64 = null;
        profile.CertificateSubject = null;
        profile.CertificateIssuer = null;
        profile.CertificateSerial = null;
        profile.CertificateThumbprint = null;
        profile.CertificateNotBefore = null;
        profile.CertificateNotAfter = null;
        profile.EncryptedPkcs11Pin = null;
        profile.UpdatedAt = DateTime.Now;

        AddLog(null, "CERTIFICATE_CLEAR", true, "CLEARED", null, null, null,
            "کلید خصوصی و گواهی امضای الکترونیکی از گاوصندوق این شرکت پاک شد.", null, username);

        await _db.SaveChangesAsync();
        return MapProfileDto(profile, companyName);
    }

    public async Task<MdnConnectionDiagnosticsDto> TestConnectionAsync(string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var diag = await _gateway.TestConnectionAsync(profile);

        profile.LastConnectionTestAt = DateTime.Now;
        profile.LastConnectionStatus = Truncate(diag.Summary, 300);
        if (!string.IsNullOrWhiteSpace(diag.FiscalStatus))
            profile.LastFiscalStatus = Truncate(diag.FiscalStatus, 120);
        profile.UpdatedAt = DateTime.Now;

        AddLog(null, "CONNECTION_TEST", diag.Success, diag.FiscalStatus ?? (diag.Success ? "OK" : "ERROR"),
            null, null, null, diag.Summary,
            JsonSerializer.Serialize(diag.Steps, LogJsonOpts), username);

        await _db.SaveChangesAsync();
        return diag;
    }

    public async Task<MdnTaxpayerLookupDto> LookupTaxpayerAsync(string economicCode)
    {
        if (string.IsNullOrWhiteSpace(economicCode))
            throw new InvalidOperationException("کد اقتصادی جهت استعلام وارد نشده است.");
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        return await _gateway.LookupTaxpayerAsync(profile, economicCode);
    }

    public async Task<MdnArticle6CheckResultDto> CheckArticle6StatusAsync(MdnArticle6CheckRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EconomicCode))
            throw new InvalidOperationException("کد اقتصادی جهت بررسی وضعیت ماده ۶ الزامی است.");
        if (req.VatValue <= 0)
            throw new InvalidOperationException("مبلغ مالیات بر ارزش افزوده باید بزرگ‌تر از صفر باشد.");
        if (req.Period < 14001)
            throw new InvalidOperationException("دوره مالیاتی را به صورت ۵ رقمی (سال شمسی + فصل، مثل 14053) وارد کنید.");

        var (profile, _) = await GetOrCreateProfileEntityAsync();
        return await _gateway.CheckArticle6StatusAsync(profile, req);
    }

    // ==================== خریداران، کالاها/خدمات و واحدهای سنجش ====================

    public async Task<List<MdnBuyerDto>> GetBuyersAsync(string? search = null)
    {
        await RequireActiveCompanyAsync();
        var q = _db.MdnBuyers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(b => b.Name.Contains(s)
                          || (b.EconomicCode != null && b.EconomicCode.Contains(s))
                          || (b.NationalId != null && b.NationalId.Contains(s)));
        }

        return await q.OrderByDescending(b => b.IsActive).ThenBy(b => b.Name)
            .Select(b => new MdnBuyerDto
            {
                Id = b.Id,
                PartyId = b.PartyId,
                Name = b.Name,
                BuyerType = b.BuyerType,
                EconomicCode = b.EconomicCode,
                NationalId = b.NationalId,
                PostalCode = b.PostalCode,
                BranchCode = b.BranchCode,
                PassportNumber = b.PassportNumber,
                Phone = b.Phone,
                Address = b.Address,
                IsActive = b.IsActive
            }).ToListAsync();
    }

    public async Task<MdnBuyerDto> SaveBuyerAsync(MdnSaveBuyerRequest req)
    {
        var (companyId, _) = await RequireActiveCompanyAsync();
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("نام خریدار الزامی است.");

        MdnBuyer entity;
        if (req.Id > 0)
        {
            entity = await _db.MdnBuyers.FirstOrDefaultAsync(b => b.Id == req.Id)
                ?? throw new InvalidOperationException("خریدار مورد نظر یافت نشد.");
        }
        else
        {
            entity = new MdnBuyer { CompanyId = companyId, CreatedAt = DateTime.Now };
            _db.MdnBuyers.Add(entity);
        }

        entity.PartyId = req.PartyId;
        entity.Name = req.Name.Trim();
        entity.BuyerType = req.BuyerType;
        entity.EconomicCode = NullIfBlank(req.EconomicCode);
        entity.NationalId = NullIfBlank(req.NationalId);
        entity.PostalCode = NullIfBlank(req.PostalCode);
        entity.BranchCode = NullIfBlank(req.BranchCode);
        entity.PassportNumber = NullIfBlank(req.PassportNumber);
        entity.Phone = NullIfBlank(req.Phone);
        entity.Address = NullIfBlank(req.Address);
        entity.IsActive = req.IsActive;

        await _db.SaveChangesAsync();
        return new MdnBuyerDto
        {
            Id = entity.Id,
            PartyId = entity.PartyId,
            Name = entity.Name,
            BuyerType = entity.BuyerType,
            EconomicCode = entity.EconomicCode,
            NationalId = entity.NationalId,
            PostalCode = entity.PostalCode,
            BranchCode = entity.BranchCode,
            PassportNumber = entity.PassportNumber,
            Phone = entity.Phone,
            Address = entity.Address,
            IsActive = entity.IsActive
        };
    }

    public async Task DeleteBuyerAsync(int id)
    {
        await RequireActiveCompanyAsync();
        var entity = await _db.MdnBuyers.FirstOrDefaultAsync(b => b.Id == id)
            ?? throw new InvalidOperationException("خریدار یافت نشد.");
        var used = await _db.MdnInvoices.AnyAsync(i => i.BuyerId == id);
        if (used)
            throw new InvalidOperationException("این خریدار در صورتحساب‌ها استفاده شده است؛ به جای حذف، آن را غیرفعال کنید.");
        _db.MdnBuyers.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<MdnSyncResultDto> SyncBuyersFromPartiesAsync()
    {
        var (companyId, _) = await RequireActiveCompanyAsync();
        var parties = await _db.Parties.AsNoTracking()
            .Where(p => p.IsActive && p.Type == Inventory.Shared.PartyType.Customer)
            .ToListAsync();

        var existingByParty = await _db.MdnBuyers
            .Where(b => b.PartyId != null)
            .ToDictionaryAsync(b => b.PartyId!.Value, b => b);

        int added = 0, updated = 0;
        foreach (var p in parties)
        {
            var taxId = NullIfBlank(p.TaxId);
            var isNatural = taxId is { Length: 10 };
            if (existingByParty.TryGetValue(p.Id, out var buyer))
            {
                buyer.Name = p.Name;
                buyer.PostalCode ??= NullIfBlank(p.PostalCode);
                buyer.Phone ??= NullIfBlank(p.Mobile) ?? NullIfBlank(p.Phone);
                buyer.Address ??= NullIfBlank(p.Address);
                if (!string.IsNullOrWhiteSpace(taxId))
                {
                    if (isNatural) buyer.NationalId ??= taxId;
                    else buyer.EconomicCode ??= taxId;
                }
                updated++;
            }
            else
            {
                _db.MdnBuyers.Add(new MdnBuyer
                {
                    CompanyId = companyId,
                    PartyId = p.Id,
                    Name = p.Name,
                    BuyerType = isNatural ? MdnTaxpayerType.Natural : MdnTaxpayerType.Legal,
                    EconomicCode = !isNatural ? taxId : null,
                    NationalId = isNatural ? taxId : null,
                    PostalCode = NullIfBlank(p.PostalCode),
                    Phone = NullIfBlank(p.Mobile) ?? NullIfBlank(p.Phone),
                    Address = NullIfBlank(p.Address),
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
                added++;
            }
        }

        await _db.SaveChangesAsync();
        return new MdnSyncResultDto
        {
            Added = added,
            Updated = updated,
            Message = $"همگام‌سازی طرف‌حساب‌ها انجام شد: {added} خریدار جدید افزوده و {updated} مورد به‌روز شد."
        };
    }

    public async Task<List<MdnCatalogItemDto>> GetCatalogItemsAsync(string? search = null)
    {
        await RequireActiveCompanyAsync();
        var units = await _db.MdnMeasureUnits.AsNoTracking()
            .ToDictionaryAsync(u => u.Code, u => u.Name, StringComparer.OrdinalIgnoreCase);

        var q = _db.MdnCatalogItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Name.Contains(s) || c.Sstid.Contains(s) || (c.InternalCode != null && c.InternalCode.Contains(s)));
        }

        var items = await q.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ToListAsync();
        return items.Select(c => new MdnCatalogItemDto
        {
            Id = c.Id,
            ProductId = c.ProductId,
            InternalCode = c.InternalCode,
            Name = c.Name,
            Sstid = c.Sstid,
            UnitCode = c.UnitCode,
            UnitName = units.TryGetValue(c.UnitCode, out var un) ? un : c.UnitCode,
            VatRate = c.VatRate,
            DefaultUnitPrice = c.DefaultUnitPrice,
            HsCode = c.HsCode,
            IsService = c.IsService,
            IsActive = c.IsActive
        }).ToList();
    }

    public async Task<MdnCatalogItemDto> SaveCatalogItemAsync(MdnSaveCatalogItemRequest req)
    {
        var (companyId, _) = await RequireActiveCompanyAsync();
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("نام کالا یا خدمت الزامی است.");
        var sstid = (req.Sstid ?? string.Empty).Trim();
        if (sstid.Length != 13 || !sstid.All(char.IsDigit))
            throw new InvalidOperationException("شناسه کالا/خدمت مالیاتی (SSTID) باید دقیقاً ۱۳ رقم عددی باشد.");

        MdnCatalogItem entity;
        if (req.Id > 0)
        {
            entity = await _db.MdnCatalogItems.FirstOrDefaultAsync(c => c.Id == req.Id)
                ?? throw new InvalidOperationException("کالا/خدمت مورد نظر یافت نشد.");
        }
        else
        {
            entity = new MdnCatalogItem { CompanyId = companyId, CreatedAt = DateTime.Now };
            _db.MdnCatalogItems.Add(entity);
        }

        entity.ProductId = req.ProductId;
        entity.InternalCode = NullIfBlank(req.InternalCode);
        entity.Name = req.Name.Trim();
        entity.Sstid = sstid;
        entity.UnitCode = NullIfBlank(req.UnitCode) ?? "164";
        entity.VatRate = Math.Clamp(req.VatRate, 0, 100);
        entity.DefaultUnitPrice = Math.Max(0, req.DefaultUnitPrice);
        entity.HsCode = NullIfBlank(req.HsCode);
        entity.IsService = req.IsService;
        entity.IsActive = req.IsActive;

        await _db.SaveChangesAsync();
        var unitName = await _db.MdnMeasureUnits.AsNoTracking()
            .Where(u => u.Code == entity.UnitCode)
            .Select(u => u.Name)
            .FirstOrDefaultAsync() ?? entity.UnitCode;

        return new MdnCatalogItemDto
        {
            Id = entity.Id,
            ProductId = entity.ProductId,
            InternalCode = entity.InternalCode,
            Name = entity.Name,
            Sstid = entity.Sstid,
            UnitCode = entity.UnitCode,
            UnitName = unitName,
            VatRate = entity.VatRate,
            DefaultUnitPrice = entity.DefaultUnitPrice,
            HsCode = entity.HsCode,
            IsService = entity.IsService,
            IsActive = entity.IsActive
        };
    }

    public async Task DeleteCatalogItemAsync(int id)
    {
        await RequireActiveCompanyAsync();
        var entity = await _db.MdnCatalogItems.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException("کالا/خدمت یافت نشد.");
        var used = await _db.MdnInvoiceLines.AnyAsync(l => l.CatalogItemId == id);
        if (used)
            throw new InvalidOperationException("این قلم در صورتحساب‌ها استفاده شده است؛ به جای حذف، آن را غیرفعال کنید.");
        _db.MdnCatalogItems.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<MdnSyncResultDto> SyncCatalogFromProductsAsync()
    {
        var (companyId, _) = await RequireActiveCompanyAsync();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.TaxCode != null && p.TaxCode != "")
            .ToListAsync();

        var existingByProduct = await _db.MdnCatalogItems
            .Where(c => c.ProductId != null)
            .ToDictionaryAsync(c => c.ProductId!.Value, c => c);

        int added = 0, updated = 0;
        foreach (var p in products)
        {
            var sstid = p.TaxCode!.Trim();
            if (sstid.Length != 13 || !sstid.All(char.IsDigit)) continue;

            var unitCode = NullIfBlank(p.TaxUnitCode) ?? "164";
            var priceRial = ToWholeRial(p.SalePrice);
            var vat = p.IsVatIncluded ? p.VatRate : 0m;

            if (existingByProduct.TryGetValue(p.Id, out var cat))
            {
                cat.Name = p.Name;
                cat.Sstid = sstid;
                cat.InternalCode = p.Code;
                cat.DefaultUnitPrice = priceRial;
                cat.VatRate = vat;
                cat.HsCode = NullIfBlank(p.CustomsCode);
                cat.IsService = p.IsService;
                updated++;
            }
            else
            {
                _db.MdnCatalogItems.Add(new MdnCatalogItem
                {
                    CompanyId = companyId,
                    ProductId = p.Id,
                    InternalCode = p.Code,
                    Name = p.Name,
                    Sstid = sstid,
                    UnitCode = unitCode,
                    VatRate = vat,
                    DefaultUnitPrice = priceRial,
                    HsCode = NullIfBlank(p.CustomsCode),
                    IsService = p.IsService,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
                added++;
            }
        }

        await _db.SaveChangesAsync();
        return new MdnSyncResultDto
        {
            Added = added,
            Updated = updated,
            Message = products.Count == 0
                ? "هیچ کالایی با «شناسه مالیاتی ۱۳ رقمی» در کاتالوگ سیستم یافت نشد."
                : $"همگام‌سازی کاتالوگ انجام شد: {added} قلم جدید افزوده و {updated} قلم به‌روز شد."
        };
    }

    public async Task<List<MdnMeasureUnitDto>> GetMeasureUnitsAsync()
    {
        return await _db.MdnMeasureUnits.AsNoTracking()
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.Code)
            .Select(u => new MdnMeasureUnitDto
            {
                Id = u.Id,
                Code = u.Code,
                Name = u.Name,
                IsActive = u.IsActive
            }).ToListAsync();
    }

    public async Task<MdnMeasureUnitDto> SaveMeasureUnitAsync(MdnSaveMeasureUnitRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("کد و عنوان واحد سنجش الزامی است.");

        var code = req.Code.Trim();
        MdnMeasureUnit entity;
        if (req.Id > 0)
        {
            entity = await _db.MdnMeasureUnits.FirstOrDefaultAsync(u => u.Id == req.Id)
                ?? throw new InvalidOperationException("واحد سنجش یافت نشد.");
        }
        else
        {
            if (await _db.MdnMeasureUnits.AnyAsync(u => u.Code == code))
                throw new InvalidOperationException($"واحد سنجش با کد «{code}» قبلاً ثبت شده است.");
            entity = new MdnMeasureUnit();
            _db.MdnMeasureUnits.Add(entity);
        }

        entity.Code = code;
        entity.Name = req.Name.Trim();
        entity.IsActive = req.IsActive;
        await _db.SaveChangesAsync();

        return new MdnMeasureUnitDto { Id = entity.Id, Code = entity.Code, Name = entity.Name, IsActive = entity.IsActive };
    }

    // ==================== داشبورد و فهرست صورتحساب‌ها ====================

    public async Task<MdnDashboardDto> GetDashboardAsync()
    {
        var (profile, companyName) = await GetOrCreateProfileEntityAsync();
        var q = _db.MdnInvoices.AsNoTracking();

        var totalInvoices = await q.CountAsync();
        var draftCount = await q.CountAsync(i => i.SubmissionStatus == MdnSubmissionStatus.Draft);
        var inProgressCount = await q.CountAsync(i => i.SubmissionStatus == MdnSubmissionStatus.Sent || i.SubmissionStatus == MdnSubmissionStatus.InProgress);
        var submissionSuccessCount = await q.CountAsync(i => i.SubmissionStatus == MdnSubmissionStatus.Success);
        var submissionFailedCount = await q.CountAsync(i => i.SubmissionStatus == MdnSubmissionStatus.Failed || i.SubmissionStatus == MdnSubmissionStatus.Timeout);
        var folderApprovedCount = await q.CountAsync(i => i.FolderStatus == MdnFolderStatus.Approved || i.FolderStatus == MdnFolderStatus.SystemApproved);
        var folderPendingCount = await q.CountAsync(i => i.FolderStatus == MdnFolderStatus.Pending);
        var folderRejectedCount = await q.CountAsync(i => i.FolderStatus == MdnFolderStatus.Rejected);
        var cancelledCount = await q.CountAsync(i => i.Subject == MdnInvoiceSubject.Cancellation || i.FolderStatus == MdnFolderStatus.Cancelled);

        var approvedQuery = q.Where(i =>
            i.Subject != MdnInvoiceSubject.Cancellation &&
            i.FolderStatus != MdnFolderStatus.Cancelled &&
            (i.SubmissionStatus == MdnSubmissionStatus.Success ||
             i.FolderStatus == MdnFolderStatus.Approved ||
             i.FolderStatus == MdnFolderStatus.SystemApproved));

        var approvedTotalAmount = await approvedQuery.SumAsync(i => (long?)i.TotalAmount) ?? 0L;
        var approvedTotalVat = await approvedQuery.SumAsync(i => (long?)i.TotalVat) ?? 0L;
        var approvedPaid = await approvedQuery.SumAsync(i => (long?)i.PaidAmount) ?? 0L;
        var totalRegisteredPayments = await q.SumAsync(i => (long?)i.PaidAmount) ?? 0L;

        var recentEntities = await q
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.Id)
            .Take(8)
            .ToListAsync();

        var recentLogs = await _db.MdnSubmissionLogs.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)
            .Take(10)
            .Select(l => new MdnSubmissionLogDto
            {
                Id = l.Id,
                ActionType = l.ActionType,
                CreatedAt = l.CreatedAt,
                IsSuccess = l.IsSuccess,
                StatusCode = l.StatusCode,
                Uid = l.Uid,
                ReferenceNumber = l.ReferenceNumber,
                TaxId = l.TaxId,
                Summary = l.Summary,
                ErrorDetailsJson = l.ErrorDetailsJson,
                CreatedByUsername = l.CreatedByUsername
            })
            .ToListAsync();

        return new MdnDashboardDto
        {
            Profile = MapProfileDto(profile, companyName),
            TotalInvoices = totalInvoices,
            DraftCount = draftCount,
            InProgressCount = inProgressCount,
            SubmissionSuccessCount = submissionSuccessCount,
            SubmissionFailedCount = submissionFailedCount,
            FolderApprovedCount = folderApprovedCount,
            FolderPendingCount = folderPendingCount,
            FolderRejectedCount = folderRejectedCount,
            CancelledCount = cancelledCount,
            ApprovedTotalAmount = approvedTotalAmount,
            ApprovedTotalVat = approvedTotalVat,
            TotalRegisteredPayments = totalRegisteredPayments,
            TotalUnpaidCreditAmount = Math.Max(0L, approvedTotalAmount - approvedPaid),
            RecentInvoices = recentEntities.Select(MapInvoiceSummaryDto).ToList(),
            RecentLogs = recentLogs
        };
    }

    public async Task<MdnInvoiceListResultDto> GetInvoicesAsync(MdnInvoiceFilterRequest filter)
    {
        await RequireActiveCompanyAsync();
        var q = _db.MdnInvoices.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(i => i.InternalNumber.Contains(s)
                          || (i.TaxId != null && i.TaxId.Contains(s))
                          || (i.ReferenceNumber != null && i.ReferenceNumber.Contains(s))
                          || (i.BuyerName != null && i.BuyerName.Contains(s))
                          || (i.BuyerEconomicCode != null && i.BuyerEconomicCode.Contains(s)));
        }

        if (filter.SubmissionStatus.HasValue)
            q = q.Where(i => i.SubmissionStatus == filter.SubmissionStatus.Value);
        if (filter.FolderStatus.HasValue)
            q = q.Where(i => i.FolderStatus == filter.FolderStatus.Value);
        if (filter.PaymentStatus.HasValue)
            q = q.Where(i => i.PaymentStatus == filter.PaymentStatus.Value);
        if (filter.Subject.HasValue)
            q = q.Where(i => i.Subject == filter.Subject.Value);
        if (filter.FromDate.HasValue)
            q = q.Where(i => i.IssueDate >= filter.FromDate.Value.Date);
        if (filter.ToDate.HasValue)
            q = q.Where(i => i.IssueDate < filter.ToDate.Value.Date.AddDays(1));

        var total = await q.CountAsync();
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 5, 100);

        var items = await q
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new MdnInvoiceListResultDto
        {
            Items = items.Select(MapInvoiceSummaryDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<MdnInvoiceDetailDto> GetInvoiceDetailAsync(int id)
    {
        await RequireActiveCompanyAsync();
        var inv = await _db.MdnInvoices
            .AsNoTracking()
            .Include(i => i.Lines.OrderBy(l => l.RowNumber))
            .Include(i => i.Payments.OrderByDescending(p => p.PaymentDate))
            .Include(i => i.Logs.OrderByDescending(l => l.CreatedAt))
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("صورتحساب مورد نظر یافت نشد.");

        var related = await _db.MdnInvoices.AsNoTracking()
            .Where(r => r.Id != inv.Id &&
                       (r.ParentInvoiceId == inv.Id ||
                        (inv.ParentInvoiceId != null && r.Id == inv.ParentInvoiceId) ||
                        (!string.IsNullOrEmpty(inv.TaxId) && r.ReferenceTaxId == inv.TaxId)))
            .OrderByDescending(r => r.IssueDate)
            .ToListAsync();

        return MapInvoiceDetailDto(inv, related);
    }

    // ==================== ثبت، ویرایش، محاسبه ریالی و مشتق‌سازی صورتحساب ====================

    public async Task<MdnInvoiceDetailDto> SaveInvoiceAsync(MdnSaveInvoiceRequest req, int userId, string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var companyId = profile.CompanyId ?? 0;

        if (req.Lines is null || req.Lines.Count == 0)
            throw new InvalidOperationException("صورتحساب باید حداقل یک قلم کالا یا خدمت داشته باشد.");

        if (req.Subject != MdnInvoiceSubject.Original && string.IsNullOrWhiteSpace(req.ReferenceTaxId))
            throw new InvalidOperationException("برای صورتحساب اصلاحی، ابطالی یا برگشت از فروش، «شماره مالیاتی مرجع ۲۲ کاراکتری» الزامی است.");

        MdnInvoice invoice;
        if (req.Id > 0)
        {
            invoice = await _db.MdnInvoices
                .Include(i => i.Lines)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == req.Id)
                ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

            if (invoice.SubmissionStatus is MdnSubmissionStatus.Sent or MdnSubmissionStatus.InProgress or MdnSubmissionStatus.Success)
                throw new InvalidOperationException("این صورتحساب ارسال شده است و قابل ویرایش مستقیم نیست؛ از گزینهٔ «صدور صورتحساب اصلاحی» استفاده کنید.");
        }
        else
        {
            var nextSeq = await _db.MdnInvoices.CountAsync() + 1;
            invoice = new MdnInvoice
            {
                CompanyId = companyId,
                InternalNumber = $"MDN-{DateTime.Now:yyyyMM}-{nextSeq:D4}",
                CreateDate = DateTime.Now,
                CreatedByUserId = userId,
                SubmissionStatus = MdnSubmissionStatus.Draft,
                FolderStatus = MdnFolderStatus.Unknown
            };
            _db.MdnInvoices.Add(invoice);
        }

        // پر کردن اطلاعات خریدار در صورت انتخاب از لیست
        if (req.BuyerId is > 0)
        {
            var buyer = await _db.MdnBuyers.AsNoTracking().FirstOrDefaultAsync(b => b.Id == req.BuyerId.Value);
            if (buyer is not null)
            {
                req.BuyerName = string.IsNullOrWhiteSpace(req.BuyerName) ? buyer.Name : req.BuyerName;
                req.BuyerType = buyer.BuyerType;
                req.BuyerEconomicCode ??= buyer.EconomicCode;
                req.BuyerNationalId ??= buyer.NationalId;
                req.BuyerPostalCode ??= buyer.PostalCode;
                req.BuyerBranchCode ??= buyer.BranchCode;
                req.BuyerPassportNumber ??= buyer.PassportNumber;
            }
        }

        if (req.InvoiceType == MdnInvoiceType.Type1 &&
            string.IsNullOrWhiteSpace(req.BuyerEconomicCode) &&
            string.IsNullOrWhiteSpace(req.BuyerNationalId))
        {
            throw new InvalidOperationException("در صورتحساب نوع اول، وارد کردن کد اقتصادی یا شناسه/کد ملی خریدار الزامی است (برای مصرف‌کننده نهایی از نوع دوم استفاده کنید).");
        }

        invoice.IssueDate = req.IssueDate;
        invoice.InvoiceType = req.InvoiceType;
        invoice.Pattern = req.Pattern;
        invoice.Subject = req.Subject;
        invoice.SettlementMethod = req.SettlementMethod;
        invoice.ParentInvoiceId = req.ParentInvoiceId;
        invoice.ReferenceTaxId = req.Subject == MdnInvoiceSubject.Original ? null : NullIfBlank(req.ReferenceTaxId)?.ToUpperInvariant();

        invoice.SellerEconomicCode = NullIfBlank(profile.EconomicCode);
        invoice.SellerTradeName = NullIfBlank(profile.TradeName);
        invoice.SellerPostalCode = NullIfBlank(profile.PostalCode);
        invoice.SellerBranchCode = NullIfBlank(profile.BranchCode);

        invoice.BuyerId = req.InvoiceType == MdnInvoiceType.Type1 ? req.BuyerId : null;
        invoice.BuyerName = req.InvoiceType == MdnInvoiceType.Type1 ? NullIfBlank(req.BuyerName) : "مصرف‌کننده نهایی";
        invoice.BuyerType = req.BuyerType;
        invoice.BuyerEconomicCode = req.InvoiceType == MdnInvoiceType.Type1 ? NullIfBlank(req.BuyerEconomicCode) : null;
        invoice.BuyerNationalId = req.InvoiceType == MdnInvoiceType.Type1 ? NullIfBlank(req.BuyerNationalId) : null;
        invoice.BuyerPostalCode = req.InvoiceType == MdnInvoiceType.Type1 ? NullIfBlank(req.BuyerPostalCode) : null;
        invoice.BuyerBranchCode = req.InvoiceType == MdnInvoiceType.Type1 ? NullIfBlank(req.BuyerBranchCode) : null;
        invoice.BuyerPassportNumber = req.InvoiceType == MdnInvoiceType.Type1 ? NullIfBlank(req.BuyerPassportNumber) : null;

        invoice.ContractRegistrationNumber = NullIfBlank(req.ContractRegistrationNumber);
        invoice.BillId = NullIfBlank(req.BillId);
        invoice.CustomsLicenseNumber = NullIfBlank(req.CustomsLicenseNumber);
        invoice.CustomsCode = NullIfBlank(req.CustomsCode);
        invoice.CustomsDeclarationNumber = NullIfBlank(req.CustomsDeclarationNumber);
        invoice.CustomsDeclarationDate = req.CustomsDeclarationDate;
        invoice.Tax17 = req.Tax17;
        invoice.Description = NullIfBlank(req.Description);
        invoice.UpdatedAt = DateTime.Now;

        // بازسازی اقلام با محاسبهٔ دقیق ریالی
        if (invoice.Id > 0 && invoice.Lines.Count > 0)
            _db.MdnInvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines.Clear();

        long tprdis = 0, tdis = 0, tadis = 0, tvam = 0, todam = 0;
        var rowNum = 0;

        foreach (var l in req.Lines)
        {
            rowNum++;
            var sstid = (l.Sstid ?? string.Empty).Trim();
            if (sstid.Length != 13 || !sstid.All(char.IsDigit))
                throw new InvalidOperationException($"ردیف {rowNum}: شناسه کالا/خدمت (SSTID) باید ۱۳ رقم عددی باشد.");
            if (string.IsNullOrWhiteSpace(l.Description))
                throw new InvalidOperationException($"ردیف {rowNum}: شرح کالا/خدمت الزامی است.");
            if (l.Quantity <= 0)
                throw new InvalidOperationException($"ردیف {rowNum}: مقدار/تعداد باید بزرگ‌تر از صفر باشد.");
            if (l.UnitPrice < 0)
                throw new InvalidOperationException($"ردیف {rowNum}: مبلغ واحد نمی‌تواند منفی باشد.");

            var prdis = ToWholeRial(checked(l.Quantity * l.UnitPrice));
            var dis = Math.Max(0L, l.DiscountAmount);
            if (dis > prdis)
                throw new InvalidOperationException($"ردیف {rowNum}: مبلغ تخفیف نمی‌تواند از مبلغ قبل از تخفیف بیشتر باشد.");

            var adis = checked(prdis - dis);

            // در الگوی ۳ (طلا و جواهر) پایه محاسبه مالیات بر ارزش افزوده اجرت + سود + حق‌العمل است؛ در سایر الگوها مبلغ پس از تخفیف
            var vatBase = invoice.Pattern == MdnInvoicePattern.GoldJewelry
                ? checked((l.ConstructionFee ?? 0L) + (l.SellerProfit ?? 0L) + (l.BrokerFee ?? 0L))
                : adis;

            var vam = ToWholeRial(vatBase * l.VatRate / 100m);
            var odam = l.OtherTaxRate is > 0 ? ToWholeRial(adis * l.OtherTaxRate.Value / 100m) : Math.Max(0L, l.OtherTaxAmount);
            var olam = l.OtherLegalFeeRate is > 0 ? ToWholeRial(adis * l.OtherLegalFeeRate.Value / 100m) : Math.Max(0L, l.OtherLegalFeeAmount);
            var goldExtras = invoice.Pattern == MdnInvoicePattern.GoldJewelry
                ? checked((l.ConstructionFee ?? 0L) + (l.SellerProfit ?? 0L) + (l.BrokerFee ?? 0L))
                : 0L;
            var tsstam = checked(adis + goldExtras + vam + odam + olam);

            invoice.Lines.Add(new MdnInvoiceLine
            {
                CompanyId = companyId,
                RowNumber = rowNum,
                CatalogItemId = l.CatalogItemId,
                Sstid = sstid,
                Description = l.Description.Trim(),
                UnitCode = NullIfBlank(l.UnitCode) ?? "164",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                PriceBeforeDiscount = prdis,
                DiscountAmount = dis,
                PriceAfterDiscount = adis,
                VatRate = l.VatRate,
                VatAmount = vam,
                OtherTaxSubject = NullIfBlank(l.OtherTaxSubject),
                OtherTaxRate = l.OtherTaxRate,
                OtherTaxAmount = odam,
                OtherLegalFeeSubject = NullIfBlank(l.OtherLegalFeeSubject),
                OtherLegalFeeRate = l.OtherLegalFeeRate,
                OtherLegalFeeAmount = olam,
                ConstructionFee = l.ConstructionFee,
                SellerProfit = l.SellerProfit,
                BrokerFee = l.BrokerFee,
                CurrencyCode = NullIfBlank(l.CurrencyCode),
                ExchangeRate = l.ExchangeRate,
                NetWeight = l.NetWeight,
                HsCode = NullIfBlank(l.HsCode),
                ContractRegistrationNumber = NullIfBlank(l.ContractRegistrationNumber),
                TotalLineAmount = tsstam
            });

            tprdis = checked(tprdis + prdis);
            tdis = checked(tdis + dis);
            tadis = checked(tadis + adis);
            tvam = checked(tvam + vam);
            todam = checked(todam + odam + olam);
        }

        var tbill = checked(tadis + tvam + todam + (invoice.Pattern == MdnInvoicePattern.GoldJewelry
            ? invoice.Lines.Sum(x => (x.ConstructionFee ?? 0L) + (x.SellerProfit ?? 0L) + (x.BrokerFee ?? 0L))
            : 0L));

        invoice.TotalBeforeDiscount = tprdis;
        invoice.TotalDiscount = tdis;
        invoice.TotalAfterDiscount = tadis;
        invoice.TotalVat = tvam;
        invoice.TotalOtherTaxes = todam;
        invoice.TotalAmount = tbill;

        // قواعد رسمی مبلغ نقدی (cap) و نسیه (insp): در حالت ترکیبی (setm=3)، جمع cap + insp باید برابر tadis باشد
        switch (invoice.SettlementMethod)
        {
            case MdnSettlementMethod.Cash:
                invoice.CashAmount = tadis;
                invoice.CreditAmount = 0;
                break;
            case MdnSettlementMethod.Credit:
                invoice.CashAmount = 0;
                invoice.CreditAmount = tadis;
                break;
            case MdnSettlementMethod.CashAndCredit:
                var cashPart = Math.Clamp(req.CashAmount ?? 0L, 0L, tadis);
                invoice.CashAmount = cashPart;
                invoice.CreditAmount = tadis - cashPart;
                break;
        }

        // پرداخت‌های اولیه هم‌زمان با صدور (IncludedInInvoice)
        var initialPayments = invoice.Payments.Where(p => p.SyncState == MdnPaymentSyncState.IncludedInInvoice).ToList();
        if (initialPayments.Count > 0)
            _db.MdnInvoicePayments.RemoveRange(initialPayments);
        foreach (var old in initialPayments)
            invoice.Payments.Remove(old);

        if (req.InitialPayments is { Count: > 0 })
        {
            foreach (var p in req.InitialPayments.Where(x => x.PaidAmount > 0))
            {
                invoice.Payments.Add(new MdnInvoicePayment
                {
                    CompanyId = companyId,
                    PaymentMethod = p.PaymentMethod,
                    PaidAmount = p.PaidAmount,
                    PaymentDate = p.PaymentDate,
                    SwitchNumber = NullIfBlank(p.SwitchNumber),
                    AcceptorNumber = NullIfBlank(p.AcceptorNumber),
                    TerminalNumber = NullIfBlank(p.TerminalNumber),
                    ReferenceNumber = NullIfBlank(p.ReferenceNumber),
                    MaskedCardNumber = NullIfBlank(p.MaskedCardNumber),
                    PayerNationalId = NullIfBlank(p.PayerNationalId),
                    Note = NullIfBlank(p.Note),
                    SyncState = MdnPaymentSyncState.IncludedInInvoice,
                    CreatedAt = DateTime.Now
                });
            }
        }

        // اگر صورتحساب نقدی است و ردیف پرداخت جدا ثبت نشده، پس از تأیید تسویه‌شده تلقی شود
        RecalculatePaymentSummary(invoice);

        // اگر در حالت Failed ویرایش شده، به Draft برمی‌گردد تا شماره مالیاتی تازه در ارسال بعدی تولید شود
        if (invoice.SubmissionStatus == MdnSubmissionStatus.Failed)
        {
            invoice.SubmissionStatus = MdnSubmissionStatus.Draft;
            invoice.TaxId = null;
            invoice.SerialHex = null;
            invoice.SerialDecimal = 0;
            invoice.Uid = null;
            invoice.ReferenceNumber = null;
            invoice.LastErrorCode = null;
            invoice.LastErrorMessage = null;
        }

        await _db.SaveChangesAsync();
        return await GetInvoiceDetailAsync(invoice.Id);
    }

    public async Task DeleteInvoiceAsync(int id)
    {
        await RequireActiveCompanyAsync();
        var inv = await _db.MdnInvoices.FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

        if (inv.SubmissionStatus is MdnSubmissionStatus.Sent or MdnSubmissionStatus.InProgress or MdnSubmissionStatus.Success)
            throw new InvalidOperationException("صورتحساب ارسال‌شده قابل حذف فیزیکی نیست؛ برای بی‌اثر کردن آن «صورتحساب ابطالی» صادر کنید.");

        _db.MdnInvoices.Remove(inv);
        await _db.SaveChangesAsync();
    }

    public async Task<MdnInvoiceJsonPreviewDto> PreviewInvoiceJsonAsync(int id)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var inv = await _db.MdnInvoices.AsNoTracking()
            .Include(i => i.Lines.OrderBy(l => l.RowNumber))
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

        var warnings = new List<string>();
        var mem = string.IsNullOrWhiteSpace(profile.MemoryId) || profile.MemoryId.Trim().Length != 6
            ? "A00000"
            : profile.MemoryId.Trim().ToUpperInvariant();

        if (mem == "A00000")
            warnings.Add("شناسه یکتای ۶ کاراکتری حافظه مالیاتی هنوز در تنظیمات ثبت نشده است (مقدار نمونه A00000 نمایش داده شد).");
        if (string.IsNullOrWhiteSpace(profile.EconomicCode))
            warnings.Add("کد اقتصادی فروشنده در پروفایل مودیان ثبت نشده است.");
        if (!MapProfileDto(profile, "").IsReadyForSigning)
            warnings.Add("گواهی امضای الکترونیکی معتبر هنوز بارگذاری نشده است.");

        var serial = inv.SerialDecimal > 0 ? inv.SerialDecimal : Math.Max(1L, profile.NextSerial);
        var taxId = !string.IsNullOrWhiteSpace(inv.TaxId)
            ? inv.TaxId!
            : _gateway.GenerateTaxId(mem, serial, inv.IssueDate);
        var serialHex = !string.IsNullOrWhiteSpace(inv.SerialHex)
            ? inv.SerialHex!
            : _gateway.FormatSerialHex(serial);

        var dto = _gateway.BuildSdkInvoiceDto(profile, inv, taxId, serialHex);
        var json = _gateway.SerializeInvoiceJson(dto);

        return new MdnInvoiceJsonPreviewDto
        {
            InvoiceId = inv.Id,
            PotentialTaxId = taxId,
            PotentialSerialHex = serialHex,
            JsonPayload = json,
            ValidationWarnings = warnings
        };
    }

    public async Task<MdnInvoiceDetailDto> CreateDerivativeInvoiceAsync(
        int sourceInvoiceId,
        MdnCreateDerivativeRequest req,
        int userId,
        string username)
    {
        if (req.Subject == MdnInvoiceSubject.Original)
            throw new InvalidOperationException("موضوع صورتحساب مشتق باید اصلاحی، ابطالی یا برگشت از فروش باشد.");

        var source = await _db.MdnInvoices.AsNoTracking()
            .Include(i => i.Lines.OrderBy(l => l.RowNumber))
            .FirstOrDefaultAsync(i => i.Id == sourceInvoiceId)
            ?? throw new InvalidOperationException("صورتحساب مرجع یافت نشد.");

        if (string.IsNullOrWhiteSpace(source.TaxId))
            throw new InvalidOperationException("صورتحساب مرجع هنوز شماره مالیاتی ۲۲ کاراکتری (TaxId) ندارد.");

        var writeReq = new MdnSaveInvoiceRequest
        {
            Id = 0,
            IssueDate = req.IssueDate,
            InvoiceType = source.InvoiceType,
            Pattern = source.Pattern,
            Subject = req.Subject,
            SettlementMethod = source.SettlementMethod,
            ParentInvoiceId = source.Id,
            ReferenceTaxId = source.TaxId,
            BuyerId = source.BuyerId,
            BuyerName = source.BuyerName,
            BuyerType = source.BuyerType,
            BuyerEconomicCode = source.BuyerEconomicCode,
            BuyerNationalId = source.BuyerNationalId,
            BuyerPostalCode = source.BuyerPostalCode,
            BuyerBranchCode = source.BuyerBranchCode,
            BuyerPassportNumber = source.BuyerPassportNumber,
            ContractRegistrationNumber = source.ContractRegistrationNumber,
            BillId = source.BillId,
            CustomsLicenseNumber = source.CustomsLicenseNumber,
            CustomsCode = source.CustomsCode,
            CustomsDeclarationNumber = source.CustomsDeclarationNumber,
            CustomsDeclarationDate = source.CustomsDeclarationDate,
            Tax17 = source.Tax17,
            CashAmount = source.CashAmount,
            CreditAmount = source.CreditAmount,
            Description = NullIfBlank(req.Description) ?? $"{MdnUiLabels.SubjectTitle(req.Subject)} بابت صورتحساب {source.InternalNumber} ({source.TaxId})",
            Lines = source.Lines.Select(l => new MdnInvoiceLineDto
            {
                RowNumber = l.RowNumber,
                CatalogItemId = l.CatalogItemId,
                Sstid = l.Sstid,
                Description = l.Description,
                UnitCode = l.UnitCode,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountAmount = l.DiscountAmount,
                VatRate = l.VatRate,
                OtherTaxSubject = l.OtherTaxSubject,
                OtherTaxRate = l.OtherTaxRate,
                OtherTaxAmount = l.OtherTaxAmount,
                OtherLegalFeeSubject = l.OtherLegalFeeSubject,
                OtherLegalFeeRate = l.OtherLegalFeeRate,
                OtherLegalFeeAmount = l.OtherLegalFeeAmount,
                ConstructionFee = l.ConstructionFee,
                SellerProfit = l.SellerProfit,
                BrokerFee = l.BrokerFee,
                CurrencyCode = l.CurrencyCode,
                ExchangeRate = l.ExchangeRate,
                NetWeight = l.NetWeight,
                HsCode = l.HsCode,
                ContractRegistrationNumber = l.ContractRegistrationNumber
            }).ToList()
        };

        var created = await SaveInvoiceAsync(writeReq, userId, username);
        AddLog(created.Id, "CREATE_DERIVATIVE", true, req.Subject.ToString().ToUpperInvariant(),
            null, null, source.TaxId,
            $"ایجاد پیش‌نویس «{MdnUiLabels.SubjectTitle(req.Subject)}» پیرو صورتحساب مرجع {source.TaxId}.",
            null, username);
        await _db.SaveChangesAsync();
        return await GetInvoiceDetailAsync(created.Id);
    }

    // ==================== تبدیل فاکتور فروش سیستم (FacInvoices) به صورتحساب مودیان ====================

    public async Task<List<MdnErpFacInvoicePickDto>> GetReceivableErpInvoicesAsync()
    {
        await RequireActiveCompanyAsync();
        var importedIds = (await _db.MdnInvoices.AsNoTracking()
            .Where(i => i.SourceFacInvoiceId != null)
            .Select(i => i.SourceFacInvoiceId!.Value)
            .ToListAsync()).ToHashSet();

        var list = await _db.FacInvoices.AsNoTracking()
            .Include(f => f.Party)
            .Include(f => f.Lines)
            .Where(f => f.Kind == Inventory.Shared.InvoiceKind.Sale && f.Status != Inventory.Shared.InvoiceStatus.Cancelled)
            .OrderByDescending(f => f.Date)
            .ThenByDescending(f => f.Id)
            .Take(50)
            .ToListAsync();

        return list.Select(f => new MdnErpFacInvoicePickDto
        {
            Id = f.Id,
            Number = f.Number,
            Date = f.Date,
            PartyName = f.Party?.Name ?? "بدون طرف حساب",
            PartyTaxId = f.Party?.TaxId,
            TotalNet = f.TotalNet,
            TotalVat = f.TotalVat,
            LineCount = f.Lines.Count,
            AlreadyImported = importedIds.Contains(f.Id)
        }).ToList();
    }

    public async Task<MdnInvoiceDetailDto> ImportFromErpInvoiceAsync(int facInvoiceId, int userId, string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var fac = await _db.FacInvoices.AsNoTracking()
            .Include(f => f.Party)
            .Include(f => f.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(f => f.Id == facInvoiceId)
            ?? throw new InvalidOperationException("فاکتور فروش مورد نظر در سیستم یافت نشد.");

        if (fac.Lines.Count == 0)
            throw new InvalidOperationException("فاکتور فروش انتخاب‌شده فاقد اقلام کالا است.");

        var hasBuyerTaxId = !string.IsNullOrWhiteSpace(fac.Party?.TaxId);
        var isNatural = fac.Party?.TaxId?.Trim().Length == 10;

        var lines = new List<MdnInvoiceLineDto>();
        var idx = 0;
        foreach (var fl in fac.Lines)
        {
            idx++;
            var sstid = NullIfBlank(fl.TaxCode) ?? NullIfBlank(fl.Product?.TaxCode) ?? "0000000000000";
            if (sstid.Length != 13 || !sstid.All(char.IsDigit))
                throw new InvalidOperationException($"ردیف {idx} («{fl.Product?.Name}») شناسه مالیاتی ۱۳ رقمی معتبر ندارد. ابتدا شناسه کالا را در کاتالوگ تکمیل کنید.");

            lines.Add(new MdnInvoiceLineDto
            {
                RowNumber = idx,
                Sstid = sstid,
                Description = fl.Product?.Name ?? $"ردیف {idx}",
                UnitCode = NullIfBlank(fl.Product?.TaxUnitCode) ?? "164",
                Quantity = fl.Quantity,
                UnitPrice = fl.UnitPrice,
                DiscountAmount = ToWholeRial(fl.Discount),
                VatRate = fl.VatRate > 0 ? fl.VatRate : profile.DefaultVatRate
            });
        }

        var settlement = fac.Settlement == Inventory.Shared.SettlementType.Cash
            ? MdnSettlementMethod.Cash
            : MdnSettlementMethod.Credit;

        var saveReq = new MdnSaveInvoiceRequest
        {
            Id = 0,
            IssueDate = fac.Date,
            InvoiceType = hasBuyerTaxId ? MdnInvoiceType.Type1 : MdnInvoiceType.Type2,
            Pattern = MdnInvoicePattern.Sale,
            Subject = MdnInvoiceSubject.Original,
            SettlementMethod = settlement,
            BuyerName = fac.Party?.Name,
            BuyerType = isNatural ? MdnTaxpayerType.Natural : MdnTaxpayerType.Legal,
            BuyerEconomicCode = !isNatural ? NullIfBlank(fac.Party?.TaxId) : null,
            BuyerNationalId = isNatural ? NullIfBlank(fac.Party?.TaxId) : null,
            BuyerPostalCode = NullIfBlank(fac.Party?.PostalCode),
            Description = $"تبدیل از فاکتور فروش شماره {fac.Number}",
            Lines = lines
        };

        var detail = await SaveInvoiceAsync(saveReq, userId, username);
        var createdEntity = await _db.MdnInvoices.FirstAsync(i => i.Id == detail.Id);
        createdEntity.SourceFacInvoiceId = fac.Id;
        createdEntity.SourceFacInvoiceNumber = fac.Number;
        await _db.SaveChangesAsync();

        return await GetInvoiceDetailAsync(detail.Id);
    }

    // ==================== ارسال، استعلام وضعیت، استعلام کارپوشه و ثبت پرداخت (SDK 2.0.29) ====================

    public async Task<MdnBatchActionResultDto> SendInvoicesAsync(List<int> invoiceIds, string username)
    {
        var (profile, companyName) = await GetOrCreateProfileEntityAsync();
        if (!MapProfileDto(profile, companyName).IsReadyForSigning)
            throw new InvalidOperationException("پروفایل مودیان یا گواهی امضای الکترونیکی کامل نیست. ابتدا از بخش «پرونده و گواهی امضا» تنظیمات را تکمیل کنید.");

        var invoices = await _db.MdnInvoices
            .Include(i => i.Lines.OrderBy(l => l.RowNumber))
            .Include(i => i.Payments)
            .Where(i => invoiceIds.Contains(i.Id))
            .OrderBy(i => i.Id)
            .ToListAsync();

        var batch = new MdnBatchActionResultDto { Total = invoices.Count };

        foreach (var inv in invoices)
        {
            if (inv.SubmissionStatus is MdnSubmissionStatus.Sent or MdnSubmissionStatus.InProgress or MdnSubmissionStatus.Success)
            {
                batch.Failed++;
                batch.Items.Add(new MdnActionItemOutcomeDto
                {
                    InvoiceId = inv.Id,
                    InternalNumber = inv.InternalNumber,
                    TaxId = inv.TaxId,
                    Success = false,
                    Status = inv.SubmissionStatus.ToString(),
                    Message = "این صورتحساب قبلاً به درگاه ارسال شده است."
                });
                continue;
            }

            try
            {
                if (inv.SerialDecimal <= 0 || string.IsNullOrWhiteSpace(inv.TaxId))
                {
                    var serial = Math.Max(1L, profile.NextSerial);
                    profile.NextSerial = serial + 1;
                    inv.SerialDecimal = serial;
                    inv.SerialHex = _gateway.FormatSerialHex(serial);
                    inv.MemoryId = profile.MemoryId.Trim().ToUpperInvariant();
                    inv.TaxId = _gateway.GenerateTaxId(inv.MemoryId, serial, inv.IssueDate);
                    inv.SellerEconomicCode = profile.EconomicCode.Trim();
                    inv.SellerTradeName = profile.TradeName.Trim();
                    inv.SellerPostalCode = profile.PostalCode;
                    inv.SellerBranchCode = profile.BranchCode;
                }

                var dto = _gateway.BuildSdkInvoiceDto(profile, inv, inv.TaxId!, inv.SerialHex!);
                var (uid, refNum, returnedTaxId) = await _gateway.SendInvoiceAsync(profile, dto);

                inv.Uid = uid;
                inv.ReferenceNumber = refNum;
                if (!string.IsNullOrWhiteSpace(returnedTaxId)) inv.TaxId = returnedTaxId;
                inv.SubmissionStatus = MdnSubmissionStatus.InProgress;
                inv.SentAt = DateTime.Now;
                inv.LastErrorCode = null;
                inv.LastErrorMessage = null;
                inv.UpdatedAt = DateTime.Now;

                AddLog(inv.Id, "INVOICE_SEND", true, "IN_PROGRESS", uid, refNum, inv.TaxId,
                    $"صورتحساب با گواهی امضا به درگاه مودیان ارسال شد (کد رهگیری: {refNum}). حداقل ۱۰ ثانیه بعد استعلام بگیرید.",
                    null, username);

                await _db.SaveChangesAsync();

                batch.Succeeded++;
                batch.Items.Add(new MdnActionItemOutcomeDto
                {
                    InvoiceId = inv.Id,
                    InternalNumber = inv.InternalNumber,
                    TaxId = inv.TaxId,
                    Success = true,
                    Status = "IN_PROGRESS",
                    Message = $"ارسال موفق — شماره پیگیری: {refNum}"
                });
            }
            catch (Exception ex)
            {
                inv.LastErrorCode = "SEND_ERROR";
                inv.LastErrorMessage = Truncate(ex.Message, 2000);
                inv.UpdatedAt = DateTime.Now;

                AddLog(inv.Id, "INVOICE_SEND", false, "ERROR", inv.Uid, inv.ReferenceNumber, inv.TaxId,
                    $"خطا در ارسال صورتحساب: {ex.Message}", null, username);

                await _db.SaveChangesAsync();

                batch.Failed++;
                batch.Items.Add(new MdnActionItemOutcomeDto
                {
                    InvoiceId = inv.Id,
                    InternalNumber = inv.InternalNumber,
                    TaxId = inv.TaxId,
                    Success = false,
                    Status = "ERROR",
                    Message = ex.Message
                });
            }
        }

        batch.Message = $"ارسال به پایان رسید: {batch.Succeeded} موفق، {batch.Failed} ناموفق.";
        return batch;
    }

    public async Task<MdnBatchActionResultDto> InquirySubmissionsAsync(List<int> invoiceIds, string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var q = _db.MdnInvoices.Include(i => i.Payments).Where(i => i.ReferenceNumber != null && i.ReferenceNumber != "");
        if (invoiceIds.Count > 0)
            q = q.Where(i => invoiceIds.Contains(i.Id));
        else
            q = q.Where(i => i.SubmissionStatus == MdnSubmissionStatus.Sent || i.SubmissionStatus == MdnSubmissionStatus.InProgress);

        var invoices = await q.ToListAsync();
        var batch = new MdnBatchActionResultDto { Total = invoices.Count };
        if (invoices.Count == 0)
        {
            batch.Message = "هیچ صورتحسابی با شماره پیگیری برای استعلام وضعیت ارسال یافت نشد.";
            return batch;
        }

        var refs = invoices.Select(i => i.ReferenceNumber!).Distinct().ToList();
        var sdkResults = await _gateway.InquiryByReferenceNumbersAsync(profile, refs);
        var byRef = sdkResults
            .Where(r => !string.IsNullOrWhiteSpace(r.ReferenceNumber))
            .GroupBy(r => r.ReferenceNumber)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var inv in invoices)
        {
            inv.LastInquiryAt = DateTime.Now;
            inv.UpdatedAt = DateTime.Now;

            if (!byRef.TryGetValue(inv.ReferenceNumber!, out var res))
            {
                batch.Failed++;
                batch.Items.Add(new MdnActionItemOutcomeDto
                {
                    InvoiceId = inv.Id,
                    InternalNumber = inv.InternalNumber,
                    TaxId = inv.TaxId,
                    Success = false,
                    Status = "NO_RESPONSE",
                    Message = "پاسخی برای این شماره پیگیری از درگاه دریافت نشد."
                });
                continue;
            }

            var rawStatus = (res.Status ?? string.Empty).Trim().ToUpperInvariant();
            inv.SubmissionStatus = rawStatus switch
            {
                "SUCCESS" => MdnSubmissionStatus.Success,
                "FAILED" => MdnSubmissionStatus.Failed,
                "IN_PROGRESS" or "PENDING" => MdnSubmissionStatus.InProgress,
                "TIMEOUT" => MdnSubmissionStatus.Timeout,
                "NOT_FOUND" => MdnSubmissionStatus.NotFound,
                _ => inv.SubmissionStatus
            };

            var errors = res.Data?.Error?.Select(e => $"[{e.Code}] {e.Message}").ToList() ?? new List<string>();
            var warnings = res.Data?.Warning?.Select(w => $"[{w.Code}] {w.Message}").ToList() ?? new List<string>();

            if (inv.SubmissionStatus == MdnSubmissionStatus.Success)
            {
                inv.LastErrorCode = null;
                inv.LastErrorMessage = warnings.Count > 0 ? "هشدارها: " + string.Join(" | ", warnings) : null;
                if (inv.FolderStatus == MdnFolderStatus.Unknown)
                    inv.FolderStatus = inv.Subject == MdnInvoiceSubject.Cancellation ? MdnFolderStatus.Cancelled : MdnFolderStatus.Pending;

                // اگر صورتحساب مرجع توسط این ابطالی باطل شد، وضعیت صورتحساب مرجع هم Cancelled شود
                if (inv.Subject == MdnInvoiceSubject.Cancellation && inv.ParentInvoiceId is > 0)
                {
                    var parent = await _db.MdnInvoices.FirstOrDefaultAsync(p => p.Id == inv.ParentInvoiceId.Value);
                    if (parent is not null)
                    {
                        parent.FolderStatus = MdnFolderStatus.Cancelled;
                        parent.PaymentStatus = MdnPaymentStatus.NotApplicable;
                    }
                }
            }
            else if (errors.Count > 0)
            {
                inv.LastErrorCode = res.Data?.Error?.FirstOrDefault()?.Code;
                inv.LastErrorMessage = Truncate(string.Join(" | ", errors), 2000);
            }

            RecalculatePaymentSummary(inv);

            var detailJson = (errors.Count > 0 || warnings.Count > 0)
                ? JsonSerializer.Serialize(new { errors, warnings }, LogJsonOpts)
                : null;

            var msg = inv.SubmissionStatus == MdnSubmissionStatus.Success
                ? "صورتحساب با موفقیت در درگاه مودیان ثبت و تایید اولیه شد."
                : errors.Count > 0
                    ? string.Join(" | ", errors)
                    : $"وضعیت درگاه: {rawStatus}";

            AddLog(inv.Id, "SUBMISSION_INQUIRY", inv.SubmissionStatus == MdnSubmissionStatus.Success,
                rawStatus, inv.Uid, inv.ReferenceNumber, inv.TaxId, Truncate(msg, 600) ?? rawStatus, detailJson, username);

            if (inv.SubmissionStatus == MdnSubmissionStatus.Success) batch.Succeeded++;
            else batch.Failed++;

            batch.Items.Add(new MdnActionItemOutcomeDto
            {
                InvoiceId = inv.Id,
                InternalNumber = inv.InternalNumber,
                TaxId = inv.TaxId,
                Success = inv.SubmissionStatus == MdnSubmissionStatus.Success,
                Status = rawStatus,
                Message = msg
            });
        }

        await _db.SaveChangesAsync();
        batch.Message = $"استعلام وضعیت ارسال ({invoices.Count} مورد) انجام شد.";
        return batch;
    }

    public async Task<MdnBatchActionResultDto> InquiryFolderStatusesAsync(List<int> invoiceIds, string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var q = _db.MdnInvoices.Include(i => i.Payments).Where(i => i.TaxId != null && i.TaxId != "");
        if (invoiceIds.Count > 0)
            q = q.Where(i => invoiceIds.Contains(i.Id));
        else
            q = q.Where(i => i.SubmissionStatus == MdnSubmissionStatus.Success &&
                             i.FolderStatus != MdnFolderStatus.Cancelled);

        var invoices = await q.ToListAsync();
        var batch = new MdnBatchActionResultDto { Total = invoices.Count };
        if (invoices.Count == 0)
        {
            batch.Message = "هیچ صورتحساب دارای شماره مالیاتی (TaxId) جهت استعلام کارپوشه یافت نشد.";
            return batch;
        }

        var taxIds = invoices.Select(i => i.TaxId!).Distinct().ToList();
        var sdkStatuses = await _gateway.InquiryFolderStatusesAsync(profile, taxIds);
        var byTaxId = sdkStatuses
            .Where(s => !string.IsNullOrWhiteSpace(s.TaxId))
            .GroupBy(s => s.TaxId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var inv in invoices)
        {
            inv.LastFolderInquiryAt = DateTime.Now;
            inv.UpdatedAt = DateTime.Now;

            if (!byTaxId.TryGetValue(inv.TaxId!, out var st))
            {
                batch.Failed++;
                batch.Items.Add(new MdnActionItemOutcomeDto
                {
                    InvoiceId = inv.Id,
                    InternalNumber = inv.InternalNumber,
                    TaxId = inv.TaxId,
                    Success = false,
                    Status = "NO_RESPONSE",
                    Message = "وضعیت کارپوشه برای این شماره مالیاتی بازنگشت."
                });
                continue;
            }

            var rawFolder = (st.InvoiceStatus ?? string.Empty).Trim().ToUpperInvariant();
            inv.FolderStatusRaw = Truncate(rawFolder, 60);
            inv.Article6Status = Truncate(st.Article6Status, 60);
            inv.FolderStatus = MapFolderStatus(rawFolder);

            if (!string.IsNullOrWhiteSpace(st.Error))
                inv.LastErrorMessage = Truncate($"خطای کارپوشه: {st.Error}", 2000);

            RecalculatePaymentSummary(inv);

            var summary = $"وضعیت کارپوشه: {MdnUiLabels.FolderTitle(inv.FolderStatus)} ({rawFolder})" +
                          (!string.IsNullOrWhiteSpace(st.Article6Status) ? $" | ماده ۶: {st.Article6Status}" : "");

            AddLog(inv.Id, "FOLDER_STATUS_INQUIRY", string.IsNullOrWhiteSpace(st.Error),
                string.IsNullOrWhiteSpace(rawFolder) ? "UNKNOWN" : rawFolder,
                inv.Uid, inv.ReferenceNumber, inv.TaxId,
                Truncate(summary, 600) ?? summary,
                !string.IsNullOrWhiteSpace(st.Error) ? st.Error : null,
                username);

            batch.Succeeded++;
            batch.Items.Add(new MdnActionItemOutcomeDto
            {
                InvoiceId = inv.Id,
                InternalNumber = inv.InternalNumber,
                TaxId = inv.TaxId,
                Success = true,
                Status = rawFolder,
                Message = summary
            });
        }

        await _db.SaveChangesAsync();
        batch.Message = $"استعلام وضعیت به‌روز کارپوشه برای {batch.Succeeded} صورتحساب انجام شد.";
        return batch;
    }

    public async Task<MdnBatchActionResultDto> RefreshAllStatusesAsync(List<int> invoiceIds, string username)
    {
        var subBatch = await InquirySubmissionsAsync(invoiceIds, username);
        var folderBatch = await InquiryFolderStatusesAsync(invoiceIds, username);

        return new MdnBatchActionResultDto
        {
            Total = subBatch.Total + folderBatch.Total,
            Succeeded = subBatch.Succeeded + folderBatch.Succeeded,
            Failed = subBatch.Failed + folderBatch.Failed,
            Message = $"به‌روزرسانی جامع انجام شد — درگاه ارسال: {subBatch.Succeeded}/{subBatch.Total} | کارپوشه: {folderBatch.Succeeded}/{folderBatch.Total}",
            Items = subBatch.Items.Concat(folderBatch.Items).ToList()
        };
    }

    public async Task<List<MdnTimeRangeInquiryItemDto>> InquiryByTimeRangeAsync(MdnTimeRangeInquiryRequest req, string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var results = await _gateway.InquiryByTimeRangeAsync(profile, req);

        var refs = results.Where(r => !string.IsNullOrWhiteSpace(r.ReferenceNumber)).Select(r => r.ReferenceNumber).ToList();
        var localMap = await _db.MdnInvoices.AsNoTracking()
            .Where(i => i.ReferenceNumber != null && refs.Contains(i.ReferenceNumber))
            .ToDictionaryAsync(i => i.ReferenceNumber!, i => new { i.Id, i.TaxId }, StringComparer.OrdinalIgnoreCase);

        AddLog(null, "TIME_RANGE_INQUIRY", true, "OK", null, null, null,
            $"استعلام بازه‌ای درگاه انجام شد ({results.Count} رکورد برگشتی).", null, username);
        await _db.SaveChangesAsync();

        return results.Select(r =>
        {
            localMap.TryGetValue(r.ReferenceNumber ?? "", out var matched);
            return new MdnTimeRangeInquiryItemDto
            {
                ReferenceNumber = r.ReferenceNumber ?? "",
                Uid = r.Uid ?? "",
                Status = r.Status ?? "",
                PacketType = r.PacketType ?? "",
                FiscalId = r.FiscalId,
                MatchedInvoiceId = matched?.Id,
                MatchedTaxId = matched?.TaxId,
                Errors = r.Data?.Error?.Select(e => $"[{e.Code}] {e.Message}").ToList() ?? new List<string>(),
                Warnings = r.Data?.Warning?.Select(w => $"[{w.Code}] {w.Message}").ToList() ?? new List<string>()
            };
        }).ToList();
    }

    // ==================== ثبت پرداخت در سامانه مودیان (RegisterPaymentRequestAsync) ====================

    public async Task<MdnInvoicePaymentDto> RegisterPaymentAsync(
        int invoiceId,
        MdnRegisterPaymentRequest req,
        string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var companyId = profile.CompanyId ?? 0;

        if (req.PaidAmount <= 0)
            throw new InvalidOperationException("مبلغ پرداختی باید بزرگ‌تر از صفر (به ریال) باشد.");

        var inv = await _db.MdnInvoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

        if (inv.Subject == MdnInvoiceSubject.Cancellation || inv.FolderStatus == MdnFolderStatus.Cancelled)
            throw new InvalidOperationException("امکان ثبت پرداخت روی صورتحساب ابطالی وجود ندارد.");

        var payment = new MdnInvoicePayment
        {
            CompanyId = companyId,
            InvoiceId = inv.Id,
            PaymentMethod = req.PaymentMethod,
            PaidAmount = req.PaidAmount,
            PaymentDate = req.PaymentDate,
            TerminalNumber = NullIfBlank(req.TerminalNumber),
            ReferenceNumber = NullIfBlank(req.ReferenceNumber),
            SwitchNumber = NullIfBlank(req.SwitchNumber),
            AcceptorNumber = NullIfBlank(req.AcceptorNumber),
            MaskedCardNumber = NullIfBlank(req.MaskedCardNumber),
            PayerNationalId = NullIfBlank(req.PayerNationalId),
            Note = NullIfBlank(req.Note),
            SyncState = MdnPaymentSyncState.LocalDraft,
            CreatedAt = DateTime.Now
        };
        inv.Payments.Add(payment);
        await _db.SaveChangesAsync();

        if (req.SendToMoadianNow)
        {
            if (string.IsNullOrWhiteSpace(inv.TaxId))
                throw new InvalidOperationException("پرداخت به صورت محلی ذخیره شد؛ اما برای ارسال به سامانه مودیان، ابتدا صورتحساب باید ارسال و دارای TaxId باشد.");

            await ExecuteSdkPaymentSyncAsync(profile, inv, payment, username);
        }
        else
        {
            RecalculatePaymentSummary(inv);
            await _db.SaveChangesAsync();
        }

        return MapPaymentDto(payment);
    }

    public async Task<MdnInvoicePaymentDto> RetryPaymentSyncAsync(int invoiceId, int paymentId, string username)
    {
        var (profile, _) = await GetOrCreateProfileEntityAsync();
        var inv = await _db.MdnInvoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

        var payment = inv.Payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("رکورد پرداخت یافت نشد.");

        if (string.IsNullOrWhiteSpace(inv.TaxId))
            throw new InvalidOperationException("ابتدا صورتحساب باید به سامانه مودیان ارسال شده و دارای شماره مالیاتی ۲۲ کاراکتری (TaxId) باشد.");

        await ExecuteSdkPaymentSyncAsync(profile, inv, payment, username);
        return MapPaymentDto(payment);
    }

    public async Task DeletePaymentAsync(int invoiceId, int paymentId)
    {
        await RequireActiveCompanyAsync();
        var inv = await _db.MdnInvoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

        var payment = inv.Payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("رکورد پرداخت یافت نشد.");

        if (payment.SyncState == MdnPaymentSyncState.Registered)
            throw new InvalidOperationException("این پرداخت در سامانه مودیان ثبت قطعی شده و قابل حذف نیست.");

        _db.MdnInvoicePayments.Remove(payment);
        inv.Payments.Remove(payment);
        RecalculatePaymentSummary(inv);
        await _db.SaveChangesAsync();
    }

    private async Task ExecuteSdkPaymentSyncAsync(
        MdnTaxpayerProfile profile,
        MdnInvoice inv,
        MdnInvoicePayment payment,
        string username)
    {
        try
        {
            var result = await _gateway.RegisterPaymentAsync(profile, inv.TaxId!, payment);
            var errs = result?.Error?.Select(e => $"[{e.Code}] {e.Message}").ToList() ?? new List<string>();

            if (result?.RequestStatus == RequestStatus.SUCCESS && errs.Count == 0)
            {
                payment.SyncState = MdnPaymentSyncState.Registered;
                payment.RegisteredAt = DateTime.Now;
                payment.LastError = null;

                AddLog(inv.Id, "PAYMENT_REGISTER", true, "SUCCESS", inv.Uid, payment.ReferenceNumber, inv.TaxId,
                    $"ثبت پرداخت به مبلغ {payment.PaidAmount:N0} ریال با روش {MdnUiLabels.PaymentMethodTitle(payment.PaymentMethod)} در سامانه مودیان تأیید شد.",
                    null, username);
            }
            else
            {
                payment.SyncState = result?.RequestStatus == RequestStatus.TIMEOUT
                    ? MdnPaymentSyncState.InProgress
                    : MdnPaymentSyncState.Failed;
                payment.LastError = Truncate(errs.Count > 0 ? string.Join(" | ", errs) : $"وضعیت برگشتی: {result?.RequestStatus}", 1000);

                AddLog(inv.Id, "PAYMENT_REGISTER", false, result?.RequestStatus.ToString() ?? "FAILED",
                    inv.Uid, payment.ReferenceNumber, inv.TaxId,
                    $"عدم تأیید ثبت پرداخت در سامانه مودیان: {payment.LastError}",
                    errs.Count > 0 ? JsonSerializer.Serialize(errs, LogJsonOpts) : null,
                    username);
            }
        }
        catch (Exception ex)
        {
            payment.SyncState = MdnPaymentSyncState.Failed;
            payment.LastError = Truncate(ex.Message, 1000);
            AddLog(inv.Id, "PAYMENT_REGISTER", false, "ERROR", inv.Uid, payment.ReferenceNumber, inv.TaxId,
                $"خطا در ارسال پرداخت به سامانه مودیان: {ex.Message}", null, username);
        }

        RecalculatePaymentSummary(inv);
        inv.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ==================== توابع کمکی محاسبه و نگاشت ====================

    private static void RecalculatePaymentSummary(MdnInvoice inv)
    {
        if (inv.Subject == MdnInvoiceSubject.Cancellation || inv.FolderStatus == MdnFolderStatus.Cancelled)
        {
            inv.PaidAmount = 0;
            inv.PaymentStatus = MdnPaymentStatus.NotApplicable;
            return;
        }

        var confirmedPayments = inv.Payments
            .Where(p => p.SyncState is MdnPaymentSyncState.Registered or MdnPaymentSyncState.IncludedInInvoice)
            .Sum(p => p.PaidAmount);

        if (inv.SettlementMethod == MdnSettlementMethod.Cash && confirmedPayments == 0 && inv.SubmissionStatus == MdnSubmissionStatus.Success)
        {
            inv.PaidAmount = inv.TotalAmount;
            inv.PaymentStatus = MdnPaymentStatus.Paid;
            return;
        }

        inv.PaidAmount = Math.Min(inv.TotalAmount, confirmedPayments);
        inv.PaymentStatus = inv.PaidAmount <= 0
            ? MdnPaymentStatus.Unpaid
            : inv.PaidAmount >= inv.TotalAmount
                ? MdnPaymentStatus.Paid
                : MdnPaymentStatus.PartiallyPaid;
    }

    private static MdnFolderStatus MapFolderStatus(string raw) => raw switch
    {
        "CONFIRMED" or "APPROVED" or "ACCEPTED" => MdnFolderStatus.Approved,
        "SYSTEM_CONFIRMED" or "AUTO_CONFIRMED" => MdnFolderStatus.SystemApproved,
        "PENDING" or "WAITING" or "WAITING_FOR_CONFIRMATION" or "IN_PROGRESS" => MdnFolderStatus.Pending,
        "REJECTED" or "DISAPPROVED" => MdnFolderStatus.Rejected,
        "CANCELLED" or "CANCELED" or "REVOKED" => MdnFolderStatus.Cancelled,
        "NOT_FOUND" => MdnFolderStatus.NotFound,
        _ => MdnFolderStatus.Unknown
    };

    private void AddLog(
        int? invoiceId,
        string actionType,
        bool isSuccess,
        string statusCode,
        string? uid,
        string? referenceNumber,
        string? taxId,
        string summary,
        string? errorDetailsJson,
        string? username)
    {
        _db.MdnSubmissionLogs.Add(new MdnSubmissionLog
        {
            CompanyId = _db.ActiveCompanyId ?? _db.ResolveDefaultCompanyId(),
            InvoiceId = invoiceId,
            ActionType = actionType,
            CreatedAt = DateTime.Now,
            IsSuccess = isSuccess,
            StatusCode = Truncate(statusCode, 60) ?? "INFO",
            Uid = Truncate(uid, 64),
            ReferenceNumber = Truncate(referenceNumber, 80),
            TaxId = Truncate(taxId, 30),
            Summary = Truncate(summary, 600) ?? "",
            ErrorDetailsJson = errorDetailsJson,
            CreatedByUsername = Truncate(username, 100)
        });
    }

    private static MdnProfileDto MapProfileDto(MdnTaxpayerProfile p, string companyName)
    {
        int? daysRemaining = null;
        var isExpired = false;
        if (p.CertificateNotAfter.HasValue)
        {
            var diff = (p.CertificateNotAfter.Value - DateTime.UtcNow).TotalDays;
            daysRemaining = (int)Math.Floor(diff);
            isExpired = diff < 0;
        }

        return new MdnProfileDto
        {
            Id = p.Id,
            CompanyId = p.CompanyId ?? 0,
            CompanyName = companyName,
            TradeName = p.TradeName,
            TaxpayerType = p.TaxpayerType,
            EconomicCode = p.EconomicCode,
            NationalId = p.NationalId,
            PostalCode = p.PostalCode,
            BranchCode = p.BranchCode,
            Address = p.Address,
            MemoryId = p.MemoryId,
            Environment = p.Environment,
            ProductionBaseUrl = p.ProductionBaseUrl,
            SandboxBaseUrl = p.SandboxBaseUrl,
            SignatureMode = p.SignatureMode,
            HasPrivateKey = !string.IsNullOrWhiteSpace(p.EncryptedPrivateKeyPem),
            HasCertificate = !string.IsNullOrWhiteSpace(p.EncryptedCertificateBase64),
            CertificateSubject = p.CertificateSubject,
            CertificateIssuer = p.CertificateIssuer,
            CertificateSerial = p.CertificateSerial,
            CertificateThumbprint = p.CertificateThumbprint,
            CertificateNotBefore = p.CertificateNotBefore?.ToLocalTime(),
            CertificateNotAfter = p.CertificateNotAfter?.ToLocalTime(),
            CertificateDaysRemaining = daysRemaining,
            IsCertificateExpired = isExpired,
            Pkcs11LibraryPath = p.Pkcs11LibraryPath,
            Pkcs11CertificateAlias = p.Pkcs11CertificateAlias,
            HasPkcs11Pin = !string.IsNullOrWhiteSpace(p.EncryptedPkcs11Pin),
            NextSerial = p.NextSerial,
            DefaultVatRate = p.DefaultVatRate,
            IsActive = p.IsActive,
            LastConnectionTestAt = p.LastConnectionTestAt,
            LastConnectionStatus = p.LastConnectionStatus,
            LastFiscalStatus = p.LastFiscalStatus
        };
    }

    private static MdnInvoiceSummaryDto MapInvoiceSummaryDto(MdnInvoice i) => new()
    {
        Id = i.Id,
        InternalNumber = i.InternalNumber,
        SerialDecimal = i.SerialDecimal,
        SerialHex = i.SerialHex,
        TaxId = i.TaxId,
        ReferenceTaxId = i.ReferenceTaxId,
        ParentInvoiceId = i.ParentInvoiceId,
        Uid = i.Uid,
        ReferenceNumber = i.ReferenceNumber,
        IssueDate = i.IssueDate,
        InvoiceType = i.InvoiceType,
        Pattern = i.Pattern,
        Subject = i.Subject,
        SettlementMethod = i.SettlementMethod,
        BuyerId = i.BuyerId,
        BuyerName = i.BuyerName,
        BuyerEconomicCode = i.BuyerEconomicCode,
        BuyerNationalId = i.BuyerNationalId,
        TotalBeforeDiscount = i.TotalBeforeDiscount,
        TotalDiscount = i.TotalDiscount,
        TotalAfterDiscount = i.TotalAfterDiscount,
        TotalVat = i.TotalVat,
        TotalOtherTaxes = i.TotalOtherTaxes,
        TotalAmount = i.TotalAmount,
        CashAmount = i.CashAmount,
        CreditAmount = i.CreditAmount,
        PaidAmount = i.PaidAmount,
        SubmissionStatus = i.SubmissionStatus,
        FolderStatus = i.FolderStatus,
        FolderStatusRaw = i.FolderStatusRaw,
        Article6Status = i.Article6Status,
        PaymentStatus = i.PaymentStatus,
        SentAt = i.SentAt,
        LastInquiryAt = i.LastInquiryAt,
        LastFolderInquiryAt = i.LastFolderInquiryAt,
        LastErrorCode = i.LastErrorCode,
        LastErrorMessage = i.LastErrorMessage,
        SourceFacInvoiceId = i.SourceFacInvoiceId,
        SourceFacInvoiceNumber = i.SourceFacInvoiceNumber
    };

    private static MdnInvoiceDetailDto MapInvoiceDetailDto(MdnInvoice i, List<MdnInvoice> related) => new()
    {
        Id = i.Id,
        InternalNumber = i.InternalNumber,
        SerialDecimal = i.SerialDecimal,
        SerialHex = i.SerialHex,
        TaxId = i.TaxId,
        ReferenceTaxId = i.ReferenceTaxId,
        ParentInvoiceId = i.ParentInvoiceId,
        Uid = i.Uid,
        ReferenceNumber = i.ReferenceNumber,
        MemoryId = i.MemoryId,
        IssueDate = i.IssueDate,
        CreateDate = i.CreateDate,
        InvoiceType = i.InvoiceType,
        Pattern = i.Pattern,
        Subject = i.Subject,
        SettlementMethod = i.SettlementMethod,
        SellerEconomicCode = i.SellerEconomicCode,
        SellerTradeName = i.SellerTradeName,
        SellerPostalCode = i.SellerPostalCode,
        SellerBranchCode = i.SellerBranchCode,
        BuyerId = i.BuyerId,
        BuyerName = i.BuyerName,
        BuyerType = i.BuyerType,
        BuyerEconomicCode = i.BuyerEconomicCode,
        BuyerNationalId = i.BuyerNationalId,
        BuyerPostalCode = i.BuyerPostalCode,
        BuyerBranchCode = i.BuyerBranchCode,
        BuyerPassportNumber = i.BuyerPassportNumber,
        ContractRegistrationNumber = i.ContractRegistrationNumber,
        BillId = i.BillId,
        CustomsLicenseNumber = i.CustomsLicenseNumber,
        CustomsCode = i.CustomsCode,
        CustomsDeclarationNumber = i.CustomsDeclarationNumber,
        CustomsDeclarationDate = i.CustomsDeclarationDate,
        Tax17 = i.Tax17,
        TotalBeforeDiscount = i.TotalBeforeDiscount,
        TotalDiscount = i.TotalDiscount,
        TotalAfterDiscount = i.TotalAfterDiscount,
        TotalVat = i.TotalVat,
        TotalOtherTaxes = i.TotalOtherTaxes,
        TotalAmount = i.TotalAmount,
        CashAmount = i.CashAmount,
        CreditAmount = i.CreditAmount,
        PaidAmount = i.PaidAmount,
        SubmissionStatus = i.SubmissionStatus,
        FolderStatus = i.FolderStatus,
        FolderStatusRaw = i.FolderStatusRaw,
        Article6Status = i.Article6Status,
        PaymentStatus = i.PaymentStatus,
        SentAt = i.SentAt,
        LastInquiryAt = i.LastInquiryAt,
        LastFolderInquiryAt = i.LastFolderInquiryAt,
        LastErrorCode = i.LastErrorCode,
        LastErrorMessage = i.LastErrorMessage,
        Description = i.Description,
        SourceFacInvoiceId = i.SourceFacInvoiceId,
        SourceFacInvoiceNumber = i.SourceFacInvoiceNumber,
        Lines = i.Lines.Select(l => new MdnInvoiceLineDto
        {
            Id = l.Id,
            RowNumber = l.RowNumber,
            CatalogItemId = l.CatalogItemId,
            Sstid = l.Sstid,
            Description = l.Description,
            UnitCode = l.UnitCode,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            PriceBeforeDiscount = l.PriceBeforeDiscount,
            DiscountAmount = l.DiscountAmount,
            PriceAfterDiscount = l.PriceAfterDiscount,
            VatRate = l.VatRate,
            VatAmount = l.VatAmount,
            OtherTaxSubject = l.OtherTaxSubject,
            OtherTaxRate = l.OtherTaxRate,
            OtherTaxAmount = l.OtherTaxAmount,
            OtherLegalFeeSubject = l.OtherLegalFeeSubject,
            OtherLegalFeeRate = l.OtherLegalFeeRate,
            OtherLegalFeeAmount = l.OtherLegalFeeAmount,
            ConstructionFee = l.ConstructionFee,
            SellerProfit = l.SellerProfit,
            BrokerFee = l.BrokerFee,
            CurrencyCode = l.CurrencyCode,
            ExchangeRate = l.ExchangeRate,
            NetWeight = l.NetWeight,
            HsCode = l.HsCode,
            ContractRegistrationNumber = l.ContractRegistrationNumber,
            TotalLineAmount = l.TotalLineAmount
        }).ToList(),
        Payments = i.Payments.Select(MapPaymentDto).ToList(),
        Logs = i.Logs.Select(MapLogDto).ToList(),
        RelatedInvoices = related.Select(MapInvoiceSummaryDto).ToList()
    };

    private static MdnInvoicePaymentDto MapPaymentDto(MdnInvoicePayment p) => new()
    {
        Id = p.Id,
        PaymentMethod = p.PaymentMethod,
        PaidAmount = p.PaidAmount,
        PaymentDate = p.PaymentDate,
        SwitchNumber = p.SwitchNumber,
        AcceptorNumber = p.AcceptorNumber,
        TerminalNumber = p.TerminalNumber,
        ReferenceNumber = p.ReferenceNumber,
        MaskedCardNumber = p.MaskedCardNumber,
        PayerNationalId = p.PayerNationalId,
        Note = p.Note,
        SyncState = p.SyncState,
        RegisteredAt = p.RegisteredAt,
        LastError = p.LastError
    };

    private static MdnSubmissionLogDto MapLogDto(MdnSubmissionLog l) => new()
    {
        Id = l.Id,
        ActionType = l.ActionType,
        CreatedAt = l.CreatedAt,
        IsSuccess = l.IsSuccess,
        StatusCode = l.StatusCode,
        Uid = l.Uid,
        ReferenceNumber = l.ReferenceNumber,
        TaxId = l.TaxId,
        Summary = l.Summary,
        ErrorDetailsJson = l.ErrorDetailsJson,
        CreatedByUsername = l.CreatedByUsername
    };

    private static long ToWholeRial(decimal value)
        => checked((long)decimal.Round(value, 0, MidpointRounding.AwayFromZero));

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
