namespace EntitleX.Application.Abstractions;

/// <summary>Authenticated caller identity sourced from JWT claims.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string UserId { get; }

    string DisplayName { get; }

    string? Email { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}
