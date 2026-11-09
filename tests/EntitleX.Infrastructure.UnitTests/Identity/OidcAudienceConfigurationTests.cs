using EntitleX.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EntitleX.Infrastructure.UnitTests.Identity;

public sealed class OidcAudienceConfigurationTests
{
    [Fact]
    public void NonDevelopment_Oidc_Requires_Audience()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "true",
            ["Oidc:Authority"] = "https://login.example.com/",
            ["Oidc:Audience"] = "",
            ["Jwt:EnableDevTokenEndpoint"] = "false",
            ["Jwt:SigningKey"] = "EntitleX-Should-Not-Matter-In-Staging-Key-32+"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddEntitleXAuthentication(config, new StubHostEnvironment(Environments.Staging)));

        Assert.Contains("Oidc:Audience", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_Oidc_Requires_Audience()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "true",
            ["Oidc:Authority"] = "https://login.example.com/",
            ["Oidc:Audience"] = "   ",
            ["Jwt:EnableDevTokenEndpoint"] = "false"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddEntitleXAuthentication(config, new StubHostEnvironment(Environments.Production)));

        Assert.Contains("Oidc:Audience", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Development_Oidc_Allows_Empty_Audience()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "true",
            ["Oidc:Authority"] = "http://localhost:8080/realms/entitlex",
            ["Oidc:Audience"] = "",
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
