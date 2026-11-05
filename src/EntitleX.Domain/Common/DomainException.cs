namespace EntitleX.Domain.Common;

/// <summary>Thrown when a domain invariant is violated.</summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
