using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Services;

public class TokenService
{
    private readonly IConfiguration _config;
    public TokenService(IConfiguration config) => _config = config;

    public string Key => _config["Jwt:Key"] ?? "RADIS-HR-V019-DOTNET8-DEFAULT-SIGNING-KEY-CHANGE-ME-32B";
    public string Issuer => _config["Jwt:Issuer"] ?? "RadisHr";
    public string Audience => _config["Jwt:Audience"] ?? "RadisHrClient";
    public int LifetimeHours => int.TryParse(_config["Jwt:LifetimeHours"], out var h) ? h : 12;

    public (string Token, DateTime ExpiresAt) Create(AppUser user)
    {
        var meta = RolePages.Map.TryGetValue(user.UserKey, out var m) ? m : default;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserKey),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.UserKey),
            new("roleTitle", user.RoleTitle),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (meta.Pages != null)
            foreach (var page in meta.Pages) claims.Add(new Claim("page", page));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(LifetimeHours);

        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
