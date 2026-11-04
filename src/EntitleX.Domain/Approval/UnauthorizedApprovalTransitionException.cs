using EntitleX.Domain.Common;

namespace EntitleX.Domain.Approval;

/// <summary>
/// Raised when an actor attempts to decide a stage they are not authorized for.
/// Distinct from generic <see cref="DomainException"/> so the API can map to HTTP 403.
/// </summary>
public sealed class UnauthorizedApprovalTransitionException : DomainException
{
    public UnauthorizedApprovalTransitionException(string message)
        : base(message)
    {
    }
}
