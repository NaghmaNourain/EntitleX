using FluentValidation;

namespace EntitleX.Application.Audit.GetRequestAudit;

public sealed class GetRequestAuditValidator : AbstractValidator<GetRequestAuditQuery>
{
    public GetRequestAuditValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
    }
}
