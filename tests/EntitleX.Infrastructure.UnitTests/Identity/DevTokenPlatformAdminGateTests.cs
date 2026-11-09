using EntitleX.Domain.Identity;
using EntitleX.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.UnitTests.Identity;

/// <summary>
/// Mirrors DevTokenEndpoints PlatformAdmin gate logic (H5) without hosting the full API.
/// </summary>
public sealed class DevTokenPlatformAdminGateTests
{
    [Fact]
    public void AllowDevPlatformAdmin_Defaults_False()
    {
        var options = new JwtOptions();
        Assert.False(options.AllowDevPlatformAdmin);
    }

    [Theory]
    [InlineData(EntitleXRoles.RenewalRepresentative, false, true)]
    [InlineData(EntitleXRoles.RenewalManager, false, true)]
    [InlineData(EntitleXRoles.DealDeskManager, false, true)]
    [InlineData(EntitleXRoles.PlatformAdmin, false, false)]
    [InlineData(EntitleXRoles.PlatformAdmin, true, true)]
    public void PlatformAdmin_Mint_Requires_Explicit_Flag(
        string role,
        bool allowDevPlatformAdmin,
        bool expectedAllowed)
    {
        var jwt = Options.Create(new JwtOptions
        {
            EnableDevTokenEndpoint = true,
            AllowDevPlatformAdmin = allowDevPlatformAdmin,
            SigningKey = "EntitleX-Dev-Only-Signing-Key-Change-Me-32+"
        });

        var allowed = IsPlatformAdminMintAllowed(role, jwt.Value);
        Assert.Equal(expectedAllowed, allowed);
    }

    /// <summary>Same rule as <c>DevTokenEndpoints.MintAsync</c>.</summary>
    private static bool IsPlatformAdminMintAllowed(string role, JwtOptions jwt)
    {
        if (!EntitleXRoles.All.Contains(role, StringComparer.Ordinal))
        {
            return false;
        }

        if (string.Equals(role, EntitleXRoles.PlatformAdmin, StringComparison.Ordinal)
            && !jwt.AllowDevPlatformAdmin)
        {
            return false;
        }

        return true;
    }
}
