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
        var customerId = request.CustomerId.Trim();
        _authorization.EnsureCanSearchEntitlements(customerId);

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        return _aggregation.AggregateForCustomerAsync(
            customerId,
            correlationId,
            cancellationToken);
    }
}