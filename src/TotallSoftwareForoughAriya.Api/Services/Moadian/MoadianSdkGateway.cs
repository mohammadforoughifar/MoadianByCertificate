using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using TaxCollectData.Library.Abstraction.Clients;
using TaxCollectData.Library.Abstraction.Cryptography;
using TaxCollectData.Library.Abstraction.Properties;
using TaxCollectData.Library.Algorithms;
using TaxCollectData.Library.Clients;
using TaxCollectData.Library.Configs;
using TaxCollectData.Library.Dto;
using TaxCollectData.Library.Enums;
using TaxCollectData.Library.Exceptions;
using TaxCollectData.Library.Factories;
using TaxCollectData.Library.Models;
using TaxCollectData.Library.Properties;
using TaxCollectData.Library.Providers;

namespace Inventory.Api.Services.Moadian;

public interface IMoadianSdkGateway
{
    string GenerateTaxId(string memoryId, long serialDecimal, DateTime issueDate, DateTimeOffset? normalizedIssueOffset = null);
    string FormatSerialHex(long serialDecimal);
    InvoiceDto BuildSdkInvoiceDto(
        MdnTaxpayerProfile profile,
        MdnInvoice invoice,
        string effectiveTaxId,
        string effectiveSerialHex,
        DateTimeOffset? normalizedIssueOffset = null);
    string SerializeInvoiceJson(InvoiceDto dto);

    Task<MdnConnectionDiagnosticsDto> TestConnectionAsync(MdnTaxpayerProfile profile);
    Task<MdnTaxpayerLookupDto> LookupTaxpayerAsync(MdnTaxpayerProfile profile, string economicCode);
    Task<MdnArticle6CheckResultDto> CheckArticle6StatusAsync(MdnTaxpayerProfile profile, MdnArticle6CheckRequest request);

    Task<(string Uid, string ReferenceNumber, string TaxId)> SendInvoiceAsync(MdnTaxpayerProfile profile, InvoiceDto dto);
    Task<List<InquiryResultModel>> InquiryByReferenceNumbersAsync(MdnTaxpayerProfile profile, List<string> referenceNumbers);
    Task<List<InquiryResultModel>> InquiryByTimeRangeAsync(MdnTaxpayerProfile profile, MdnTimeRangeInquiryRequest request);
    Task<List<InvoiceStatusInquiryResponseDto>> InquiryFolderStatusesAsync(MdnTaxpayerProfile profile, List<string> taxIds);
    Task<RegisterPaymentResultModel> RegisterPaymentAsync(MdnTaxpayerProfile profile, string taxId, MdnInvoicePayment payment);
}

// سرآمد صورتحساب الکترونیکی با فیلدهای ماده ۹ و توضیحات (insr, nti1, nti2)
public sealed class MdnV79HeaderDto : HeaderDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? insr { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? nti1 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? nti2 { get; set; }
}

public sealed class MoadianSdkGateway : IMoadianSdkGateway
{
    private const string OfficialProductionUrl = "https://tp.tax.gov.ir/requestsmanager";
    private const string OfficialSandboxUrl = "https://rc.tax.gov.ir/requestsmanager";

    private static readonly JsonSerializerOptions PreviewJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new MdnV79HeaderJsonConverter(omitNullsAndIndent: true) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private static readonly TimeSpan IranStandardOffset = new(3, 30, 0);

    private readonly MoadianSecretVault _vault;

    public MoadianSdkGateway(MoadianSecretVault vault) => _vault = vault;

    // نرمال‌سازی تاریخ صدور بر اساس ساعت ایران (UTC+03:30)
    public static DateTimeOffset NormalizeIssueDateForMoadian(DateTime issueDate)
        => MdnArticle9Rules.ToNormalizedIranIssueOffset(issueDate);

    public static bool IsPastArticle9Deadline(
        DateTimeOffset normalizedIssueOffset,
        int deadlineDays = MdnArticle9Rules.DefaultSendDeadlineDays,
        DateTimeOffset? referenceNowUtc = null)
    {
        var iranNow = (referenceNowUtc ?? DateTimeOffset.UtcNow).ToOffset(IranStandardOffset);
        var normalizedDays = MdnArticle9Rules.NormalizeDeadlineDays(deadlineDays);
        return (iranNow - normalizedIssueOffset) > TimeSpan.FromDays(normalizedDays);
    }

