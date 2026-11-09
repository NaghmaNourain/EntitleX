using EntitleX.Infrastructure.Notifications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EntitleX.Infrastructure.UnitTests.Notifications;

public sealed class SesCredentialConfigurationTests
{
    [Fact]
    public void NonDevelopment_rejects_test_access_keys()
    {
        var options = new SesEmailOptions
        {
            ServiceUrl = "http://localhost:4566",
            AccessKey = "test",
            SecretKey = "test",
            Region = "us-east-1"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SesEmailSender.CreateClient(options, new StubHostEnvironment(Environments.Staging)));

        Assert.Contains("test", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Development_allows_localstack_test_keys()
    {
        var options = new SesEmailOptions
        {
            ServiceUrl = "http://localhost:4566",
            AccessKey = "test",
            SecretKey = "test",
            Region = "us-east-1"
        };

        using var client = SesEmailSender.CreateClient(
            options,
            new StubHostEnvironment(Environments.Development));

        Assert.NotNull(client);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "EntitleX.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
