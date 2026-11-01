namespace Inventory.Api.Infrastructure;

/// <summary>
/// The source client runs on its own dev server. Never open an old published UI from API wwwroot
/// when a developer visits the local API root. API/static/download paths are not redirected.
/// </summary>
public sealed class DevelopmentClientRedirect(RequestDelegate next, IConfiguration configuration)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var localHost = request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || request.Host.Host is "127.0.0.1" or "::1" or "[::1]";
        var entryPage = request.Path == "/"
            || request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase);
        if (localHost && entryPage && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            var clientUrl = configuration["Development:ClientUrl"]
                ?? (request.IsHttps ? "https://localhost:7232" : "http://localhost:5210");
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Redirect(clientUrl.TrimEnd('/') + "/" + request.QueryString);
            return Task.CompletedTask;
        }
        return next(context);
    }
}
