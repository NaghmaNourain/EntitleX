using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.Saga.Models;
using EntitleX.BuildingBlocks.Ids;
using MediatR;

namespace EntitleX.Application.Saga.GetSaga;

public sealed class GetSagaQueryHandler : IRequestHandler<GetSagaQuery, SagaDto>
{
    private readonly ILicenseExtensionSagaRepository _sagaRepository;

    public GetSagaQueryHandler(ILicenseExtensionSagaRepository sagaRepository)
    {
        _sagaRepository = sagaRepository;
    }

    public async Task<SagaDto> Handle(GetSagaQuery query, CancellationToken cancellationToken)
    {
        var saga = await _sagaRepository.GetByRequestIdReadOnlyAsync(
            RequestId.From(query.RequestId),
            cancellationToken)
            ?? throw new NotFoundException(
                $"Saga for request '{query.RequestId}' was not found.");

        return saga.ToDto();
    }
}
