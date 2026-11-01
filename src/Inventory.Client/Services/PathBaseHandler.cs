namespace Inventory.Client.Services;

/// <summary>
/// پشتیبانی از نصب برنامه زیر یک مسیر پایه (مثلاً Application «erp» در IIS: http://server/erp/).
/// HttpClient آدرس‌هایی مثل «/api/attachments/…» (با اسلش اول) را نسبت به ریشهٔ سایت حل می‌کند و مسیر پایه
/// حذف می‌شود؛ این هندلر برای همان میزبان، مسیر پایه را دوباره اضافه می‌کند. وقتی برنامه روی ریشهٔ سایت است
/// (مسیر پایه خالی) هیچ کاری انجام نمی‌دهد.
/// </summary>
public sealed class PathBaseHandler : DelegatingHandler
{
    private readonly Uri _base;
    private readonly string _basePath;

    public PathBaseHandler(Uri baseAddress)
    {
        _base = baseAddress;
        _basePath = baseAddress.AbsolutePath.TrimEnd('/');
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.RequestUri = Rewrite(request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>منطق خالص بازنویسی آدرس — جدا شده تا بدون مرورگر قابل تست باشد.</summary>
    public Uri? Rewrite(Uri? uri)
    {
        if (_basePath.Length == 0 || uri is null || !uri.IsAbsoluteUri) return uri;
        if (Uri.Compare(uri, _base, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0)
            return uri;

        var path = uri.AbsolutePath;
        if (path.Equals(_basePath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(_basePath + "/", StringComparison.OrdinalIgnoreCase))
            return uri;

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + _basePath + uri.PathAndQuery + uri.Fragment);
    }
}
