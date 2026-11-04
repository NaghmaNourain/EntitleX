namespace CloudShield.Domain.Common;

/// <summary>Thrown when a CloudShield domain invariant is violated.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
