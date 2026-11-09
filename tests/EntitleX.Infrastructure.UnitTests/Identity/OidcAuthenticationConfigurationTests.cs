using EntitleX.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EntitleX.Infrastructure.UnitTests.Identity;

public sealed class OidcAuthenticationConfigurationTests
{
    [Fact]
    public void Production_requires_oidc_authority()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "false",
            ["Jwt:SigningKey"] = "EntitleX-Prod-Would-Come-From-Secrets-Mgr-32+",
            ["Jwt:EnableDevTokenEndpoint"] = "false"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddEntitleXAuthentication(config, new StubHostEnvironment(Environments.Production)));

        Assert.Contains("Oidc:Authority", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_disables_dev_token_even_when_flag_set()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "true",
            ["Oidc:Authority"] = "https://login.example.com/",
            ["Oidc:Audience"] = "entitlex-api",
            ["Jwt:EnableDevTokenEndpoint"] = "true",
            ["Jwt:SigningKey"] = "EntitleX-Should-Not-Matter-In-Prod-Key-32+"
        });

        services.AddEntitleXAuthentication(config, new StubHostEnvironment(Environments.Production));
        using var sp = services.BuildServiceProvider();
        var jwt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value;

        Assert.False(jwt.EnableDevTokenEndpoint);
    }

    [Fact]
    public void Development_accepts_hmac_only_when_oidc_disabled()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "false",
            ["Jwt:Issuer"] = "EntitleX",
            ["Jwt:Audience"] = "EntitleX",
            ["Jwt:SigningKey"] = "EntitleX-Dev-Only-Signing-Key-Change-Me-32+",
            ["Jwt:EnableDevTokenEndpoint"] = "true"
        });

        services.AddEntitleXAuthentication(config, new StubHostEnvironment(Environments.Development));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IJwtTokenService>());
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "EntitleX.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