    // نرمال‌سازی تاریخ ثبت برای صورتحساب‌های موضوع ماده ۹ (بین تاریخ صدور و زمان جاری)
    public static DateTimeOffset NormalizeArticle9RegistrationDate(
        DateTime? registrationDate,
        DateTimeOffset normalizedIssueOffset,
        DateTimeOffset? referenceNowUtc = null)
    {
        var iranNowOffset = (referenceNowUtc ?? DateTimeOffset.UtcNow).ToOffset(IranStandardOffset);
        var safeMaxIranOffset = iranNowOffset.AddSeconds(-5);
        if (safeMaxIranOffset < normalizedIssueOffset)
            safeMaxIranOffset = normalizedIssueOffset;

        if (!registrationDate.HasValue)
            return safeMaxIranOffset;

        var raw = registrationDate.Value;
        var iranWallClock = raw.Kind == DateTimeKind.Utc
            ? raw.Add(IranStandardOffset)
            : DateTime.SpecifyKind(raw, DateTimeKind.Unspecified);

        if (iranWallClock.TimeOfDay == TimeSpan.Zero)
        {
            if (iranWallClock.Date == safeMaxIranOffset.Date && safeMaxIranOffset.TimeOfDay >= new TimeSpan(3, 35, 0))
                iranWallClock = safeMaxIranOffset.DateTime;
            else
                iranWallClock = iranWallClock.Date.AddHours(12);
        }

        var candidate = new DateTimeOffset(DateTime.SpecifyKind(iranWallClock, DateTimeKind.Unspecified), IranStandardOffset);
        if (candidate < normalizedIssueOffset)
            candidate = normalizedIssueOffset;
        if (candidate > safeMaxIranOffset)
            candidate = safeMaxIranOffset;

        return candidate;
    }

