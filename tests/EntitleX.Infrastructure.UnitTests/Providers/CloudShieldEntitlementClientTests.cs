using System.Net;
using System.Text;
using EntitleX.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntitleX.Infrastructure.UnitTests.Providers;

public sealed class CloudShieldEntitlementClientTests
{
    [Fact]
    public async Task DiscoverByCustomerAsync_NormalizesProviderPayload()
    {
        const string json = """
            [
              {
                "entitlementKey": "CS-ENT-1001",
                "customerAccountId": "CUST-1001",
                "productCode": "CS-SECURE-EDGE",
                "productGroup": "SecuritySuite",
                "displayName": "Secure Edge",
                "expiresAtUtc": "2026-07-01T00:00:00Z",
                "updatedAtUtc": "2026-01-01T00:00:00Z"
              }
            ]
            """;

        var handler = new StubHandler((request) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("accounts/CUST-1001/entitlements", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var factory = new StubHttpClientFactory(ProviderHttpClientNames.CloudShield, handler);
        var sut = new CloudShieldEntitlementClient(factory, NullLogger<CloudShieldEntitlementClient>.Instance);

        var results = await sut.DiscoverByCustomerAsync("CUST-1001", "corr-abc");

        Assert.Single(results);
        Assert.Equal("CS-ENT-1001", results[0].EntitlementId);
        Assert.Equal("CloudShield", results[0].Provider);
        Assert.Equal("CUST-1001", results[0].CustomerId);
        Assert.Equal("CS-SECURE-EDGE", results[0].ProductCode);
        Assert.Equal("SecuritySuite", results[0].ProductGroup);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly string _expectedName;
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(string expectedName, HttpMessageHandler handler)
        {
            _expectedName = expectedName;
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            Assert.Equal(_expectedName, name);
            return new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://cloudshield.test/")
            };
        }
    }
}
