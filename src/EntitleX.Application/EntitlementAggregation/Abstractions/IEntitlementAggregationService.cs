using EntitleX.Application.EntitlementAggregation.Models;

namespace EntitleX.Application.EntitlementAggregation.Abstractions;

public interface IEntitlementAggregationService
{
    Task<CustomerEntitlementAggregationDto> AggregateForCustomerAsync(
        string customerId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
