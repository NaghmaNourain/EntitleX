using System.Security.Claims;
using EntitleX.Domain.Identity;
using EntitleX.Infrastructure.Identity;

namespace EntitleX.Infrastructure.UnitTests.Identity;

public sealed class OidcRoleClaimMapperTests
{
    [Fact]
    public void ExtractMappedRoles_maps_flat_roles_claim()
    {
        var options = new OidcOptions { RoleClaimType = "roles" };
        var claims = new[]
        {
            new Claim("roles", EntitleXRoles.RenewalManager),
            new Claim("roles", EntitleXRoles.PlatformAdmin),
            new Claim("roles", "offline_access")
        };

        var mapped = OidcRoleClaimMapper.ExtractMappedRoles(claims, options);

        Assert.Equal(
            [EntitleXRoles.PlatformAdmin, EntitleXRoles.RenewalManager],
            mapped);
    }

    [Fact]
    public void ExtractMappedRoles_applies_role_map()
    {
        var options = new OidcOptions
        {
            RoleClaimType = "roles",
            RoleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["entitlex.renewal.manager"] = EntitleXRoles.RenewalManager,
                ["entitlex.admin"] = EntitleXRoles.PlatformAdmin
            }
        };

        var claims = new[]
        {
            new Claim("roles", "entitlex.renewal.manager"),
            new Claim("roles", "entitlex.admin"),
            new Claim("roles", "some-other-group")
        };

        var mapped = OidcRoleClaimMapper.ExtractMappedRoles(claims, options);

        Assert.Equal(
            [EntitleXRoles.PlatformAdmin, EntitleXRoles.RenewalManager],
            mapped);
    }

    [Fact]
    public void ExtractMappedRoles_parses_realm_access_json()
    {
        var options = new OidcOptions { RoleClaimType = "realm_access.roles" };
        var claims = new[]
        {
            new Claim(
                "realm_access",
                """{"roles":["RenewalRepresentative","default-roles-entitlex","offline_access"]}""")
        };

        var mapped = OidcRoleClaimMapper.ExtractMappedRoles(claims, options);

        Assert.Equal([EntitleXRoles.RenewalRepresentative], mapped);
    }

    [Fact]
    public void MapToEntitleXRoles_ignores_unknown_and_blank()
    {
        var mapped = OidcRoleClaimMapper.MapToEntitleXRoles(
            ["", "  ", "NotARole", EntitleXRoles.DealDeskManager],
            roleMap: null);

        Assert.Equal([EntitleXRoles.DealDeskManager], mapped);
    }

    [Fact]
    public void ApplyRoleClaims_adds_claim_types_role_without_duplicates()
    {
        var options = new OidcOptions { RoleClaimType = "roles" };
        var identity = new ClaimsIdentity(
        [
            new Claim("roles", EntitleXRoles.RenewalRepresentative),
            new Claim(ClaimTypes.Role, EntitleXRoles.RenewalRepresentative)
        ], authenticationType: "test");

        OidcRoleClaimMapper.ApplyRoleClaims(identity, options);

        var roles = identity.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        Assert.Equal([EntitleXRoles.RenewalRepresentative], roles);
    }

    [Fact]
    public void ExtractMappedRoles_parses_json_array_claim_value()
    {
        var options = new OidcOptions { RoleClaimType = "roles" };
        var claims = new[]
        {
            new Claim("roles", """["DealDeskManager","PlatformAdmin"]""")
        };

        var mapped = OidcRoleClaimMapper.ExtractMappedRoles(claims, options);

        Assert.Equal(
            [EntitleXRoles.DealDeskManager, EntitleXRoles.PlatformAdmin],
            mapped);
    }
}
