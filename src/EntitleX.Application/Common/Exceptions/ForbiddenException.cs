namespace EntitleX.Application.Common.Exceptions;

/// <summary>Raised when the caller is authenticated but not allowed to perform the action.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
