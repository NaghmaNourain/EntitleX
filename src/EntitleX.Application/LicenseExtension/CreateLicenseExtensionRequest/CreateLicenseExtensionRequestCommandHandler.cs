using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.LicenseExtension.Models;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using MediatR;

namespace EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;

public sealed class CreateLicenseExtensionRequestCommandHandler
    : IRequestHandler<CreateLicenseExtensionRequestCommand, LicenseExtensionRequestDto>
{
    private readonly ILicenseExtensionRequestRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateLicenseExtensionRequestCommandHandler(
        ILicenseExtensionRequestRepository repository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<LicenseExtensionRequestDto> Handle(
        CreateLicenseExtensionRequestCommand request,
        CancellationToken cancellationToken)
    {
        var extensionNumber = string.IsNullOrWhiteSpace(request.ExtensionNumber)
            ? GenerateExtensionNumber()
            : request.ExtensionNumber.Trim();

        if (await _repository.ExtensionNumberExistsAsync(extensionNumber, cancellationToken))
        {
            throw new ConflictException(
                $"A license-extension request with ExtensionNumber '{extensionNumber}' already exists.");
        }

        var items = request.Items.Select(i => (
            EntitlementId: EntitlementId.From(i.EntitlementId),
            Provider: Enum.Parse<ProviderSystem>(i.Provider, ignoreCase: true),
            CurrentExpiryUtc: i.CurrentExpiryUtc,
            RequestedExpiryUtc: i.RequestedExpiryUtc));

        var aggregate = LicenseExtensionRequest.CreateFresh(
            extensionNumber,
            request.RequestedBy,
            items);

        aggregate.Submit();

        await _repository.AddAsync(aggregate, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return aggregate.ToDto();
    }

    private static string GenerateExtensionNumber() =>
        $"EXT-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
}

