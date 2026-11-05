using EntitleX.Application.Saga.Models;
using MediatR;

namespace EntitleX.Application.Saga.GetSaga;

public sealed record GetSagaQuery(Guid RequestId) : IRequest<SagaDto>;
