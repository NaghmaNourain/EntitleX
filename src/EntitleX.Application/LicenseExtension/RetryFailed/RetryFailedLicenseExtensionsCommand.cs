using EntitleX.Application.LicenseExtension.Models;
using MediatR;

namespace EntitleX.Application.LicenseExtension.RetryFailed;

public sealed record RetryFailedLicenseExtensionsCommand(
    Guid SourceRequestId,
    string IdempotencyKey,
    string? Notes = null,
    IReadOnlyList<Guid>? LogicalOperationIds = null,
    string? CorrelationId = null) : IRequest<LicenseExtensionRequestDto>;
