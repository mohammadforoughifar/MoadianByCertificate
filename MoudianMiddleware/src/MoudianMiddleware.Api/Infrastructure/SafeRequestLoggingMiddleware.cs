using System.Diagnostics;

namespace MoudianMiddleware.Api.Infrastructure;

public sealed class SafeRequestLoggingMiddleware(RequestDelegate next, ILogger<SafeRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();
        try { await next(context).ConfigureAwait(false); }
        finally
        {
            timer.Stop();
            logger.LogInformation("HTTP {Method} {Endpoint} {StatusCode} {ElapsedMs}ms TraceId={TraceId}", context.Request.Method,
                context.GetEndpoint()?.DisplayName ?? "unmatched", context.Response.StatusCode, timer.ElapsedMilliseconds, context.TraceIdentifier);
        }
    }
}
