using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.LicenseExtension.Models;
using EntitleX.BuildingBlocks.Ids;
using MediatR;

namespace EntitleX.Application.LicenseExtension.GetLicenseExtensionRequest;

public sealed class GetLicenseExtensionRequestQueryHandler
    : IRequestHandler<GetLicenseExtensionRequestQuery, LicenseExtensionRequestDto>
{
    private readonly ILicenseExtensionRequestRepository _repository;
    private readonly IResourceAuthorizationService _authorization;

    public GetLicenseExtensionRequestQueryHandler(
        ILicenseExtensionRequestRepository repository,
        IResourceAuthorizationService authorization)
    {
        _repository = repository;
        _authorization = authorization;
    }

    public async Task<LicenseExtensionRequestDto> Handle(
        GetLicenseExtensionRequestQuery request,
        CancellationToken cancellationToken)
    {
        var aggregate = await _repository.GetByIdAsync(RequestId.From(request.Id), cancellationToken)
            ?? throw new NotFoundException(
                $"License-extension request '{request.Id}' was not found.");

        _authorization.EnsureCanReadRequest(aggregate);

        return aggregate.ToDto();
    }
}
