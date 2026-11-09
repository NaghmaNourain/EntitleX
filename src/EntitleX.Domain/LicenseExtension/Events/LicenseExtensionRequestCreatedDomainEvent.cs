using EntitleX.SharedKernel.Ids;

namespace EntitleX.Domain.LicenseExtension.Events;

public sealed record LicenseExtensionRequestCreatedDomainEvent(
    RequestId RequestId,
    string ExtensionNumber,
    DateTimeOffset OccurredAtUtc);
