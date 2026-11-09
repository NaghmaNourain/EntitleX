using EntitleX.Application.EntitlementAggregation;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntitleX.Application.UnitTests.EntitlementAggregation;

public sealed class EntitlementAggregationServiceTests
{
    [Fact]
    public async Task Aggregate_MergesSuccessfulProviders_AndFlagsPartialOnFailure()
    {
        var ok = new FakeProvider("CloudShield", succeed: true,
        [
            new NormalizedEntitlementDto(
                "CS-ENT-1001",
                "CloudShield",
                "CUST-1001",
                "CS-SECURE-EDGE",
                "SecuritySuite",
                "Secure Edge",
                DateTimeOffset.Parse("2026-07-01Z"),
                DateTimeOffset.Parse("2026-01-01Z"))
        ]);

        var fail = new FakeProvider("NetworkGuard", succeed: false, []);

        var sut = new EntitlementAggregationService(
            [ok, fail],
            NullLogger<EntitlementAggregationService>.Instance);

        var result = await sut.AggregateForCustomerAsync("CUST-1001", "corr-1");

        Assert.Equal("CUST-1001", result.CustomerId);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.True(result.IsPartial);
        Assert.Single(result.Entitlements);
        Assert.Equal("CS-ENT-1001", result.Entitlements[0].EntitlementId);
        Assert.Equal(2, result.Providers.Count);
        Assert.Contains(result.Providers, p => p.Provider == "CloudShield" && p.Succeeded);
        Assert.Contains(result.Providers, p => p.Provider == "NetworkGuard" && !p.Succeeded);
    }

    [Fact]
    public async Task Aggregate_WhenAllSucceed_IsNotPartial()
    {
        var cs = new FakeProvider("CloudShield", succeed: true,
        [
            new NormalizedEntitlementDto(
                "CS-1", "CloudShield", "CUST-1", "P1", null, "A",
                DateTimeOffset.UtcNow.AddMonths(1), null)
        ]);
        var ng = new FakeProvider("NetworkGuard", succeed: true,
        [
            new NormalizedEntitlementDto(
                "NG-1", "NetworkGuard", "CUST-1", "S1", "G", "B",
                DateTimeOffset.UtcNow.AddMonths(2), null)
        ]);

        var sut = new EntitlementAggregationService(
            [cs, ng],
            NullLogger<EntitlementAggregationService>.Instance);

        var result = await sut.AggregateForCustomerAsync("CUST-1", "corr-2");

        Assert.False(result.IsPartial);
        Assert.Equal(2, result.Entitlements.Count);
    }

    private sealed class FakeProvider : IProviderEntitlementClient
    {
        private readonly bool _succeed;
        private readonly IReadOnlyList<NormalizedEntitlementDto> _items;

        public FakeProvider(string name, bool succeed, IReadOnlyList<NormalizedEntitlementDto> items)
        {
            ProviderName = name;
            _succeed = succeed;
            _items = items;
        }

        public string ProviderName { get; }

        public Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
            string customerId,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            if (!_succeed)
            {
                throw new HttpRequestException($"{ProviderName} unavailable");
            }

            return Task.FromResult(_items);
        }

        public Task<NormalizedEntitlementDto?> GetByEntitlementIdAsync(
            string entitlementId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.EntitlementId == entitlementId));

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_succeed);
    }
}
