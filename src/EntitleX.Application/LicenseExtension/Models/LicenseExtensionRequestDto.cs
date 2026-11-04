using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.LicenseExtension.Models;

public sealed record LicenseExtensionRequestDto(
    Guid Id,
    string ExtensionNumber,
    Guid? ParentId,
    string Status,
    int ApprovalVersion,
    string? ApprovedScopeHash,
    string RequestedBy,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<LicenseExtensionRequestItemDto> Items);

public sealed record LicenseExtensionRequestItemDto(
    Guid Id,
    string EntitlementId,
    string Provider,
    Guid LogicalOperationId,
    Guid AttemptId,
    DateTimeOffset CurrentExpiryUtc,
    DateTimeOffset RequestedExpiryUtc);

public static class LicenseExtensionRequestMappings
{
    public static LicenseExtensionRequestDto ToDto(this LicenseExtensionRequest request) =>
        new(
            request.Id.Value,
            request.ExtensionNumber,
            request.ParentId?.Value,
            request.Status.ToString(),
            request.ApprovalVersion,
            request.ApprovedScopeHash,
            request.RequestedBy,
            request.CreatedAtUtc,
            request.Items.Select(i => new LicenseExtensionRequestItemDto(
                i.Id.Value,
                i.EntitlementId.Value,
                i.Provider.ToString(),
                i.LogicalOperationId.Value,
                i.AttemptId.Value,
                i.CurrentExpiryUtc,
                i.RequestedExpiryUtc)).ToList());
}
