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

    public async Task<CustomerEntitlementAggregationDto> Handle(
        SearchCustomerEntitlementsQuery request,
        CancellationToken cancellationToken)
    {
        var customerId = request.CustomerId.Trim();
        _authorization.EnsureCanSearchEntitlements(customerId);

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        var aggregated = await _aggregation.AggregateForCustomerAsync(
            customerId,
            correlationId,
            cancellationToken);

        var totalCount = aggregated.Entitlements.Count;

        // PageSize <= 0 means "return all" (backward-compatible unpaged search).
        if (request.PageSize <= 0)
        {
            return aggregated with
            {
                Page = 1,
                PageSize = totalCount == 0 ? 50 : totalCount,
                TotalCount = totalCount
            };
        }

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var pageItems = aggregated.Entitlements.Skip(skip).Take(pageSize).ToList();

        return aggregated with
        {
            Entitlements = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
