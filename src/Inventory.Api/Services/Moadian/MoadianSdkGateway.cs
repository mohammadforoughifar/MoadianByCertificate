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
    string GenerateTaxId(string memoryId, long serialDecimal, DateTime issueDate);
    string FormatSerialHex(long serialDecimal);
    InvoiceDto BuildSdkInvoiceDto(MdnTaxpayerProfile profile, MdnInvoice invoice, string effectiveTaxId, string effectiveSerialHex);
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

/// <summary>
/// تنها لایهٔ ارتباط مستقیم با بستهٔ رسمی TaxCollectData.Library نسخهٔ 2.0.29.
/// شامل اصلاح باگ Path.Combine در DefaultUrlProperties روی ویندوز و پشتیبانی کامل از:
///   • امضای JWS با گواهی امضای X.509 در حافظه (بدون نوشتن فایل کلید روی دیسک) یا توکن PKCS#11
///   • رمزنگاری JWE با کلید عمومی سازمان
///   • استعلام وضعیت پردازش بسته + استعلام وضعیت کارپوشه و ماده ۶ + ثبت پرداخت (RegisterPaymentRequestAsync)
/// </summary>
public sealed class MoadianSdkGateway : IMoadianSdkGateway
{
    private const string OfficialProductionUrl = "https://tp.tax.gov.ir/requestsmanager";
    private const string OfficialSandboxUrl = "https://rc.tax.gov.ir/requestsmanager";

    private static readonly JsonSerializerOptions PreviewJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly MoadianSecretVault _vault;

    public MoadianSdkGateway(MoadianSecretVault vault) => _vault = vault;

    public string GenerateTaxId(string memoryId, long serialDecimal, DateTime issueDate)
    {
        if (string.IsNullOrWhiteSpace(memoryId) || memoryId.Trim().Length != 6)
            throw new InvalidOperationException("شناسه یکتای حافظه مالیاتی باید دقیقاً ۶ کاراکتر باشد (مثال: A11226).");
        var provider = new TaxIdProvider(new VerhoeffAlgorithm());
        return provider.GenerateTaxId(memoryId.Trim().ToUpperInvariant(), serialDecimal, issueDate);
    }

    public string FormatSerialHex(long serialDecimal)
        => serialDecimal.ToString("X").PadLeft(10, '0');

