using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using MediatR;

namespace EntitleX.Application.EntitlementAggregation.SearchCustomerEntitlements;

public sealed class SearchCustomerEntitlementsQueryHandler
    : IRequestHandler<SearchCustomerEntitlementsQuery, CustomerEntitlementAggregationDto>
{
    private readonly IEntitlementAggregationService _aggregation;
    private readonly IResourceAuthorizationService _authorization;

    public SearchCustomerEntitlementsQueryHandler(
        IEntitlementAggregationService aggregation,
        IResourceAuthorizationService authorization)
    {
        _aggregation = aggregation;
        _authorization = authorization;
    }

    public Task<CustomerEntitlementAggregationDto> Handle(
        SearchCustomerEntitlementsQuery request,
        CancellationToken cancellationToken)
    {
        _authorization.EnsureCanSearchEntitlements();

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        return _aggregation.AggregateForCustomerAsync(
            request.CustomerId.Trim(),
            correlationId,
            cancellationToken);
    }
}