using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EntitleX.Infrastructure.Identity;

public static class AuthenticationServiceCollectionExtensions
{
    public const string DefaultPolicyName = "EntitleXAuthenticated";

    public static IServiceCollection AddEntitleXAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured (minimum 32 characters). " +
                "Set a strong secret via configuration or environment variable Jwt__SigningKey.");
        }

        services.AddHttpContextAccessor();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<Application.Abstractions.ICurrentUser, HttpCurrentUser>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                    NameClaimType = System.Security.Claims.ClaimTypes.Name
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(DefaultPolicyName, policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy(
                AuthorizationPolicies.CanCreateRequests,
                policy => policy.RequireRole(
                    Domain.Identity.EntitleXRoles.RenewalRepresentative,
                    Domain.Identity.EntitleXRoles.PlatformAdmin));

            options.AddPolicy(
                AuthorizationPolicies.CanDecideApprovals,
                policy => policy.RequireRole(
                    Domain.Identity.EntitleXRoles.RenewalManager,
                    Domain.Identity.EntitleXRoles.DealDeskManager,
                    Domain.Identity.EntitleXRoles.PlatformAdmin));

            options.AddPolicy(
                AuthorizationPolicies.CanReadApprovals,
                policy => policy.RequireAuthenticatedUser());

            options.AddPolicy(
                AuthorizationPolicies.CanManageReconciliation,
                policy => policy.RequireRole(
                    Domain.Identity.EntitleXRoles.PlatformAdmin));
        });

        return services;
    }
}

public static class AuthorizationPolicies
{
    public const string CanCreateRequests = "CanCreateRequests";
    public const string CanDecideApprovals = "CanDecideApprovals";
    public const string CanReadApprovals = "CanReadApprovals";
    public const string CanManageReconciliation = "CanManageReconciliation";
}
