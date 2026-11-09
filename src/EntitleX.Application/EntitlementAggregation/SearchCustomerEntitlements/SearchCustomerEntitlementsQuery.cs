using EntitleX.Application.EntitlementAggregation.Models;
using MediatR;

namespace EntitleX.Application.EntitlementAggregation.SearchCustomerEntitlements;

public sealed record SearchCustomerEntitlementsQuery(
    string CustomerId,
    string? CorrelationId = null,
    int Page = 1,
    int PageSize = 50) : IRequest<CustomerEntitlementAggregationDto>;
