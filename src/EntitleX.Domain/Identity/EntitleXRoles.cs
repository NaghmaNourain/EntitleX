namespace EntitleX.Domain.Identity;

/// <summary>Platform RBAC role names embedded in JWT <c>role</c> claims.</summary>
public static class EntitleXRoles
{
    public const string RenewalRepresentative = "RenewalRepresentative";
    public const string RenewalManager = "RenewalManager";
    public const string DealDeskManager = "DealDeskManager";
    public const string PlatformAdmin = "PlatformAdmin";

    public static readonly IReadOnlyList<string> All =
    [
        RenewalRepresentative,
        RenewalManager,
        DealDeskManager,
        PlatformAdmin
    ];
}
