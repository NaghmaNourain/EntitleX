namespace EntitleX.Application.EntitlementAggregation.Models;

public sealed record ProviderContributionDto(
    string Provider,
    bool Succeeded,
    int EntitlementCount,
    string? Error,
    TimeSpan Duration);
