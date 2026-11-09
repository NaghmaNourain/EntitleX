namespace EntitleX.Application.Abstractions;

/// <summary>Authenticated caller identity sourced from JWT claims.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string UserId { get; }

    string DisplayName { get; }

    string? Email { get; }

    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// Customer ids the caller may access (JWT <c>customer_ids</c> claim).
    /// Empty means no customer-scoped ACL — privileged roles bypass via AuthZ service.
    /// </summary>
    IReadOnlyList<string> CustomerIds { get; }

    bool IsInRole(string role);
}
