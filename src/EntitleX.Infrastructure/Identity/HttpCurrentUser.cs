using System.Security.Claims;
using EntitleX.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace EntitleX.Infrastructure.Identity;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub")
        ?? string.Empty;

    public string DisplayName =>
        Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue("name")
        ?? UserId;

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue("email");

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            if (Principal is null)
            {
                return Array.Empty<string>();
            }

            var fromRoleClaim = Principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
            var fromRoles = Principal.FindAll("roles").Select(c => c.Value);
            return fromRoleClaim
                .Concat(fromRoles)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
