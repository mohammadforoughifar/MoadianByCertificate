using System.Net.Http;
using TaxCollectData.Library.Abstraction.Clients;
using TaxCollectData.Library.Clients;
using TaxCollectData.Library.Dto;
using TaxCollectData.Library.Factories;
using TaxCollectData.Library.Properties;
using TaxCollectData.Library.Providers;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>تنها پیاده‌سازی‌ای که با SDK TaxCollectData ارتباط مستقیم دارد.</summary>
public sealed class TaxCollectDataMoadianGateway : IMoadianGateway
{
    private const string OfficialProductionBaseUrl = "https://tp.tax.gov.ir/requestsmanager";
    private readonly IMoadianSignerFactory _signerFactory;

    /// <summary>ساخت Gateway با factory استراتژی امضا.</summary>
    public TaxCollectDataMoadianGateway(IMoadianSignerFactory signerFactory) => _signerFactory = signerFactory;

    /// <inheritdoc />
    public async Task<MoadianSendReceipt> SendInvoiceAsync(
        MoadianSetting setting,
        MoadianCredential credential,
        Invoice invoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(invoice);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var signingMaterial = _signerFactory.Create(setting, credential);
            using var session = CreateSession(setting, signingMaterial.Signatory);
            var payload = MoadianInvoiceMapper.Map(setting, invoice);

            // SDK 2.0.29 exposes no CancellationToken overload; cancellation cannot interrupt an in-flight HTTP request.
            var responses = await session.Api.SendInvoicesAsync(new List<InvoiceDto> { payload }).ConfigureAwait(false);
            var response = responses?.FirstOrDefault()
                ?? throw new InvalidOperationException("SDK پاسخ خالی برای ارسال صورتحساب برگرداند.");
            return new MoadianSendReceipt(response.Uid, response.ReferenceNumber, response.TaxId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // SDK response exceptions may carry unredacted payloads. Preserve only their CLR type, never message/body.
            throw new MoadianGatewayException(exception.GetType().Name);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MoadianInquiryResult>> InquiryByReferenceNumbersAsync(
        MoadianSetting setting,
        MoadianCredential credential,
        IReadOnlyCollection<string> referenceNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(referenceNumbers);
        if (referenceNumbers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("شماره‌ی پیگیری خالی مجاز نیست.", nameof(referenceNumbers));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (referenceNumbers.Count == 0)
        {
            return Array.Empty<MoadianInquiryResult>();
        }

        try
        {
            using var signingMaterial = _signerFactory.Create(setting, credential);
            using var session = CreateSession(setting, signingMaterial.Signatory);
            var response = await session.Api.InquiryByReferenceIdAsync(
                new InquiryByReferenceNumberDto(referenceNumbers.ToList())).ConfigureAwait(false);
            return response.Select(item => new MoadianInquiryResult(
                item.ReferenceNumber,
                item.Uid,
                NormalizeStatus(item.Status))).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MoadianGatewayException(exception.GetType().Name);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MoadianInvoiceStatusResult>> InquiryByTaxIdsAsync(
        MoadianSetting setting,
        MoadianCredential credential,
        IReadOnlyCollection<string> taxIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(taxIds);
        if (taxIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("شناسه‌ی مالیاتی خالی مجاز نیست.", nameof(taxIds));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (taxIds.Count == 0)
        {
            return Array.Empty<MoadianInvoiceStatusResult>();
        }

        try
        {
            using var signingMaterial = _signerFactory.Create(setting, credential);
            using var session = CreateSession(setting, signingMaterial.Signatory);
            var response = await session.Api.InquiryInvoiceStatusAsync(taxIds.ToList()).ConfigureAwait(false);
            return response.Select(item => new MoadianInvoiceStatusResult(
                item.TaxId,
                NormalizeStatus(item.InvoiceStatus),
                NormalizeStatus(item.Article6Status))).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MoadianGatewayException(exception.GetType().Name);
        }
    }

    private static SdkSession CreateSession(MoadianSetting setting, TaxCollectData.Library.Abstraction.Cryptography.ISignatory signatory)
    {
        var baseUrl = ResolveBaseUrl(setting);
        var taxProperties = new TaxProperties(setting.MemoryId);
        var serializer = new Serializer();
        var requestProvider = new RequestProvider(new DefaultUrlProperties(baseUrl), serializer);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        try
        {
            var sdkClient = new RestSharpHttpClient(
                httpClient,
                taxProperties,
                signatory,
                new DefaultHttpHeadersProperties(),
                serializer);
            var lowLevelApi = new LowLevelTaxApi(sdkClient, requestProvider);
            var publicApi = new TaxPublicApi(lowLevelApi);
            var encryptor = new EncryptorFactory().Create(publicApi);
            var packetProvider = new PacketProviderFactory(taxProperties).CreatePacketProvider(signatory, encryptor);
            return new SdkSession(httpClient, new TaxApi(lowLevelApi, packetProvider));
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private static string NormalizeStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "SUCCESS" => "SUCCESS",
        "FAILED" => "FAILED",
        "TIMEOUT" => "TIMEOUT",
        "IN_PROGRESS" => "IN_PROGRESS",
        "NOT_FOUND" => "NOT_FOUND",
        _ => "UNKNOWN"
    };

    private static string ResolveBaseUrl(MoadianSetting setting)
    {
        if (!setting.IsActive)
        {
            throw new InvalidOperationException("اتصال مودیان برای این tenant غیرفعال است.");
        }

        var configured = setting.ActiveEnvironment switch
        {
            MoadianEnvironment.Sandbox => setting.SandboxBaseUrl,
            MoadianEnvironment.Production => setting.ProductionBaseUrl ?? OfficialProductionBaseUrl,
            _ => throw new InvalidOperationException("محیط اتصال مودی نامعتبر است.")
        };
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("آدرس Sandbox باید به‌صورت صریح تنظیم شود؛ مقدار آن حدس زده نمی‌شود.");
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || (setting.ActiveEnvironment == MoadianEnvironment.Production && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("آدرس سامانه باید URL مطلق HTTP(S) باشد؛ Production فقط HTTPS می‌پذیرد.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private sealed class SdkSession : IDisposable
    {
        public SdkSession(HttpClient httpClient, ITaxApi api)
        {
            HttpClient = httpClient;
            Api = api;
        }

        private HttpClient HttpClient { get; }
        public ITaxApi Api { get; }
        public void Dispose() => HttpClient.Dispose();
    }
}
