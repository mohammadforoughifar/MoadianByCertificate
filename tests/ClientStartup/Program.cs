using Inventory.Api.Infrastructure;

// Request-level tests of the real redirect middleware, without running the API/database.
foreach (var (host, scheme) in new[]
{
    ("localhost:5100", "http"), ("127.0.0.1:5100", "http"), ("[::1]:5100", "http"), ("localhost:7021", "https")
})
{
    foreach (var path in new[] { "/", "/index.html" })
    foreach (var method in new[] { "GET", "HEAD" })
    {
        var context = Request(host, scheme, path, method);
        context.Request.QueryString = new QueryString("?returnUrl=%2Fdashboard");
        var middleware = new DevelopmentClientRedirect(_ => throw new Exception("Unexpected next middleware"),
            new ConfigurationBuilder().Build());
        await middleware.InvokeAsync(context);
        var expected = scheme == "https" ? "https://localhost:7232/" : "http://localhost:5210/";
        Check(context.Response.StatusCode == 302, "entry-page redirect");
        Check(context.Response.Headers.Location.ToString() == expected + "?returnUrl=%2Fdashboard", "client port/query preserved");
        Check(context.Response.Headers.CacheControl == "no-store", "do not cache development redirect");
    }
}

foreach (var path in new[] { "/api/health", "/api/auth/login", "/swagger", "/https-setup", "/ca.crt", "/hubs/chat", "/uploads/file.pdf" })
    await PassThrough(Request("localhost:5100", "http", path));
await PassThrough(Request("erp.example.test", "http", "/"));
await PassThrough(Request("192.168.30.104:8181", "http", "/"));
await PassThrough(Request("localhost:5100", "http", "/", "POST"));

var configuredContext = Request("localhost:5100", "http", "/");
var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Development:ClientUrl"] = "http://localhost:5300"
}).Build();
await new DevelopmentClientRedirect(_ => throw new Exception("Unexpected next middleware"), configured)
    .InvokeAsync(configuredContext);
Check(configuredContext.Response.Headers.Location == "http://localhost:5300/", "configured client address");
Console.WriteLine("Local development navigation tests passed. No database was accessed.");

static DefaultHttpContext Request(string host, string scheme, string path, string method = "GET")
{
    var context = new DefaultHttpContext();
    context.Request.Host = new HostString(host);
    context.Request.Scheme = scheme;
    context.Request.Path = path;
    context.Request.Method = method;
    return context;
}

static async Task PassThrough(DefaultHttpContext context)
{
    var called = false;
    await new DevelopmentClientRedirect(_ => { called = true; return Task.CompletedTask; }, new ConfigurationBuilder().Build())
        .InvokeAsync(context);
    Check(called && context.Response.Headers.Location.Count == 0, "non-entry request remains untouched");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
}