    public InvoiceDto BuildSdkInvoiceDto(
        MdnTaxpayerProfile profile,
        MdnInvoice invoice,
        string effectiveTaxId,
        string effectiveSerialHex)
    {
        var indatim = new DateTimeOffset(DateTime.SpecifyKind(invoice.IssueDate, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var indati2m = new DateTimeOffset(DateTime.SpecifyKind(invoice.CreateDate, DateTimeKind.Local)).ToUnixTimeMilliseconds();

        int? cdcd = null;
        if (invoice.CustomsDeclarationDate.HasValue)
            cdcd = (int)(new DateTimeOffset(invoice.CustomsDeclarationDate.Value).ToUnixTimeSeconds() / 86400);

        var isType1 = invoice.InvoiceType == MdnInvoiceType.Type1;
        var sellerEconomic = NullIfBlank(invoice.SellerEconomicCode) ?? profile.EconomicCode.Trim();

        var header = new HeaderDto
        {
            taxid = effectiveTaxId,
            indatim = indatim,
            indati2m = indati2m,
            inty = (int)invoice.InvoiceType,
            inp = (int)invoice.Pattern,
            ins = (int)invoice.Subject,
            inno = effectiveSerialHex,
            irtaxid = invoice.Subject == MdnInvoiceSubject.Original ? null : NullIfBlank(invoice.ReferenceTaxId),
            tins = sellerEconomic,
            sbc = NullIfBlank(invoice.SellerBranchCode) ?? NullIfBlank(profile.BranchCode),
            tob = isType1 ? (int)invoice.BuyerType : null,
            tinb = isType1 ? NullIfBlank(invoice.BuyerEconomicCode) : null,
            bid = isType1 ? NullIfBlank(invoice.BuyerNationalId) : null,
            bpc = isType1 ? (NullIfBlank(invoice.BuyerPostalCode) ?? string.Empty) : null!,
            bbc = isType1 ? NullIfBlank(invoice.BuyerBranchCode) : null,
            bpn = isType1 ? NullIfBlank(invoice.BuyerPassportNumber) : null,
            crn = NullIfBlank(invoice.ContractRegistrationNumber),
            billid = NullIfBlank(invoice.BillId),
            scln = NullIfBlank(invoice.CustomsLicenseNumber),
            scc = NullIfBlank(invoice.CustomsCode),
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
            sg = null!
        };

        var body = invoice.Lines
            .OrderBy(l => l.RowNumber)
            .ThenBy(l => l.Id)
            .Select(line => new BodyItemDto
            {
                sstid = line.Sstid.Trim(),
                sstt = line.Description.Trim(),
                mu = line.UnitCode.Trim(),
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
                consfee = line.ConstructionFee,
                spro = line.SellerProfit,
                bros = line.BrokerFee,
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
                Title = "بارگذاری کلید خصوصی و گواهی امضای الکترونیکی در حافظه",
                Passed = true,
                Detail = profile.SignatureMode == MdnSignatureMode.Pkcs8Certificate
                    ? $"گواهی X.509 آماده است (انقضا: {profile.CertificateNotAfter?.ToLocalTime():yyyy/MM/dd})."
                    : $"توکن سخت‌افزاری PKCS#11 آماده است ({profile.Pkcs11CertificateAlias})."
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
                Title = "دریافت زمان سرور و کلید عمومی رمزنگاری سازمان (GetServerInformation)",
                Passed = !string.IsNullOrWhiteSpace(keyId),
                Detail = $"کلید عمومی سرور دریافت شد (kid={keyId ?? "—"}، اختلاف ساعت سرور: {result.ClockDriftSeconds} ثانیه)."
            });

            var fiscal = await session.LowLevelApi.GetFiscalInformationAsync(profile.MemoryId.Trim()).ConfigureAwait(false);
            result.FiscalStatus = fiscal?.FiscalStatus;
            result.FiscalTradeName = fiscal?.NameTrade;
            result.FiscalEconomicCode = fiscal?.EconomicCode;
            result.FiscalNationalId = fiscal?.NationalId;
            result.FiscalSaleThreshold = fiscal?.SaleThreshold;
            result.Steps.Add(new MdnDiagnosticStepDto
            {
                Title = $"استعلام وضعیت حافظه مالیاتی ({profile.MemoryId}) با توکن امضاشده JWS",
                Passed = !string.IsNullOrWhiteSpace(fiscal?.FiscalStatus),
                Detail = $"وضعیت حافظه: {fiscal?.FiscalStatus ?? "نامشخص"} | نام تجاری: {fiscal?.NameTrade ?? "—"} | کد اقتصادی ثبت‌شده: {fiscal?.EconomicCode ?? "—"}"
            });

            if (!string.IsNullOrWhiteSpace(profile.EconomicCode))
            {
                var tp = await session.LowLevelApi.GetTaxpayerAsync(profile.EconomicCode.Trim()).ConfigureAwait(false);
                result.TaxpayerStatus = tp?.TaxpayerStatus;
                result.TaxpayerType = tp?.TaxpayerType;
                result.Steps.Add(new MdnDiagnosticStepDto
                {
                    Title = $"استعلام پرونده مالیاتی مودی ({profile.EconomicCode})",
                    Passed = !string.IsNullOrWhiteSpace(tp?.TaxpayerStatus),
                    Detail = $"وضعیت پرونده: {tp?.TaxpayerStatus ?? "—"} | نوع مودی: {tp?.TaxpayerType ?? "—"} | نام: {tp?.NameTrade ?? "—"}"
                });
            }

            result.Success = true;
            result.Summary = $"اتصال و احراز هویت گواهی امضای الکترونیکی با موفقیت تأیید شد (حافظه مالیاتی: {result.FiscalStatus}).";
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

    // ==================== ساخت نشست و امضاکننده در حافظه ====================

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

        throw new InvalidOperationException("حالت امضای دیجیتال پشتیبانی‌نشده است.");
    }

    private static LightSdkSession CreateLightSession(MdnTaxpayerProfile profile, ISignatory signatory)
    {
        var baseUrl = ResolveNormalizedBaseUrl(profile);
        var taxProps = new TaxProperties(profile.MemoryId.Trim().ToUpperInvariant());
        var serializer = new Serializer();
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
            var packetProvider = new PacketProviderFactory(taxProps).CreatePacketProvider(signatory, encryptor);
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
                    return $"خطای درگاه سامانه مودیان: {items}";
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
