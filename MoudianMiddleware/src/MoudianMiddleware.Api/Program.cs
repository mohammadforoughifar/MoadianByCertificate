using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using MoudianMiddleware.Api.Infrastructure;
using MoudianMiddleware.Api.Services;
using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer;
using MoudianMiddleware.DataLayer;
using MoudianMiddleware.DataLayer.Abstractions;

var builder = WebApplication.CreateBuilder(args);
var jwtKeyIsMissing = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOUDIAN__JWT_SIGNING_KEY"));
var jwt = JwtOptions.FromEnvironment(allowDevelopmentFallback: builder.Environment.IsDevelopment());
if (jwtKeyIsMissing && builder.Environment.IsDevelopment())
    Console.Error.WriteLine("WARNING: Using an ephemeral development-only JWT key. Tokens will stop validating after API restart.");
var clientOrigin = Environment.GetEnvironmentVariable("MOUDIAN__CLIENT_ORIGIN");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();
builder.Services.AddScoped<ICurrentUserProvider, HttpCurrentUserProvider>();
builder.Services.AddDataLayer(builder.Configuration);
builder.Services.AddCoreLayer(builder.Configuration);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<MoudianMiddleware.DataLayer.Abstractions.IPasswordHasher>(sp => sp.GetRequiredService<IPasswordHasher>());
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options => options.AddPolicy("ClientOnly", policy =>
{
    if (Uri.TryCreate(clientOrigin, UriKind.Absolute, out var origin) && (origin.Scheme == Uri.UriSchemeHttps || origin.IsLoopback))
        policy.WithOrigins(origin.GetLeftPart(UriPartial.Authority)).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var trusted = Environment.GetEnvironmentVariable("MOUDIAN__FORWARDED_HEADERS_KNOWN_PROXIES");
    if (!string.IsNullOrWhiteSpace(trusted))
        foreach (var address in trusted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Net.IPAddress.TryParse(address, out var ip))
                throw new InvalidOperationException("MOUDIAN__FORWARDED_HEADERS_KNOWN_PROXIES contains an invalid IP address.");
            options.KnownProxies.Add(ip);
        }
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "BearerOrApiKey";
    options.DefaultChallengeScheme = "BearerOrApiKey";
})
.AddPolicyScheme("BearerOrApiKey", "JWT or API key", options => options.ForwardDefaultSelector = context =>
    context.Request.Headers.ContainsKey("X-Api-Key") ? ApiKeyDefaults.Scheme : JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options => options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
    ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKey), ValidateLifetime = true,
    ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All) options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", permission));
    options.AddPolicy("ApiScope.InvoiceWrite", policy => policy.AddAuthenticationSchemes(ApiKeyDefaults.Scheme).RequireAuthenticatedUser().RequireClaim(ApiClaims.ApiScope, nameof(ApiScope.InvoiceWrite)));
    options.AddPolicy("ApiScope.InvoiceRead", policy => policy.AddAuthenticationSchemes(ApiKeyDefaults.Scheme).RequireAuthenticatedUser().RequireClaim(ApiClaims.ApiScope, nameof(ApiScope.InvoiceRead)));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthByIp", context => RateLimitPartition.GetFixedWindowLimiter("auth:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _ =>
        new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("ApiClient", context =>
    {
        var client = context.User.FindFirstValue(ApiClaims.ApiClientId) ?? "invalid-client";
        var limit = int.TryParse(context.User.FindFirstValue(ApiClaims.RequestsPerMinute), out var value) ? Math.Clamp(value, 1, 10000) : 1;
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
    });
    options.OnRejected = async (context, _) => await context.HttpContext.Response.WriteAsJsonAsync(new MoudianMiddleware.Contracts.Common.ApiResponse<object>
    { IsSuccess = false, Code = MoudianMiddleware.Contracts.Common.ApiResponseCodes.RateLimited, Message = "سقف درخواست پر شده است.", TraceId = context.HttpContext.TraceIdentifier }).ConfigureAwait(false);
});
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseRouting();
app.UseMiddleware<SafeRequestLoggingMiddleware>();
app.UseCors("ClientOnly");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.Run();

public partial class Program { }

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
