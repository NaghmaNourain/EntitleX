using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.Saga.Models;
using EntitleX.BuildingBlocks.Ids;
using MediatR;

namespace EntitleX.Application.Saga.GetSaga;

public sealed class GetSagaQueryHandler : IRequestHandler<GetSagaQuery, SagaDto>
{
    private readonly ILicenseExtensionSagaRepository _sagaRepository;
    private readonly IResourceAuthorizationService _authorization;

    public GetSagaQueryHandler(
        ILicenseExtensionSagaRepository sagaRepository,
        IResourceAuthorizationService authorization)
    {
        _sagaRepository = sagaRepository;
        _authorization = authorization;
    }

    public async Task<SagaDto> Handle(GetSagaQuery query, CancellationToken cancellationToken)
    {
        var requestId = RequestId.From(query.RequestId);
        await _authorization.EnsureCanReadRequestAsync(requestId, cancellationToken);

        var saga = await _sagaRepository.GetByRequestIdReadOnlyAsync(
            requestId,
            cancellationToken)
            ?? throw new NotFoundException(
                $"Saga for request '{query.RequestId}' was not found.");

        return saga.ToDto();
    }
}
