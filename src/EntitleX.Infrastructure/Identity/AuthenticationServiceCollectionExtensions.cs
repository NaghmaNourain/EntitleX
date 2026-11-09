using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace EntitleX.Infrastructure.Identity;

public static class AuthenticationServiceCollectionExtensions
{
    public const string DefaultPolicyName = "EntitleXAuthenticated";
    public const string OidcBearerScheme = "OidcBearer";
    public const string DevBearerScheme = "DevBearer";

    public static IServiceCollection AddEntitleXAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OidcOptions>(configuration.GetSection(OidcOptions.SectionName));

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();
        var oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>()
            ?? new OidcOptions();

        var isProduction = environment?.IsProduction() == true
            || string.Equals(
                configuration["ASPNETCORE_ENVIRONMENT"],
                Environments.Production,
                StringComparison.OrdinalIgnoreCase);

        if (isProduction)
        {
            // Force-disable DevToken regardless of config drift.
            jwt.EnableDevTokenEndpoint = false;
            services.PostConfigure<JwtOptions>(options => options.EnableDevTokenEndpoint = false);

            if (!oidc.Enabled || string.IsNullOrWhiteSpace(oidc.Authority))
            {
                throw new InvalidOperationException(
                    "Production requires Oidc:Enabled=true and Oidc:Authority. " +
                    "DevToken HMAC auth is not permitted in Production.");
            }
        }

        var useOidc = oidc.Enabled && !string.IsNullOrWhiteSpace(oidc.Authority);
        var useDevJwt = !string.IsNullOrWhiteSpace(jwt.SigningKey) && jwt.SigningKey.Length >= 32;

        if (!useOidc && !useDevJwt)
        {
            throw new InvalidOperationException(
                "Configure Oidc:Authority (Oidc:Enabled=true) and/or Jwt:SigningKey (minimum 32 characters). " +
                "Set secrets via environment variables — never commit production secrets.");
        }

        if (jwt.EnableDevTokenEndpoint && !useDevJwt)
        {
            throw new InvalidOperationException(
                "Jwt:EnableDevTokenEndpoint requires Jwt:SigningKey (≥32 characters).");
        }

        services.AddHttpContextAccessor();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<Application.Abstractions.ICurrentUser, HttpCurrentUser>();

        if (useOidc && useDevJwt)
        {
            AddDualJwtBearer(services, jwt, oidc);
        }
        else if (useOidc)
        {
            AddOidcOnlyJwtBearer(services, oidc);
        }
        else
        {
            AddDevOnlyJwtBearer(services, jwt);
        }

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

    private static void AddOidcOnlyJwtBearer(IServiceCollection services, OidcOptions oidc)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureOidcJwtBearer(options, oidc));
    }

    private static void AddDevOnlyJwtBearer(IServiceCollection services, JwtOptions jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureDevJwtBearer(options, jwt));
    }

    private static void AddDualJwtBearer(
        IServiceCollection services,
        JwtOptions jwt,
        OidcOptions oidc)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme(JwtBearerDefaults.AuthenticationScheme, "OIDC or Dev JWT", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var header = context.Request.Headers.Authorization.ToString();
                    if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = header["Bearer ".Length..].Trim();
                        if (!string.IsNullOrEmpty(token) && IsOidcToken(token, oidc))
                        {
                            return OidcBearerScheme;
                        }
                    }

                    return DevBearerScheme;
                };
            })
            .AddJwtBearer(OidcBearerScheme, options => ConfigureOidcJwtBearer(options, oidc))
            .AddJwtBearer(DevBearerScheme, options => ConfigureDevJwtBearer(options, jwt));
    }

    private static bool IsOidcToken(string token, OidcOptions oidc)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var issuer = jwt.Issuer?.TrimEnd('/');
            var authority = oidc.Authority.TrimEnd('/');
            if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(authority))
            {
                return false;
            }

            return string.Equals(issuer, authority, StringComparison.OrdinalIgnoreCase)
                   || issuer.StartsWith(authority + "/", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ConfigureOidcJwtBearer(JwtBearerOptions options, OidcOptions oidc)
    {
        options.Authority = oidc.Authority.TrimEnd('/');
        options.RequireHttpsMetadata = true;
        options.SaveToken = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrWhiteSpace(oidc.Audience),
            ValidAudience = string.IsNullOrWhiteSpace(oidc.Audience) ? null : oidc.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    OidcRoleClaimMapper.ApplyRoleClaims(identity, oidc);

                    // Prefer human-readable name when Keycloak sends "name".
                    if (identity.FindFirst(ClaimTypes.Name) is null)
                    {
                        var display = identity.FindFirst("name")?.Value
                                      ?? identity.FindFirst("preferred_username")?.Value;
                        if (!string.IsNullOrWhiteSpace(display))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Name, display));
                        }
                    }

                    if (identity.FindFirst(ClaimTypes.NameIdentifier) is null)
                    {
                        var sub = identity.FindFirst("sub")?.Value;
                        if (!string.IsNullOrWhiteSpace(sub))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
                        }
                    }

                    if (identity.FindFirst(ClaimTypes.Email) is null)
                    {
                        var email = identity.FindFirst("email")?.Value;
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Email, email));
                        }
                    }
                }

                return Task.CompletedTask;
            }
        };
    }

    private static void ConfigureDevJwtBearer(JwtBearerOptions options, JwtOptions jwt)
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
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    }
}

public static class AuthorizationPolicies
{
    public const string CanCreateRequests = "CanCreateRequests";
    public const string CanDecideApprovals = "CanDecideApprovals";
    public const string CanReadApprovals = "CanReadApprovals";
    public const string CanManageReconciliation = "CanManageReconciliation";
}
