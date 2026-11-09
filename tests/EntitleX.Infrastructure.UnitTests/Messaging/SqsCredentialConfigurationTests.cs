using EntitleX.Messaging.Options;
using EntitleX.Messaging.Sqs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EntitleX.Infrastructure.UnitTests.Messaging;

public sealed class SqsCredentialConfigurationTests
{
    [Fact]
    public void NonDevelopment_rejects_test_access_keys()
    {
        var options = new SqsOptions
        {
            ServiceUrl = "http://localhost:4566",
            AccessKey = "test",
            SecretKey = "test",
            Region = "us-east-1"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SqsMessagePublisher.CreateClient(options, new StubHostEnvironment(Environments.Production)));

        Assert.Contains("test", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Development_allows_localstack_test_keys()
    {
        var options = new SqsOptions
        {
            ServiceUrl = "http://localhost:4566",
            AccessKey = "test",
            SecretKey = "test",
            Region = "us-east-1"
        };

        using var client = SqsMessagePublisher.CreateClient(
            options,
            new StubHostEnvironment(Environments.Development));

        Assert.NotNull(client);
    }

    [Fact]
    public void Empty_service_url_uses_default_chain_without_test_keys()
    {
        var options = new SqsOptions
        {
            ServiceUrl = "",
            AccessKey = "",
            SecretKey = "",
            Region = "us-east-1"
        };

        using var client = SqsMessagePublisher.CreateClient(
            options,
            new StubHostEnvironment(Environments.Production));

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
