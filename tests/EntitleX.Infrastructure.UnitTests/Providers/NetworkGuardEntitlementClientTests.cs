using System.Net;
using System.Text;
using EntitleX.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntitleX.Infrastructure.UnitTests.Providers;

public sealed class NetworkGuardEntitlementClientTests
{
    [Fact]
    public async Task DiscoverByCustomerAsync_MapsTenantLicensesToNormalizedDto()
    {
        const string json = """
            [
              {
                "licenseId": "NG-LIC-9001",
                "tenantExternalId": "CUST-1001",
                "skuCode": "NG-FW-ADV",
                "licenseGroup": "Firewall",
                "displayName": "Firewall Advanced",
                "validUntilUtc": "2026-09-01T00:00:00Z",
                "updatedAtUtc": "2026-01-01T00:00:00Z"
              }
            ]
            """;

        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("tenants/CUST-1001/licenses", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var factory = new StubHttpClientFactory(ProviderHttpClientNames.NetworkGuard, handler);
        var sut = new NetworkGuardEntitlementClient(
            factory,
            NullLogger<NetworkGuardEntitlementClient>.Instance);

        var results = await sut.DiscoverByCustomerAsync("CUST-1001", "corr-xyz");

        Assert.Single(results);
        Assert.Equal("NG-LIC-9001", results[0].EntitlementId);
        Assert.Equal("NetworkGuard", results[0].Provider);
        Assert.Equal("NG-FW-ADV", results[0].ProductCode);
        Assert.Equal("Firewall", results[0].ProductGroup);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

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
            return new HttpClient(_handler) { BaseAddress = new Uri("http://networkguard.test/") };
        }
    }
}