    /// <summary>تبدیل ارقام فارسی/عربی به انگلیسی و حذف فاصله‌ها از کدهای عددی مالیاتی و پستی.</summary>
    public static string? NormalizeAsciiDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.Trim())
        {
            if (ch is >= '۰' and <= '۹')
                sb.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩')
                sb.Append((char)('0' + (ch - '٠')));
            else if (!char.IsWhiteSpace(ch) && ch is not ('\u200C' or '\u200E' or '\u200F' or '-'))
                sb.Append(ch);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    public string GenerateTaxId(string memoryId, long serialDecimal, DateTime issueDate, DateTimeOffset? normalizedIssueOffset = null)
    {
        if (string.IsNullOrWhiteSpace(memoryId) || memoryId.Trim().Length != 6)
            throw new InvalidOperationException("شناسه یکتای حافظه مالیاتی باید دقیقاً ۶ کاراکتر باشد (مثال: A11226).");
        var issueOffset = normalizedIssueOffset ?? NormalizeIssueDateForMoadian(issueDate);
        var provider = new TaxIdProvider(new VerhoeffAlgorithm());
        return provider.GenerateTaxId(memoryId.Trim().ToUpperInvariant(), serialDecimal, issueOffset.UtcDateTime);
    }

    public string FormatSerialHex(long serialDecimal)
        => serialDecimal.ToString("X").PadLeft(10, '0');

    public InvoiceDto BuildSdkInvoiceDto(
        MdnTaxpayerProfile profile,
        MdnInvoice invoice,
        string effectiveTaxId,
        string effectiveSerialHex,
        DateTimeOffset? normalizedIssueOffset = null)
    {
        var issueOffset = normalizedIssueOffset ?? NormalizeIssueDateForMoadian(invoice.IssueDate);
        var indatim = issueOffset.ToUnixTimeMilliseconds();

        var deadlineDays = MdnArticle9Rules.NormalizeDeadlineDays(profile.Article9SendDeadlineDays);
        var isPastDeadline = IsPastArticle9Deadline(issueOffset, deadlineDays);

        // در حالت موضوع ماده ۹، فیلدهای insr=1 و indati2m ارسال می‌شوند و در ارسال عادی هر دو null هستند
        var effectiveArticle9 = invoice.IsArticle9 && isPastDeadline;
        long? effectiveIndati2m = effectiveArticle9
            ? NormalizeArticle9RegistrationDate(invoice.RegistrationDate, issueOffset).ToUnixTimeMilliseconds()
            : null;
        int? effectiveInsr = effectiveArticle9 ? MdnArticle9Rules.Article9SendRuleCode : null;

        var nti1 = TruncateMax(NullIfBlank(invoice.Note1), MdnArticle9Rules.MaxHeaderNoteLength);
        var nti2 = TruncateMax(NullIfBlank(invoice.Note2), MdnArticle9Rules.MaxHeaderNoteLength);

        var sellerEconomic = NormalizeAsciiDigits(invoice.SellerEconomicCode)
                             ?? NormalizeAsciiDigits(profile.EconomicCode)
                             ?? string.Empty;

        // در صورتحساب ابطالی فقط فیلدهای شناسایی سرآمد و مرجع ارسال می‌شوند
        if (invoice.Subject == MdnInvoiceSubject.Cancellation)
        {
            return new InvoiceDto
            {
                Header = new MdnV79HeaderDto
                {
                    taxid = effectiveTaxId,
                    indatim = indatim,
                    indati2m = effectiveIndati2m,
                    ins = (int)MdnInvoiceSubject.Cancellation,
                    inno = effectiveSerialHex,
                    irtaxid = NullIfBlank(invoice.ReferenceTaxId),
                    tins = sellerEconomic,
                    sbc = NormalizeAsciiDigits(invoice.SellerBranchCode) ?? NormalizeAsciiDigits(profile.BranchCode),
                    insr = effectiveInsr,
                    nti1 = nti1,
                    nti2 = nti2,
                    sg = null!
                },
                Body = new List<BodyItemDto>(),
                Payments = null!,
                Extension = null!
            };
        }

        int? cdcd = null;
        if (invoice.CustomsDeclarationDate.HasValue)
        {
            var cdOffset = NormalizeIssueDateForMoadian(invoice.CustomsDeclarationDate.Value);
            cdcd = (int)(cdOffset.ToUnixTimeSeconds() / 86400);
        }

        var isType1 = invoice.InvoiceType == MdnInvoiceType.Type1;
        var isGoldPattern = invoice.Pattern == MdnInvoicePattern.GoldJewelry;

        var header = new MdnV79HeaderDto
        {
            taxid = effectiveTaxId,
            indatim = indatim,
            // در ارسال برخط عادی (خارج از ماده ۹)، ارسال indati2m و insr خارج از الگو است؛ در موضوع ماده ۹ هر دو الزامی هستند.
            indati2m = effectiveIndati2m,
            inty = (int)invoice.InvoiceType,
            inp = (int)invoice.Pattern,
            ins = (int)invoice.Subject,
            inno = effectiveSerialHex,
            irtaxid = invoice.Subject == MdnInvoiceSubject.Original ? null : NullIfBlank(invoice.ReferenceTaxId),
            tins = sellerEconomic,
            sbc = NormalizeAsciiDigits(invoice.SellerBranchCode) ?? NormalizeAsciiDigits(profile.BranchCode),
            tob = isType1 ? (int)invoice.BuyerType : null,
            tinb = isType1 ? NormalizeAsciiDigits(invoice.BuyerEconomicCode) : null,
            bid = isType1 ? NormalizeAsciiDigits(invoice.BuyerNationalId) : null,
            bpc = isType1 ? NormalizeAsciiDigits(invoice.BuyerPostalCode)! : null!,
            bbc = isType1 ? NormalizeAsciiDigits(invoice.BuyerBranchCode) : null,
            bpn = isType1 ? NullIfBlank(invoice.BuyerPassportNumber) : null,
            crn = NullIfBlank(invoice.ContractRegistrationNumber),
            billid = NormalizeAsciiDigits(invoice.BillId),
            scln = NullIfBlank(invoice.CustomsLicenseNumber),
            scc = NormalizeAsciiDigits(invoice.CustomsCode),
            cdcn = NullIfBlank(invoice.CustomsDeclarationNumber),
            cdcd = cdcd,
            setm = (int)invoice.SettlementMethod,
            tprdis = invoice.TotalBeforeDiscount,
            tdis = invoice.TotalDiscount,
            tadis = invoice.TotalAfterDiscount,
            tvam = invoice.TotalVat,
            todam = invoice.TotalOtherTaxes,
            tbill = invoice.TotalAmount,
            cap = invoice.SettlementMethod == MdnSettlementMethod.CashAndCredit ? invoice.CashAmount : null,
            insp = invoice.SettlementMethod == MdnSettlementMethod.CashAndCredit ? invoice.CreditAmount : null,
            tax17 = invoice.Tax17 is > 0 ? invoice.Tax17 : null,
            insr = effectiveInsr,
            nti1 = nti1,
            nti2 = nti2,
            sg = null!
        };

        var body = invoice.Lines
            .OrderBy(l => l.RowNumber)
            .ThenBy(l => l.Id)
            .Select(line => new BodyItemDto
            {
                sstid = NormalizeAsciiDigits(line.Sstid) ?? line.Sstid.Trim(),
                sstt = line.Description.Trim(),
                mu = NormalizeAsciiDigits(line.UnitCode) ?? line.UnitCode.Trim(),
                am = line.Quantity,
                fee = line.UnitPrice,
                cut = NullIfBlank(line.CurrencyCode)!,
                exr = line.ExchangeRate,
                prdis = line.PriceBeforeDiscount,
                dis = line.DiscountAmount,
                adis = line.PriceAfterDiscount,
                vra = line.VatRate,
                vam = line.VatAmount,
                odt = NullIfBlank(line.OtherTaxSubject)!,
                odr = line.OtherTaxRate,
                odam = line.OtherTaxAmount > 0 ? line.OtherTaxAmount : null,
                olt = NullIfBlank(line.OtherLegalFeeSubject)!,
                olr = line.OtherLegalFeeRate,
                olam = line.OtherLegalFeeAmount > 0 ? line.OtherLegalFeeAmount : null,
                consfee = isGoldPattern ? line.ConstructionFee : null,
                spro = isGoldPattern ? line.SellerProfit : null,
                bros = isGoldPattern ? line.BrokerFee : null,
                bsrn = NullIfBlank(line.ContractRegistrationNumber)!,
                nw = line.NetWeight,
                hs = NullIfBlank(line.HsCode)!,
                tsstam = line.TotalLineAmount
            })
            .ToList();

        var payments = invoice.Payments
            .Where(p => p.SyncState == MdnPaymentSyncState.IncludedInInvoice && p.PaidAmount > 0)
            .Select(p => new PaymentItemDto
            {
                iinn = NullIfBlank(p.SwitchNumber)!,
                acn = NullIfBlank(p.AcceptorNumber)!,
                trmn = NullIfBlank(p.TerminalNumber)!,
                trn = NullIfBlank(p.ReferenceNumber)!,
                pcn = NullIfBlank(p.MaskedCardNumber)!,
                pid = NullIfBlank(p.PayerNationalId)!,
                pdt = new DateTimeOffset(DateTime.SpecifyKind(p.PaymentDate, DateTimeKind.Local)).ToUnixTimeMilliseconds(),
                pmt = (int)p.PaymentMethod,
                pv = p.PaidAmount
            })
            .ToList();

        return new InvoiceDto
        {
            Header = header,
            Body = body,
            Payments = payments.Count > 0 ? payments : null!,
            Extension = null!
        };
    }

    public string SerializeInvoiceJson(InvoiceDto dto)
        => JsonSerializer.Serialize(dto, PreviewJsonOptions);

    public async Task<MdnConnectionDiagnosticsDto> TestConnectionAsync(MdnTaxpayerProfile profile)
    {
        var result = new MdnConnectionDiagnosticsDto
        {
            CheckedAt = DateTime.Now,
            ActiveEndpoint = ResolveNormalizedBaseUrl(profile)
        };

        try
        {
            using var signerScope = CreateSignatory(profile);
            result.Steps.Add(new MdnDiagnosticStepDto
            {
                Title = "بررسی گواهی امضا",
                Passed = true,
                Detail = profile.SignatureMode switch
                {
                    MdnSignatureMode.Pkcs8Certificate => $"گواهی آماده است (انقضا: {profile.CertificateNotAfter?.ToLocalTime():yyyy/MM/dd}).",
                    MdnSignatureMode.WindowsStore => $"گواهی ویندوز آماده است ({profile.CertificateThumbprint}).",
                    _ => $"توکن سخت‌افزاری آماده است ({profile.Pkcs11CertificateAlias})."
                }
            });

            using var session = CreateLightSession(profile, signerScope.Signatory);

            var serverInfo = await session.PublicApi.GetServerInformationAsync().ConfigureAwait(false);
            var serverTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(serverInfo.ServerTime).UtcDateTime;
            var drift = Math.Abs((DateTime.UtcNow - serverTimeUtc).TotalSeconds);
            var keyId = serverInfo.PublicKeys?.FirstOrDefault()?.Id;

            result.ServerTimeUtc = serverTimeUtc;
            result.ClockDriftSeconds = Math.Round(drift, 1);
            result.ServerPublicKeyId = keyId;
            result.Steps.Add(new MdnDiagnosticStepDto
            {
                Title = "ارتباط با سرور مالیاتی",
                Passed = !string.IsNullOrWhiteSpace(keyId),
                Detail = $"کلید سرور دریافت شد (اختلاف ساعت: {result.ClockDriftSeconds} ثانیه)."
            });

            var fiscal = await session.LowLevelApi.GetFiscalInformationAsync(profile.MemoryId.Trim()).ConfigureAwait(false);
            result.FiscalStatus = fiscal?.FiscalStatus;
            result.FiscalTradeName = fiscal?.NameTrade;
            result.FiscalEconomicCode = fiscal?.EconomicCode;
            result.FiscalNationalId = fiscal?.NationalId;
            result.FiscalSaleThreshold = fiscal?.SaleThreshold;
            result.Steps.Add(new MdnDiagnosticStepDto
            {
                Title = $"استعلام حافظه مالیاتی ({profile.MemoryId})",
                Passed = !string.IsNullOrWhiteSpace(fiscal?.FiscalStatus),
                Detail = $"وضعیت: {fiscal?.FiscalStatus ?? "نامشخص"} | نام تجاری: {fiscal?.NameTrade ?? "-"} | کد اقتصادی: {fiscal?.EconomicCode ?? "-"}"
            });

            if (!string.IsNullOrWhiteSpace(profile.EconomicCode))
            {
                var tp = await session.LowLevelApi.GetTaxpayerAsync(profile.EconomicCode.Trim()).ConfigureAwait(false);
                result.TaxpayerStatus = tp?.TaxpayerStatus;
                result.TaxpayerType = tp?.TaxpayerType;
                result.Steps.Add(new MdnDiagnosticStepDto
                {
                    Title = $"استعلام پرونده مالیاتی ({profile.EconomicCode})",
                    Passed = !string.IsNullOrWhiteSpace(tp?.TaxpayerStatus),
                    Detail = $"وضعیت: {tp?.TaxpayerStatus ?? "-"} | نوع مودی: {tp?.TaxpayerType ?? "-"} | نام: {tp?.NameTrade ?? "-"}"
                });
            }

            result.Success = true;
            result.Summary = $"اتصال برقرار است (حافظه: {result.FiscalStatus}).";
            return result;
        }
        catch (Exception ex)
        {
            var friendly = TranslateSdkException(ex);
            result.Success = false;
            result.Summary = friendly;
            result.Steps.Add(new MdnDiagnosticStepDto
            {
                Title = "نتیجه ارتباط با درگاه سامانه مودیان",
                Passed = false,
                Detail = friendly
            });
            return result;
        }
    }

    public async Task<MdnTaxpayerLookupDto> LookupTaxpayerAsync(MdnTaxpayerProfile profile, string economicCode)
    {
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateLightSession(profile, signerScope.Signatory);
            var tp = await session.LowLevelApi.GetTaxpayerAsync(economicCode.Trim()).ConfigureAwait(false);
            return new MdnTaxpayerLookupDto
            {
                EconomicCode = economicCode.Trim(),
                NameTrade = tp?.NameTrade,
                TaxpayerStatus = tp?.TaxpayerStatus,
                TaxpayerType = tp?.TaxpayerType,
                NationalId = tp?.NationalId,
                PostalCode = tp?.PostalcodeTaxpayer,
                Address = tp?.AddressTaxpayer
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    public async Task<MdnArticle6CheckResultDto> CheckArticle6StatusAsync(MdnTaxpayerProfile profile, MdnArticle6CheckRequest request)
    {
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateLightSession(profile, signerScope.Signatory);

            var httpReq = session.RequestProvider.GetTaxpayerArticle6Status(
                new Article6StatusDto(request.EconomicCode.Trim(), request.VatValue, request.Period));
            var nonceReq = session.RequestProvider.GetNonceRequest();
            var model = await session.HttpClientAdapter.SendRequestAsync<Article6StatusModel>(httpReq, nonceReq).ConfigureAwait(false);

            return new MdnArticle6CheckResultDto
            {
                EconomicCode = request.EconomicCode.Trim(),
                VatValue = request.VatValue,
                Period = request.Period,
                Article6RemainStatus = model?.Article6RemainStatus ?? false,
                Message = model?.Article6RemainStatus == true
                    ? "حد مجاز ماده ۶ قانون پایانه‌های فروشگاهی برای این مبلغ برقرار است."
                    : "حد مجاز ماده ۶ برای این مبلغ/دوره تکمیل شده یا غیرمجاز است."
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    public async Task<(string Uid, string ReferenceNumber, string TaxId)> SendInvoiceAsync(MdnTaxpayerProfile profile, InvoiceDto dto)
    {
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateFullTaxApiSession(profile, signerScope.Signatory);
            var responses = await session.TaxApi.SendInvoicesAsync(new List<InvoiceDto> { dto }).ConfigureAwait(false);
            var first = responses?.FirstOrDefault()
                ?? throw new InvalidOperationException("درگاه سامانه مودیان پاسخ خالی برای ارسال صورتحساب بازگرداند.");
            return (first.Uid ?? string.Empty, first.ReferenceNumber ?? string.Empty, first.TaxId ?? dto.Header.taxid);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    public async Task<List<InquiryResultModel>> InquiryByReferenceNumbersAsync(
        MdnTaxpayerProfile profile,
        List<string> referenceNumbers)
    {
        if (referenceNumbers.Count == 0) return new List<InquiryResultModel>();
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateLightSession(profile, signerScope.Signatory);
            var dto = new InquiryByReferenceNumberDto(referenceNumbers);
            return await session.LowLevelApi.InquiryByReferenceIdAsync(dto).ConfigureAwait(false)
                ?? new List<InquiryResultModel>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    public async Task<List<InquiryResultModel>> InquiryByTimeRangeAsync(
        MdnTaxpayerProfile profile,
        MdnTimeRangeInquiryRequest request)
    {
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateLightSession(profile, signerScope.Signatory);

            RequestStatus? sdkStatus = request.Status?.Trim().ToUpperInvariant() switch
            {
                "SUCCESS" => RequestStatus.SUCCESS,
                "FAILED" => RequestStatus.FAILED,
                "TIMEOUT" => RequestStatus.TIMEOUT,
                _ => null
            };

            var pageZeroIndexed = Math.Max(0, request.PageNumber - 1);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var dto = new InquiryByTimeRangeDto(
                request.StartDate,
                request.EndDate,
                new Pageable(pageZeroIndexed, pageSize),
                sdkStatus);

            return await session.LowLevelApi.InquiryByTimeAsync(dto).ConfigureAwait(false)
                ?? new List<InquiryResultModel>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    public async Task<List<InvoiceStatusInquiryResponseDto>> InquiryFolderStatusesAsync(
        MdnTaxpayerProfile profile,
        List<string> taxIds)
    {
        if (taxIds.Count == 0) return new List<InvoiceStatusInquiryResponseDto>();
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateLightSession(profile, signerScope.Signatory);
            return await session.LowLevelApi.InquiryInvoiceStatusAsync(taxIds).ConfigureAwait(false)
                ?? new List<InvoiceStatusInquiryResponseDto>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    public async Task<RegisterPaymentResultModel> RegisterPaymentAsync(
        MdnTaxpayerProfile profile,
        string taxId,
        MdnInvoicePayment payment)
    {
        try
        {
            using var signerScope = CreateSignatory(profile);
            using var session = CreateLightSession(profile, signerScope.Signatory);

            var sdkMethod = payment.PaymentMethod switch
            {
                MdnPaymentMethod.Cheque => PaymentMethod.CHEQUE,
                MdnPaymentMethod.Barter => PaymentMethod.BARTER,
                MdnPaymentMethod.Cash => PaymentMethod.CASH,
                MdnPaymentMethod.Pos => PaymentMethod.POS,
                MdnPaymentMethod.Internet => PaymentMethod.INTERNET,
                MdnPaymentMethod.CardToCard => PaymentMethod.CARD,
                MdnPaymentMethod.BankTransfer => PaymentMethod.TRANSFER,
                _ => PaymentMethod.OTHER
            };

            var dto = new RegisterPaymentRequestDto
            {
                TaxId = taxId.Trim(),
                PaidAmount = payment.PaidAmount,
                PaymentDate = new DateTimeOffset(DateTime.SpecifyKind(payment.PaymentDate, DateTimeKind.Local)).ToUnixTimeMilliseconds(),
                PaymentMethod = sdkMethod,
                TerminalNumber = payment.TerminalNumber?.Trim() ?? string.Empty,
                ReferenceNumber = payment.ReferenceNumber?.Trim() ?? string.Empty
            };

            return await session.LowLevelApi.RegisterPaymentRequestAsync(dto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateSdkException(ex));
        }
    }

    // ساخت نشست و امضاکننده در حافظه

    private SignatoryScope CreateSignatory(MdnTaxpayerProfile profile)
    {
        var companyId = profile.CompanyId ?? 0;
        if (!profile.IsActive)
            throw new InvalidOperationException("پروفایل اتصال مودیان برای این شرکت غیرفعال شده است.");
        if (string.IsNullOrWhiteSpace(profile.MemoryId) || profile.MemoryId.Trim().Length != 6)
            throw new InvalidOperationException("شناسه یکتای حافظه مالیاتی ۶ کاراکتری در تنظیمات مودیان ثبت نشده است.");

        if (profile.SignatureMode == MdnSignatureMode.Pkcs8Certificate)
        {
            if (string.IsNullOrWhiteSpace(profile.EncryptedPrivateKeyPem) ||
                string.IsNullOrWhiteSpace(profile.EncryptedCertificateBase64))
            {
                throw new InvalidOperationException("کلید خصوصی و گواهی امضای الکترونیکی برای این شرکت بارگذاری نشده است.");
            }

            if (profile.CertificateNotAfter.HasValue && profile.CertificateNotAfter.Value < DateTime.UtcNow)
                throw new InvalidOperationException($"مهلت اعتبار گواهی امضای الکترونیکی در تاریخ {profile.CertificateNotAfter.Value.ToLocalTime():yyyy/MM/dd} به پایان رسیده است.");

            var keyBytes = _vault.DecryptToBytes(profile.EncryptedPrivateKeyPem, companyId, "pkcs8-key");
            byte[]? certBytes = null;
            RSA? rsa = null;
            X509Certificate2? cert = null;
            try
            {
                certBytes = _vault.DecryptToBytes(profile.EncryptedCertificateBase64, companyId, "x509-cert");
                rsa = MoadianSecretVault.ImportRsaFromUtf8PemBytes(keyBytes);
                cert = MoadianSecretVault.LoadCertificateFromDerBase64Bytes(certBytes);
                var signatory = new SignatoryFactory(new CurrentDateProvider()).Create(rsa, cert);
                return new SignatoryScope(signatory, rsa, cert);
            }
            catch
            {
                rsa?.Dispose();
                cert?.Dispose();
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                if (certBytes is not null) CryptographicOperations.ZeroMemory(certBytes);
            }
        }

        if (profile.SignatureMode == MdnSignatureMode.Pkcs11Token)
        {
            if (string.IsNullOrWhiteSpace(profile.Pkcs11LibraryPath) ||
                string.IsNullOrWhiteSpace(profile.Pkcs11CertificateAlias) ||
                string.IsNullOrWhiteSpace(profile.EncryptedPkcs11Pin))
            {
                throw new InvalidOperationException("تنظیمات توکن سخت‌افزاری PKCS#11 (مسیر ماژول، نام مستعار گواهی و رمز پین) کامل نیست.");
            }

            var pinBytes = _vault.DecryptToBytes(profile.EncryptedPkcs11Pin, companyId, "pkcs11-pin");
            try
            {
                var pin = Encoding.UTF8.GetString(pinBytes);
                var signatory = new Pkcs11SignatoryFactory().Create(
                    profile.Pkcs11CertificateAlias.Trim(),
                    profile.Pkcs11LibraryPath.Trim(),
                    pin);
                return new SignatoryScope(signatory, null, null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pinBytes);
            }
        }

        if (profile.SignatureMode == MdnSignatureMode.WindowsStore)
        {
            if (string.IsNullOrWhiteSpace(profile.CertificateThumbprint))
                throw new InvalidOperationException("هیچ گواهی‌ای از مخزن گواهی ویندوز (certmgr) برای این شرکت انتخاب نشده است.");

            if (profile.CertificateNotAfter.HasValue && profile.CertificateNotAfter.Value < DateTime.UtcNow)
                throw new InvalidOperationException($"مهلت اعتبار گواهی امضای الکترونیکی انتخاب‌شده در تاریخ {profile.CertificateNotAfter.Value.ToLocalTime():yyyy/MM/dd} به پایان رسیده است.");

            var (cert, rsa) = MoadianWindowsStoreInspector.OpenSigningMaterial(profile.CertificateThumbprint);
            try
            {
                var signatory = new SignatoryFactory(new CurrentDateProvider()).Create(rsa, cert);
                return new SignatoryScope(signatory, rsa, cert);
            }
            catch
            {
                rsa.Dispose();
                cert.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException("حالت امضای دیجیتال پشتیبانی‌نشده است.");
    }

    private static LightSdkSession CreateLightSession(MdnTaxpayerProfile profile, ISignatory signatory)
    {
        var baseUrl = ResolveNormalizedBaseUrl(profile);
        var taxProps = new TaxProperties(profile.MemoryId.Trim().ToUpperInvariant());
        var serializer = new V79MoadianSerializer();
        var urlProps = new NormalizedMoadianUrlProperties(baseUrl);
        var requestProvider = new RequestProvider(urlProps, serializer);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        try
        {
            var sdkHttp = new RestSharpHttpClient(
                httpClient,
                taxProps,
                signatory,
                new DefaultHttpHeadersProperties(),
                serializer);
            var lowLevel = new LowLevelTaxApi(sdkHttp, requestProvider);
            var publicApi = new TaxPublicApi(lowLevel);
            return new LightSdkSession(httpClient, sdkHttp, requestProvider, lowLevel, publicApi);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private static FullSdkSession CreateFullTaxApiSession(MdnTaxpayerProfile profile, ISignatory signatory)
    {
        var light = CreateLightSession(profile, signatory);
        try
        {
            var taxProps = new TaxProperties(profile.MemoryId.Trim().ToUpperInvariant());
            var encryptor = new EncryptorFactory().Create(light.PublicApi);
            var packetProvider = new PacketProvider(taxProps, signatory, encryptor, new V79MoadianSerializer());
            var taxApi = new TaxApi(light.LowLevelApi, packetProvider);
            return new FullSdkSession(light, taxApi);
        }
        catch
        {
            light.Dispose();
            throw;
        }
    }

    internal static string ResolveNormalizedBaseUrl(MdnTaxpayerProfile profile)
    {
        var raw = profile.Environment == MdnEnvironment.Sandbox
            ? (NullIfBlank(profile.SandboxBaseUrl) ?? OfficialSandboxUrl)
            : (NullIfBlank(profile.ProductionBaseUrl) ?? OfficialProductionUrl);

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("آدرس وب‌سرویس سامانه مودیان معتبر نیست.");
        }

        var normalized = uri.ToString().TrimEnd('/');
        if (normalized.EndsWith("/api/v2", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^"/api/v2".Length];
        else if (normalized.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^"/api".Length];

        return normalized.TrimEnd('/');
    }

    private static string TranslateSdkException(Exception ex)
    {
        var root = ex is AggregateException agg && agg.InnerExceptions.Count > 0 ? agg.InnerExceptions[0] : ex;

        if (root.GetType().Name == "TaxCollectionApiException")
        {
            try
            {
                var errDto = JsonSerializer.Deserialize<ErrorResponseDto>(root.Message, JsonSerializerConfig.JsonSerializerOptions);
                if (errDto?.Errors is { Count: > 0 })
                {
                    var items = string.Join(" | ", errDto.Errors.Select(e => $"[{e.Code}] {e.Message}"));
                    var hasArt9Hint = errDto.Errors.Any(e => e.Code is "0200201" or "00107");
                    var hasIndati2mHint = errDto.Errors.Any(e => e.Code is "14003");
                    var hint = hasArt9Hint
                        ? " (مهلت ارسال عادی گذشته است؛ با گزینه ماده ۹ ارسال کنید.)"
                        : hasIndati2mHint
                            ? " (تاریخ ثبت برای صورتحساب عادی مجاز نیست.)"
                            : "";
                    return $"خطای سامانه مودیان: {items}{hint}";
                }
            }
            catch { /* در صورت عدم تطابق JSON، پیام خام خوانده می‌شود */ }

            return $"خطای درگاه سامانه مودیان: {root.Message}";
        }

        if (root is UnknownResponseException unknown)
            return $"پاسخ نامنتظره از سرور سامانه مودیان (HTTP {(int)unknown.StatusCode} - {unknown.StatusCode}). آدرس درگاه یا دسترسی شبکه را بررسی کنید.";

        if (root is HttpRequestException httpEx)
            return $"عدم دسترسی شبکه به درگاه سامانه مودیان: {httpEx.Message}";

        if (root is TaskCanceledException)
            return "مهلت زمانی پاسخگویی درگاه سامانه مودیان به پایان رسید (Timeout).";

        return root.Message;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed class NormalizedMoadianUrlProperties : IUrlProperties
    {
        private readonly string _baseUrl;
        public NormalizedMoadianUrlProperties(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');
        public string GetUrl(string request) => $"{_baseUrl}/api/v2/{request.TrimStart('/')}";
    }

    private sealed class SignatoryScope : IDisposable
    {
        private readonly RSA? _rsa;
        private readonly X509Certificate2? _cert;
        public ISignatory Signatory { get; }

        public SignatoryScope(ISignatory signatory, RSA? rsa, X509Certificate2? cert)
        {
            Signatory = signatory;
            _rsa = rsa;
            _cert = cert;
        }

        public void Dispose()
        {
            _rsa?.Dispose();
            _cert?.Dispose();
        }
    }

    private sealed class LightSdkSession : IDisposable
    {
        private readonly HttpClient _http;
        public RestSharpHttpClient HttpClientAdapter { get; }
        public RequestProvider RequestProvider { get; }
        public LowLevelTaxApi LowLevelApi { get; }
        public ITaxPublicApi PublicApi { get; }

        public LightSdkSession(
            HttpClient http,
            RestSharpHttpClient httpClientAdapter,
            RequestProvider requestProvider,
            LowLevelTaxApi lowLevelApi,
            ITaxPublicApi publicApi)
        {
            _http = http;
            HttpClientAdapter = httpClientAdapter;
            RequestProvider = requestProvider;
            LowLevelApi = lowLevelApi;
            PublicApi = publicApi;
        }

        public void Dispose() => _http.Dispose();
    }

    private static string? TruncateMax(string? value, int maxLen)
        => string.IsNullOrEmpty(value) || value.Length <= maxLen ? value : value[..maxLen];

    // مبدل JSON سرآمد صورتحساب برای درج فیلدهای insr، nti1 و nti2 در بسته ارسالی
    private sealed class MdnV79HeaderJsonConverter : JsonConverter<HeaderDto>
    {
        private static readonly JsonSerializerOptions InnerWireOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly JsonSerializerOptions InnerPreviewOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = true
        };

        private readonly bool _omitNullsAndIndent;

        public MdnV79HeaderJsonConverter(bool omitNullsAndIndent) => _omitNullsAndIndent = omitNullsAndIndent;

        public override HeaderDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<MdnV79HeaderDto>(ref reader, _omitNullsAndIndent ? InnerPreviewOptions : InnerWireOptions);

        public override void Write(Utf8JsonWriter writer, HeaderDto value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, (object)value, value.GetType(), _omitNullsAndIndent ? InnerPreviewOptions : InnerWireOptions);
    }

    public sealed class V79MoadianSerializer : TaxCollectData.Library.Abstraction.Providers.ISerializer
    {
        private static readonly JsonSerializerOptions WireOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter(), new MdnV79HeaderJsonConverter(omitNullsAndIndent: false) }
        };

        public string Serialize(object dto)
            => Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(dto, WireOptions));
    }

    private sealed class FullSdkSession : IDisposable
    {
        private readonly LightSdkSession _light;
        public ITaxApi TaxApi { get; }

        public FullSdkSession(LightSdkSession light, ITaxApi taxApi)
        {
            _light = light;
            TaxApi = taxApi;
        }

        public void Dispose() => _light.Dispose();
    }
}
