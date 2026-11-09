using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EntitleX.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EntitleX.Infrastructure.Identity;

public interface IJwtTokenService
{
    string CreateToken(
        string userId,
        string displayName,
        string email,
        IEnumerable<string> roles,
        IEnumerable<string>? customerIds = null,
        TimeSpan? lifetime = null);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string CreateToken(
        string userId,
        string displayName,
        string email,
        IEnumerable<string> roles,
        IEnumerable<string>? customerIds = null,
        TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured and at least 32 characters.");
        }

        var roleList = roles.ToList();
        foreach (var role in roleList)
        {
            if (!EntitleXRoles.All.Contains(role, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unknown role '{role}'.", nameof(roles));
            }
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        claims.AddRange(roleList.Select(r => new Claim(ClaimTypes.Role, r)));

        if (customerIds is not null)
        {
            foreach (var customerId in customerIds
                         .Where(c => !string.IsNullOrWhiteSpace(c))
                         .Select(c => c.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(HttpCurrentUser.CustomerIdsClaimType, customerId));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.Add(
            lifetime ?? TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
