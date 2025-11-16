using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MiniBlob.Api.Helpers;
public static class TokenHelper
{
    public static string GenerateToken(string userName, IEnumerable<string> roles, string key, string issuer, string audience, int expireMinutes = 60)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, userName) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddMinutes(expireMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
